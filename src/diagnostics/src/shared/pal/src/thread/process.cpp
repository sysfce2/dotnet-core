// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++

Module Name:

    process.cpp

Abstract:

    Implementation of process object and functions related to processes.

--*/

#include "pal/dbgmsg.h"
SET_DEFAULT_DEBUG_CHANNEL(PROCESS); // some headers have code with asserts, so do this first

#include "pal/procobj.hpp"
#include "pal/thread.hpp"
#include "pal/file.hpp"
#include "pal/handlemgr.hpp"
#include "pal/module.h"
#include "procprivate.hpp"
#include "pal/palinternal.h"
#include "pal/process.h"
#include "pal/init.h"
#include "pal/critsect.h"
#include "pal/debug.h"
#include "pal/utils.h"
#include "pal/environ.h"
#include "pal/virtual.h"
#include "pal/stackstring.hpp"

#include <errno.h>
#if HAVE_POLL
#include <poll.h>
#else
#include "pal/fakepoll.h"
#endif  // HAVE_POLL

#include <unistd.h>
#include <fcntl.h>
#include <sys/mman.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <signal.h>
#if HAVE_PRCTL_H
#include <sys/prctl.h>
#include <sys/syscall.h>
#endif
#include <sys/wait.h>
#include <sys/time.h>
#include <sys/resource.h>
#include <debugmacrosext.h>
#include <semaphore.h>
#include <stdint.h>
#include <dlfcn.h>
#include <limits.h>
#include <vector>

#ifdef __linux__
#include <sys/syscall.h> // __NR_membarrier
// Ensure __NR_membarrier is defined for portable builds.
# if !defined(__NR_membarrier)
#  if defined(__amd64__)
#   define __NR_membarrier  324
#  elif defined(__i386__)
#   define __NR_membarrier  375
#  elif defined(__arm__)
#   define __NR_membarrier  389
#  elif defined(__aarch64__)
#   define __NR_membarrier  283
#  elif defined(__loongarch64)
#   define __NR_membarrier  283
#  else
#   error Unknown architecture
#  endif
# endif
#endif

#if HAVE_KQUEUE
#include <sys/event.h>
#endif

#ifdef __APPLE__
#include <libproc.h>
#include <pwd.h>
#include <sys/sysctl.h>
#include <sys/posix_sem.h>
#include <mach/task.h>
#include <mach/vm_map.h>
extern "C"
{
#  include <mach/thread_state.h>
}

// On macOS 26, sem_open fails if debugger and debugee are signed with different team ids.
// Use fifos instead of semaphores to avoid this issue, https://github.com/dotnet/runtime/issues/116545
#define ENABLE_RUNTIME_EVENTS_OVER_PIPES
#endif // __APPLE__

#ifdef __NetBSD__
#include <sys/cdefs.h>
#include <sys/param.h>
#include <sys/sysctl.h>
#include <kvm.h>
#endif

#ifdef __FreeBSD__
#include <sys/sysctl.h>
#include <sys/user.h>
#endif

extern char *g_szCoreCLRPath;

using namespace CorUnix;

CObjectType CorUnix::otProcess(
                otiProcess,
                NULL,   // No cleanup routine
                NULL,   // No initialization routine
                0,      // No immutable data
                NULL,   // No immutable data copy routine
                NULL,   // No immutable data cleanup routine
                sizeof(CProcProcessLocalData),
                NULL,   // No process local data cleanup routine
                0,      // No shared data
                PROCESS_ALL_ACCESS,
                CObjectType::SecuritySupported,
                CObjectType::SecurityInfoNotPersisted,
                CObjectType::UnnamedObject,
                CObjectType::CrossProcessDuplicationAllowed,
                CObjectType::WaitableObject,
                CObjectType::SingleTransitionObject,
                CObjectType::ThreadReleaseHasNoSideEffects,
                CObjectType::NoOwner
                );

//
// Helper membarrier function
//
#ifdef __NR_membarrier
# define membarrier(...)  syscall(__NR_membarrier, __VA_ARGS__)
#else
# define membarrier(...)  -ENOSYS
#endif

enum membarrier_cmd
{
    MEMBARRIER_CMD_QUERY                                 = 0,
    MEMBARRIER_CMD_GLOBAL                                = (1 << 0),
    MEMBARRIER_CMD_GLOBAL_EXPEDITED                      = (1 << 1),
    MEMBARRIER_CMD_REGISTER_GLOBAL_EXPEDITED             = (1 << 2),
    MEMBARRIER_CMD_PRIVATE_EXPEDITED                     = (1 << 3),
    MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED            = (1 << 4),
    MEMBARRIER_CMD_PRIVATE_EXPEDITED_SYNC_CORE           = (1 << 5),
    MEMBARRIER_CMD_REGISTER_PRIVATE_EXPEDITED_SYNC_CORE  = (1 << 6)
};

//
// Tracks if the OS supports FlushProcessWriteBuffers using membarrier
//
static int s_flushUsingMemBarrier = 0;

//
// Helper memory page used by the FlushProcessWriteBuffers
//
static int* s_helperPage = 0;

//
// Mutex to make the FlushProcessWriteBuffersMutex thread safe
//
pthread_mutex_t flushProcessWriteBuffersMutex;

CAllowedObjectTypes aotProcess(otiProcess);

//
// The representative IPalObject for this process
//
IPalObject* CorUnix::g_pobjProcess;

//
// Critical section that protects process data (e.g., the
// list of active threads)/
//
CRITICAL_SECTION g_csProcess;

//
// List and count of active threads
//
CPalThread* CorUnix::pGThreadList;
DWORD g_dwThreadCount;

//
// The command line and app name for the process
//
LPWSTR g_lpwstrCmdLine = NULL;
LPWSTR g_lpwstrAppDir = NULL;

// Thread ID of thread that has started the ExitProcess process
Volatile<LONG> terminator = 0;

// Process and session ID of this process.
DWORD gPID = (DWORD) -1;
DWORD gSID = (DWORD) -1;

// The lowest common supported semaphore length, including null character
// NetBSD-7.99.25: 15 characters
// MacOSX 10.11: 31 -- Core 1.0 RC2 compatibility
#if defined(__NetBSD__)
#define CLR_SEM_MAX_NAMELEN 15
#elif defined(__APPLE__)
#define CLR_SEM_MAX_NAMELEN PSEMNAMLEN
#elif defined(NAME_MAX)
#define CLR_SEM_MAX_NAMELEN (NAME_MAX - 4)
#else
// On Solaris, MAXNAMLEN is 512, which is higher than MAX_PATH defined by pal.h
#define CLR_SEM_MAX_NAMELEN MAX_PATH
#endif

static_assert_no_msg(CLR_SEM_MAX_NAMELEN <= MAX_PATH);

//
// Key used for associating CPalThread's with the underlying pthread
// (through pthread_setspecific)
//
pthread_key_t CorUnix::thObjKey;

static WCHAR W16_WHITESPACE[]= {0x0020, 0x0009, 0x000D, 0};
static WCHAR W16_WHITESPACE_DQUOTE[]= {0x0020, 0x0009, 0x000D, '"', 0};

enum FILETYPE
{
    FILE_ERROR,/*ERROR*/
    FILE_UNIX, /*Unix Executable*/
    FILE_DIR   /*Directory*/
};

#pragma pack(push,1)
// When creating the semaphore name on Mac running in a sandbox, We reference this structure as a byte array
// in order to encode its data into a string. Its important to make sure there is no padding between the fields
// and also at the end of the buffer. Hence, this structure is defined inside a pack(1)
struct UnambiguousProcessDescriptor
{
    UnambiguousProcessDescriptor()
    {
    }

    UnambiguousProcessDescriptor(DWORD processId, UINT64 disambiguationKey)
    {
        Init(processId, disambiguationKey);
    }

    void Init(DWORD processId, UINT64 disambiguationKey)
    {
        m_processId = processId;
        m_disambiguationKey = disambiguationKey;
    }
    UINT64 m_disambiguationKey;
    DWORD m_processId;
};
#pragma pack(pop)

static
DWORD
StartupHelperThread(
    LPVOID p);

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
static
DWORD
StartupHelperRuntimeEventsThread(
    LPVOID p);
#endif

static
BOOL
GetProcessIdDisambiguationKey(
    IN DWORD processId,
    OUT UINT64 *disambiguationKey);

static
void
CreateSemaphoreName(
    char semName[CLR_SEM_MAX_NAMELEN],
    LPCSTR semaphoreName,
    const UnambiguousProcessDescriptor& unambiguousProcessDescriptor,
    LPCSTR applicationGroupId);

static BOOL getFileName(LPCWSTR lpApplicationName, LPWSTR lpCommandLine, PathCharString& lpFileName);
static char ** buildArgv(LPCWSTR lpCommandLine, PathCharString& lpAppPath, UINT *pnArg);
static BOOL getPath(PathCharString& lpFileName, PathCharString& lpPathFileName);
static int checkFileType(LPCSTR lpFileName);
static BOOL PROCEndProcess(HANDLE hProcess, UINT uExitCode, BOOL bTerminateUnconditionally);

ProcessModules *GetProcessModulesFromHandle(IN HANDLE hProcess, OUT LPDWORD lpCount);
ProcessModules *CreateProcessModules(IN DWORD dwProcessId, OUT LPDWORD lpCount);
void DestroyProcessModules(IN ProcessModules *listHead);

/*++
Function:
  GetCurrentProcessId

See MSDN doc.
--*/
DWORD
PALAPI
GetCurrentProcessId(
            VOID)
{
    PERF_ENTRY(GetCurrentProcessId);
    ENTRY("GetCurrentProcessId()\n" );

    LOGEXIT("GetCurrentProcessId returns DWORD %#x\n", gPID);
    PERF_EXIT(GetCurrentProcessId);
    return gPID;
}


/*++
Function:
  GetCurrentSessionId

See MSDN doc.
--*/
DWORD
PALAPI
GetCurrentSessionId(
            VOID)
{
    PERF_ENTRY(GetCurrentSessionId);
    ENTRY("GetCurrentSessionId()\n" );

    LOGEXIT("GetCurrentSessionId returns DWORD %#x\n", gSID);
    PERF_EXIT(GetCurrentSessionId);
    return gSID;
}


/*++
Function:
  GetCurrentProcess

See MSDN doc.
--*/
HANDLE
PALAPI
GetCurrentProcess(
          VOID)
{
    PERF_ENTRY(GetCurrentProcess);
    ENTRY("GetCurrentProcess()\n" );

    LOGEXIT("GetCurrentProcess returns HANDLE %p\n", hPseudoCurrentProcess);
    PERF_EXIT(GetCurrentProcess);

    /* return a pseudo handle */
    return hPseudoCurrentProcess;
}

/*++
Function:
  CreateProcessW

Note:
  Only Standard handles need to be inherited.
  Security attributes parameters are not used.

See MSDN doc.
--*/
BOOL
PALAPI
CreateProcessW(
           IN LPCWSTR lpApplicationName,
           IN LPWSTR lpCommandLine,
           IN LPSECURITY_ATTRIBUTES lpProcessAttributes,
           IN LPSECURITY_ATTRIBUTES lpThreadAttributes,
           IN BOOL bInheritHandles,
           IN DWORD dwCreationFlags,
           IN LPVOID lpEnvironment,
           IN LPCWSTR lpCurrentDirectory,
           IN LPSTARTUPINFOW lpStartupInfo,
           OUT LPPROCESS_INFORMATION lpProcessInformation)
{
    PAL_ERROR palError = NO_ERROR;
    CPalThread *pThread;

    PERF_ENTRY(CreateProcessW);
    ENTRY("CreateProcessW(lpAppName=%p (%S), lpCmdLine=%p (%S), lpProcessAttr=%p,"
           "lpThreadAttr=%p, bInherit=%d, dwFlags=%#x, lpEnv=%p,"
           "lpCurrentDir=%p (%S), lpStartupInfo=%p, lpProcessInfo=%p)\n",
           lpApplicationName?lpApplicationName:W16_NULLSTRING,
           lpApplicationName?lpApplicationName:W16_NULLSTRING,
           lpCommandLine?lpCommandLine:W16_NULLSTRING,
           lpCommandLine?lpCommandLine:W16_NULLSTRING,lpProcessAttributes,
           lpThreadAttributes, bInheritHandles, dwCreationFlags,lpEnvironment,
           lpCurrentDirectory?lpCurrentDirectory:W16_NULLSTRING,
           lpCurrentDirectory?lpCurrentDirectory:W16_NULLSTRING,
           lpStartupInfo, lpProcessInformation);

    pThread = InternalGetCurrentThread();

    palError = InternalCreateProcess(
        pThread,
        lpApplicationName,
        lpCommandLine,
        lpProcessAttributes,
        lpThreadAttributes,
        dwCreationFlags,
        lpEnvironment,
        lpCurrentDirectory,
        lpStartupInfo,
        lpProcessInformation
        );

    if (NO_ERROR != palError)
    {
        pThread->SetLastError(palError);
    }

    LOGEXIT("CreateProcessW returns BOOL %d\n", NO_ERROR == palError);
    PERF_EXIT(CreateProcessW);

    return NO_ERROR == palError;
}

PAL_ERROR
PrepareStandardHandle(
    CPalThread *pThread,
    HANDLE hFile,
    IPalObject **ppobjFile,
    int *piFd
    )
{
    PAL_ERROR palError = NO_ERROR;
    IPalObject *pobjFile = NULL;
    IDataLock *pDataLock = NULL;
    CFileProcessLocalData *pLocalData = NULL;
    int iError = 0;

    palError = g_pObjectManager->ReferenceObjectByHandle(
        pThread,
        hFile,
        &aotFile,
        &pobjFile
        );

    if (NO_ERROR != palError)
    {
        ERROR("Bad handle passed through CreateProcess\n");
        goto PrepareStandardHandleExit;
    }

    palError = pobjFile->GetProcessLocalData(
        pThread,
        ReadLock,
        &pDataLock,
        reinterpret_cast<void **>(&pLocalData)
        );

    if (NO_ERROR != palError)
    {
        ASSERT("Unable to access file data\n");
        goto PrepareStandardHandleExit;
    }

    //
    // The passed in file needs to be inheritable
    //

    if (!pLocalData->inheritable)
    {
        ERROR("Non-inheritable handle passed through CreateProcess\n");
        palError = ERROR_INVALID_HANDLE;
        goto PrepareStandardHandleExit;
    }

    iError = fcntl(pLocalData->unix_fd, F_SETFD, 0);
    if (-1 == iError)
    {
        ERROR("Unable to remove close-on-exec for file (errno %i)\n", errno);
        palError = ERROR_INVALID_HANDLE;
        goto PrepareStandardHandleExit;
    }

    *piFd = pLocalData->unix_fd;
    pDataLock->ReleaseLock(pThread, FALSE);
    pDataLock = NULL;

    //
    // Transfer pobjFile reference to out parameter
    //

    *ppobjFile = pobjFile;
    pobjFile = NULL;

PrepareStandardHandleExit:

    if (NULL != pDataLock)
    {
        pDataLock->ReleaseLock(pThread, FALSE);
    }

    if (NULL != pobjFile)
    {
        pobjFile->ReleaseReference(pThread);
    }

    return palError;
}

PAL_ERROR
CorUnix::InternalCreateProcess(
    CPalThread *pThread,
    LPCWSTR lpApplicationName,
    LPWSTR lpCommandLine,
    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    DWORD dwCreationFlags,
    LPVOID lpEnvironment,
    LPCWSTR lpCurrentDirectory,
    LPSTARTUPINFOW lpStartupInfo,
    LPPROCESS_INFORMATION lpProcessInformation
    )
{
    PAL_ERROR palError = NO_ERROR;
    IPalObject *pobjProcess = NULL;
    IPalObject *pobjProcessRegistered = NULL;
    IDataLock *pLocalDataLock = NULL;
    CProcProcessLocalData *pLocalData;
    IDataLock *pSharedDataLock = NULL;
    CPalThread *pDummyThread = NULL;
    HANDLE hDummyThread = NULL;
    HANDLE hProcess = NULL;
    CObjectAttributes oa(NULL, lpProcessAttributes);

    IPalObject *pobjFileIn = NULL;
    int iFdIn = -1;
    IPalObject *pobjFileOut = NULL;
    int iFdOut = -1;
    IPalObject *pobjFileErr = NULL;
    int iFdErr = -1;

    pid_t processId;
    PathCharString lpFileNamePS;
    char **lppArgv = NULL;
    UINT nArg;
    int  iRet;
    char **EnvironmentArray=NULL;
    int child_blocking_pipe = -1;
    int parent_blocking_pipe = -1;

    /* Validate parameters */

    /* note : specs indicate lpApplicationName should always
       be NULL; however support for it is already implemented. Leaving the code
       in, specs can change; but rejecting non-NULL for now to conform to the
       spec. */
    if( NULL != lpApplicationName )
    {
        ASSERT("lpApplicationName should be NULL, but is %S instead\n",
               lpApplicationName);
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    if (0 != (dwCreationFlags & ~(CREATE_SUSPENDED|CREATE_NEW_CONSOLE)))
    {
        ASSERT("Unexpected creation flags (%#x)\n", dwCreationFlags);
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    /* Security attributes parameters are ignored */
    if (lpProcessAttributes != NULL &&
        (lpProcessAttributes->lpSecurityDescriptor != NULL ||
         lpProcessAttributes->bInheritHandle != TRUE))
    {
        ASSERT("lpProcessAttributes is invalid, parameter ignored (%p)\n",
               lpProcessAttributes);
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    if (lpThreadAttributes != NULL)
    {
        ASSERT("lpThreadAttributes parameter must be NULL (%p)\n",
               lpThreadAttributes);
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    /* note : Win32 crashes in this case */
    if(NULL == lpStartupInfo)
    {
        ERROR("lpStartupInfo is NULL\n");
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    /* Validate lpStartupInfo.cb field */
    if (lpStartupInfo->cb < sizeof(STARTUPINFOW))
    {
        ASSERT("lpStartupInfo parameter structure size is invalid (%u)\n",
              lpStartupInfo->cb);
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    /* lpStartupInfo should be either zero or STARTF_USESTDHANDLES */
    if (lpStartupInfo->dwFlags & ~STARTF_USESTDHANDLES)
    {
        ASSERT("lpStartupInfo parameter invalid flags (%#x)\n",
              lpStartupInfo->dwFlags);
        palError = ERROR_INVALID_PARAMETER;
        goto InternalCreateProcessExit;
    }

    /* validate given standard handles if we have any */
    if (lpStartupInfo->dwFlags & STARTF_USESTDHANDLES)
    {
        palError = PrepareStandardHandle(
            pThread,
            lpStartupInfo->hStdInput,
            &pobjFileIn,
            &iFdIn
            );

        if (NO_ERROR != palError)
        {
            goto InternalCreateProcessExit;
        }

        palError = PrepareStandardHandle(
            pThread,
            lpStartupInfo->hStdOutput,
            &pobjFileOut,
            &iFdOut
            );

        if (NO_ERROR != palError)
        {
            goto InternalCreateProcessExit;
        }

        palError = PrepareStandardHandle(
            pThread,
            lpStartupInfo->hStdError,
            &pobjFileErr,
            &iFdErr
            );

        if (NO_ERROR != palError)
        {
            goto InternalCreateProcessExit;
        }
    }

    if (!getFileName(lpApplicationName, lpCommandLine, lpFileNamePS))
    {
        ERROR("Can't find executable!\n");
        palError = ERROR_FILE_NOT_FOUND;
        goto InternalCreateProcessExit;
    }

    /* check type of file */
    iRet = checkFileType(lpFileNamePS);

    switch (iRet)
    {
        case FILE_ERROR: /* file not found, or not an executable */
            WARN ("File is not valid (%s)", lpFileNamePS.GetString());
            palError = ERROR_FILE_NOT_FOUND;
            goto InternalCreateProcessExit;

        case FILE_UNIX: /* Unix binary file */
            break;  /* nothing to do */

        case FILE_DIR:/*Directory*/
            WARN ("File is a Directory (%s)", lpFileNamePS.GetString());
            palError = ERROR_ACCESS_DENIED;
            goto InternalCreateProcessExit;
            break;

        default: /* not supposed to get here */
            ASSERT ("Invalid return type from checkFileType");
            palError = ERROR_FILE_NOT_FOUND;
            goto InternalCreateProcessExit;
    }

    /* build Argument list, lppArgv is allocated in buildArgv function and
       requires to be freed */
    lppArgv = buildArgv(lpCommandLine, lpFileNamePS, &nArg);

    /* set the Environment variable */
    if (lpEnvironment != NULL)
    {
        unsigned i;
        // Since CREATE_UNICODE_ENVIRONMENT isn't supported we know the string is ansi
        unsigned EnvironmentEntries = 0;
        // Convert the environment block to array of strings
        // Count the number of entries
        // Is it a string that contains null terminated string, the end is delimited
        // by two null in a row.
        for (i = 0; ((char *)lpEnvironment)[i]!='\0'; i++)
        {
            EnvironmentEntries ++;
            for (;((char *)lpEnvironment)[i]!='\0'; i++)
            {
            }
        }
        EnvironmentEntries++;
        EnvironmentArray = (char **)malloc(EnvironmentEntries * sizeof(char *));

        EnvironmentEntries = 0;
        // Convert the environment block to array of strings
        // Count the number of entries
        // Is it a string that contains null terminated string, the end is delimited
        // by two null in a row.
        for (i = 0; ((char *)lpEnvironment)[i]!='\0'; i++)
        {
            EnvironmentArray[EnvironmentEntries] = &((char *)lpEnvironment)[i];
            EnvironmentEntries ++;
            for (;((char *)lpEnvironment)[i]!='\0'; i++)
            {
            }
        }
        EnvironmentArray[EnvironmentEntries] = NULL;
    }

    //
    // Allocate and register the process object for the new process
    //

    palError = g_pObjectManager->AllocateObject(
        pThread,
        &otProcess,
        &oa,
        &pobjProcess
        );

    if (NO_ERROR != palError)
    {
        ERROR("Unable to allocate object for new process\n");
        goto InternalCreateProcessExit;
    }

    palError = g_pObjectManager->RegisterObject(
        pThread,
        pobjProcess,
        &aotProcess,
        &hProcess,
        &pobjProcessRegistered
        );

    //
    // pobjProcess is invalidated by the above call, so
    // NULL it out here
    //

    pobjProcess = NULL;

    if (NO_ERROR != palError)
    {
        ERROR("Unable to register new process object\n");
        goto InternalCreateProcessExit;
    }

    //
    // Create a new "dummy" thread object
    //

    palError = InternalCreateDummyThread(
        pThread,
        lpThreadAttributes,
        &pDummyThread,
        &hDummyThread
        );

    if (dwCreationFlags & CREATE_SUSPENDED)
    {
        int pipe_descs[2];

        if (-1 == pipe(pipe_descs))
        {
            ERROR("pipe() failed! error is %d (%s)\n", errno, strerror(errno));
            palError = ERROR_NOT_ENOUGH_MEMORY;
            goto InternalCreateProcessExit;
        }

        /* [0] is read end, [1] is write end */
        pDummyThread->suspensionInfo.SetBlockingPipe(pipe_descs[1]);
        parent_blocking_pipe = pipe_descs[1];
        child_blocking_pipe = pipe_descs[0];
    }

    palError = pobjProcessRegistered->GetProcessLocalData(
        pThread,
        WriteLock,
        &pLocalDataLock,
        reinterpret_cast<void **>(&pLocalData)
        );

    if (NO_ERROR != palError)
    {
        ASSERT("Unable to obtain local data for new process object\n");
        goto InternalCreateProcessExit;
    }


    /* fork the new process */
    processId = fork();

    if (processId == -1)
    {
        ASSERT("Unable to create a new process with fork()\n");
        if (-1 != child_blocking_pipe)
        {
            close(child_blocking_pipe);
            close(parent_blocking_pipe);
        }

        palError = ERROR_INTERNAL_ERROR;
        goto InternalCreateProcessExit;
    }

    /* From the time the child process begins running, to when it reaches execve,
    the child process is not a real PAL process and does not own any PAL
    resources, although it has access to the PAL resources of its parent process.
    Thus, while the child process is in this window, it is dangerous for it to affect
    its parent's PAL resources. As a consequence, no PAL code should be used
    in this window; all code should make unix calls. Note the use of _exit
    instead of exit to avoid calling PAL_Terminate and the lack of TRACE's and
    ASSERT's. */

    if (processId == 0)  /* child process */
    {
        // At this point, the PAL should be considered uninitialized for this child process.

        // Don't want to enter the init_critsec here since we're trying to avoid
        // calling PAL functions. Furthermore, nothing should be changing
        // the init_count in the child process at this point since this is the only
        // thread executing.
        init_count = 0;

        sigset_t sm;

        //
        // Clear out the signal mask for the new process.
        //

        sigemptyset(&sm);
        iRet = sigprocmask(SIG_SETMASK, &sm, NULL);
        if (iRet != 0)
        {
            _exit(EXIT_FAILURE);
        }

        if (dwCreationFlags & CREATE_SUSPENDED)
        {
            BYTE resume_code = 0;
            ssize_t read_ret;

            /* close the write end of the pipe, the child doesn't need it */
            close(parent_blocking_pipe);

            read_again:
            /* block until ResumeThread writes something to the pipe */
            read_ret = read(child_blocking_pipe, &resume_code, sizeof(resume_code));
            if (sizeof(resume_code) != read_ret)
            {
                if (read_ret == -1 && EINTR == errno)
                {
                    goto read_again;
                }
                else
                {
                    /* note : read might return 0 (and return EAGAIN) if the other
                       end of the pipe gets closed - for example because the parent
                       process dies (very) abruptly */
                    _exit(EXIT_FAILURE);
                }
            }
            if (WAKEUPCODE != resume_code)
            {
                // resume_code should always equal WAKEUPCODE.
                _exit(EXIT_FAILURE);
            }

            close(child_blocking_pipe);
        }

        /* Set the current directory */
        if (lpCurrentDirectory)
        {
            SetCurrentDirectoryW(lpCurrentDirectory);
        }

        /* Set the standard handles to the incoming values */
        if (lpStartupInfo->dwFlags & STARTF_USESTDHANDLES)
        {
            /* For each handle, we need to duplicate the incoming unix
               fd to the corresponding standard one.  The API that I use,
               dup2, will copy the source to the destination, automatically
               closing the existing destination, in an atomic way */
            if (dup2(iFdIn, STDIN_FILENO) == -1)
            {
                // Didn't duplicate standard in.
                _exit(EXIT_FAILURE);
            }

            if (dup2(iFdOut, STDOUT_FILENO) == -1)
            {
                // Didn't duplicate standard out.
                _exit(EXIT_FAILURE);
            }

            if (dup2(iFdErr, STDERR_FILENO) == -1)
            {
                // Didn't duplicate standard error.
                _exit(EXIT_FAILURE);
            }

            /* now close the original FDs, we don't need them anymore */
            close(iFdIn);
            close(iFdOut);
            close(iFdErr);
        }

        /* execute the new process */

        if (EnvironmentArray)
        {
            execve(lpFileNamePS, lppArgv, EnvironmentArray);
        }
        else
        {
            execve(lpFileNamePS, lppArgv, palEnvironment);
        }

        /* if we get here, it means the execve function call failed so just exit */
        _exit(EXIT_FAILURE);
    }

    /* parent process */

    /* close the read end of the pipe, the parent doesn't need it */
    close(child_blocking_pipe);

    /* Set the process ID */
    pLocalData->dwProcessId = processId;
    pLocalDataLock->ReleaseLock(pThread, TRUE);
    pLocalDataLock = NULL;

    //
    // Release file handle info; we don't need them anymore. Note that
    // this must happen after we've released the data locks, as
    // otherwise a deadlock could result.
    //

    if (lpStartupInfo->dwFlags & STARTF_USESTDHANDLES)
    {
        pobjFileIn->ReleaseReference(pThread);
        pobjFileIn = NULL;
        pobjFileOut->ReleaseReference(pThread);
        pobjFileOut = NULL;
        pobjFileErr->ReleaseReference(pThread);
        pobjFileErr = NULL;
    }

    /* fill PROCESS_INFORMATION structure */
    lpProcessInformation->hProcess = hProcess;
    lpProcessInformation->hThread = hDummyThread;
    lpProcessInformation->dwProcessId = processId;
    lpProcessInformation->dwThreadId_PAL_Undefined = 0;


    TRACE("New process created: id=%#x\n", processId);

InternalCreateProcessExit:

    if (NULL != pLocalDataLock)
    {
        pLocalDataLock->ReleaseLock(pThread, FALSE);
    }

    if (NULL != pSharedDataLock)
    {
        pSharedDataLock->ReleaseLock(pThread, FALSE);
    }

    if (NULL != pobjProcess)
    {
        pobjProcess->ReleaseReference(pThread);
    }

    if (NULL != pobjProcessRegistered)
    {
        pobjProcessRegistered->ReleaseReference(pThread);
    }

    if (NO_ERROR != palError)
    {
        if (NULL != hProcess)
        {
            g_pObjectManager->RevokeHandle(pThread, hProcess);
        }

        if (NULL != hDummyThread)
        {
            g_pObjectManager->RevokeHandle(pThread, hDummyThread);
        }
    }

    if (EnvironmentArray)
    {
        free(EnvironmentArray);
    }

    /* if we still have the file structures at this point, it means we
       encountered an error sometime between when we acquired them and when we
       fork()ed. We not only have to release them, we have to give them back
       their close-on-exec flag */
    if (NULL != pobjFileIn)
    {
        if(-1 == fcntl(iFdIn, F_SETFD, 1))
        {
            WARN("couldn't restore close-on-exec flag to stdin descriptor! "
                 "errno is %d (%s)\n", errno, strerror(errno));
        }
        pobjFileIn->ReleaseReference(pThread);
    }

    if (NULL != pobjFileOut)
    {
        if(-1 == fcntl(iFdOut, F_SETFD, 1))
        {
            WARN("couldn't restore close-on-exec flag to stdout descriptor! "
                 "errno is %d (%s)\n", errno, strerror(errno));
        }
        pobjFileOut->ReleaseReference(pThread);
    }

    if (NULL != pobjFileErr)
    {
        if(-1 == fcntl(iFdErr, F_SETFD, 1))
        {
            WARN("couldn't restore close-on-exec flag to stderr descriptor! "
                 "errno is %d (%s)\n", errno, strerror(errno));
        }
        pobjFileErr->ReleaseReference(pThread);
    }

    /* free allocated memory */
    if (lppArgv)
    {
        free(*lppArgv);
        free(lppArgv);
    }

    return palError;
}

/*++
Function:
  TerminateProcess

Note:
  hProcess is a handle on the current process.

See MSDN doc.
--*/
BOOL
PALAPI
TerminateProcess(
    IN HANDLE hProcess,
    IN UINT uExitCode)
{
    BOOL ret;

    PERF_ENTRY(TerminateProcess);
    ENTRY("TerminateProcess(hProcess=%p, uExitCode=%u)\n",hProcess, uExitCode );

    ret = PROCEndProcess(hProcess, uExitCode, TRUE);

    LOGEXIT("TerminateProcess returns BOOL %d\n", ret);
    PERF_EXIT(TerminateProcess);
    return ret;
}

/*++
Function:
  RaiseFailFastException

See MSDN doc.
--*/
VOID
PALAPI
DECLSPEC_NORETURN
RaiseFailFastException(
    IN PEXCEPTION_RECORD pExceptionRecord,
    IN PCONTEXT pContextRecord,
    IN DWORD dwFlags)
{
    PERF_ENTRY(RaiseFailFastException);
    ENTRY("RaiseFailFastException");

    TerminateCurrentProcessNoExit(TRUE);
    for (;;) PROCAbort();

    LOGEXIT("RaiseFailFastException");
    PERF_EXIT(RaiseFailFastException);
}

/*++
Function:
  PROCEndProcess

  Called from TerminateProcess and ExitProcess. This does the work of
  TerminateProcess, but also takes a flag that determines whether we
  shut down unconditionally. If the flag is set, the PAL will do very
  little extra work before exiting. Most importantly, it won't shut
  down any DLLs that are loaded.

--*/
static BOOL PROCEndProcess(HANDLE hProcess, UINT uExitCode, BOOL bTerminateUnconditionally)
{
    DWORD dwProcessId;
    BOOL ret = FALSE;

    dwProcessId = PROCGetProcessIDFromHandle(hProcess);
    if (dwProcessId == 0)
    {
        SetLastError(ERROR_INVALID_HANDLE);
    }
    else if(dwProcessId != GetCurrentProcessId())
    {
        if (uExitCode != 0)
            WARN("exit code 0x%x ignored for external process.\n", uExitCode);

        if (kill(dwProcessId, SIGKILL) == 0)
        {
            ret = TRUE;
        }
        else
        {
            switch (errno) {
            case ESRCH:
                SetLastError(ERROR_INVALID_HANDLE);
                break;
            case EPERM:
                SetLastError(ERROR_ACCESS_DENIED);
                break;
            default:
                // Unexpected failure.
                ASSERT(FALSE);
                SetLastError(ERROR_INTERNAL_ERROR);
                break;
            }
        }
    }
    else
    {
        // WARN/ERROR before starting the termination process and/or leaving the PAL.
        if (bTerminateUnconditionally)
        {
            WARN("exit code 0x%x ignored for terminate.\n", uExitCode);
        }
        else if ((uExitCode & 0xff) != uExitCode)
        {
            // TODO: Convert uExitCodes into sysexits(3)?
            ERROR("exit() only supports the lower 8-bits of an exit code. "
                "status will only see error 0x%x instead of 0x%x.\n", uExitCode & 0xff, uExitCode);
        }

        TerminateCurrentProcessNoExit(bTerminateUnconditionally);

        LOGEXIT("PROCEndProcess will not return\n");

        if (bTerminateUnconditionally)
        {
            // abort() has the semantics that
            // (1) it doesn't run atexit handlers
            PROCAbort();
        }
        else
        {
            exit(uExitCode);
        }

        ASSERT(FALSE); // we shouldn't get here
    }

    return ret;
}

static bool IsCoreClrModule(const char* pModulePath)
{
    // Strip off everything up to and including the last slash in the path to get name
    const char* pModuleName = pModulePath;
    while (strchr(pModuleName, '/') != NULL)
    {
        pModuleName = strchr(pModuleName, '/');
        pModuleName++; // pass the slash
    }

    return _stricmp(pModuleName, MAKEDLLNAME_A("coreclr")) == 0;
}

// Build the semaphore names using the PID and a value that can be used for distinguishing
// between processes with the same PID (which ran at different times). This is to avoid
// cases where a prior process with the same PID exited abnormally without having a chance
// to clean up its semaphore.
// Note to anyone modifying these names in the future: Semaphore names on OS X are limited
// to SEM_NAME_LEN characters, including null. SEM_NAME_LEN is 31 (at least on OS X 10.11).
// NetBSD limits semaphore names to 15 characters, including null (at least up to 7.99.25).
// Keep 31 length for Core 1.0 RC2 compatibility
#if defined(__NetBSD__)
static const char* RuntimeSemaphoreNameFormat = "/clr%s%08llx";
#else
static const char* RuntimeSemaphoreNameFormat = "/clr%s%08x%016llx";
#endif

static const char* RuntimeStartupSemaphoreName = "st";
static const char* RuntimeContinueSemaphoreName = "co";

#if defined(__NetBSD__)
static uint64_t HashSemaphoreName(uint64_t a, uint64_t b)
{
    return (a ^ b) & 0xffffffff;
}
#else
#define HashSemaphoreName(a,b) a,b
#endif

static const char *const TwoWayNamedPipePrefix = "clr-debug-pipe";
static const char* IpcNameFormat = "%s-%d-%llu-%s";

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
static const char* RuntimeStartupPipeName = "st";
static const char* RuntimeContinuePipeName = "co";
#define PIPE_OPEN_RETRY_DELAY_NS 500000000 // 500 ms
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

class PAL_RuntimeStartupHelper
{
    LONG m_ref;
    volatile bool m_canceled;
    volatile PAL_ERROR m_error;
    PPAL_STARTUP_CALLBACK m_callback;
    PVOID m_parameter;
    DWORD m_threadId;
    HANDLE m_threadHandle;
    DWORD m_processId;
    char m_startupSemName[CLR_SEM_MAX_NAMELEN];
    char m_continueSemName[CLR_SEM_MAX_NAMELEN];

    // A value that, used in conjunction with the process ID, uniquely identifies a process.
    // See the format we use for debugger semaphore names for why this is necessary.
    UINT64 m_processIdDisambiguationKey;

    // Debugger waits on this semaphore and the runtime signals it on startup.
    sem_t *m_startupSem;

    // Debuggee waits on this semaphore and the debugger signals it after the startup callback
    // registered (m_callback) returns.
    sem_t *m_continueSem;

#ifdef __APPLE__    
    char m_applicationGroupId[MAX_APPLICATION_GROUP_ID_LENGTH+1];
#endif // __APPLE__

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
    char m_startupPipeName[MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH];
    char m_continuePipeName[MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH];
    DWORD m_runtimeEventsThreadId;
    HANDLE m_runtimeEventsThreadHandle;

    typedef enum
    {
        RuntimeEvent_Unknown = 0,
        RuntimeEvent_Started = 1,
        RuntimeEvent_Continue = 2,
    } RuntimeEvent;

    static void CloseFd(int fd)
    {
        if (fd != -1)
        {
            while (close(fd) < 0 && errno == EINTR);
        }
    }
    
    static ssize_t ReadIOFunc(int fd, void *buf, size_t count)
    {
        return read(fd, buf, count);
    }

    static ssize_t WriteIOFunc(int fd, void *buf, size_t count)
    {
        return write(fd, buf, count);
    }

    static BOOL CreatePipe(const char* name)
    {
        int result = -1;
        while ((result = mkfifo(name, S_IRWXU)) < 0 && errno == EINTR);
        if (result == -1)
        {
            if (errno == EEXIST)
            {
                unlink(name);
                while ((result = mkfifo(name, S_IRWXU)) < 0 && errno == EINTR);
            }
        }

        if (result == -1)
        {
            TRACE("mkfifo failed: errno is %d (%s)\n", errno, strerror(errno));
            return FALSE;
        }

        return TRUE;
    }

    static int OpenNonBlockingPipe(int kq, const char* name, int mode, volatile bool *canceled)
    {
        int fd = -1;
        int retries = 0;
        int flags = mode | O_NONBLOCK;

#if defined(FD_CLOEXEC)
        flags |= O_CLOEXEC;
#endif

        while (!*canceled && fd == -1)
        {
            fd = open(name, flags);
            if (fd == -1)
            {
                if (mode == O_WRONLY && errno == ENXIO)
                {
                    PAL_nanosleep(PIPE_OPEN_RETRY_DELAY_NS);
                    continue;
                }
                else if (errno == EINTR)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }
        }

        if (fd == -1)
        {
            if (!*canceled)
            {
                TRACE("open failed: errno is %d (%s)\n", errno, strerror(errno));
            }
            else
            {
                TRACE("open canceled\n");
            }
            return -1;
        }

#if HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
        struct kevent change;
        EV_SET(&change, fd, (mode & O_ACCMODE) == O_RDONLY ? EVFILT_READ : EVFILT_WRITE, EV_ADD | EV_DISABLE, 0, 0, NULL);
        if (kevent(kq, &change, 1, NULL, 0, NULL) == -1)
        {
            TRACE("kevent failed: errno is %d (%s)\n", errno, strerror(errno));
            CloseFd(fd);
            return -1;
        }
#endif // HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
        
        return fd;
    }

#if HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
    static int DoNonBlockingPipeIO(int kq, int fd, void *buf, size_t count, int timeout, ssize_t (*io_func)(int fd, void *buf, size_t count), short filter)
    {
        int result = -1;
        struct timespec timeout_spec;
        struct timespec *timeout_ptr = NULL;
        if (timeout > 0)
        {
            timeout_spec.tv_sec = timeout / 1000;
            timeout_spec.tv_nsec = (timeout % 1000) * 1000000L;
            timeout_ptr = &timeout_spec;
        }

        struct kevent change;
        EV_SET(&change, fd, filter, EV_ENABLE, 0, 0, NULL);
        if (kevent(kq, &change, 1, NULL, 0, NULL) == -1)
        {
            return -1;
        }

        while (1)
        {    
            struct kevent event;
            int nev = kevent(kq, NULL, 0, &event, 1, timeout_ptr);   
            if (nev == -1 && errno == EINTR)
            {
                continue;
            } 
            else if (nev == 0)
            {
                // Check for timeout or EOF.
                int n = io_func(fd, buf, count);
                if (n > 0)
                {
                    result = n;
                    break;
                }
                else if (n == 0)
                {
                    // EOF - pipe closed
                    result = -2;
                    break;
                }
                else if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK)
                {
                    // Timeout.
                    result = 0;
                    break;
                }
                else
                {
                    break;
                }
            }
            else if (nev > 0)
            {
                if (event.filter == filter && event.ident == fd)
                {
                    if (event.flags & EV_EOF)
                    {
                        result = -2;
                        break;
                    }
                    
                    int n = io_func(fd, buf, count);
                    if (n > 0)
                    {
                        result = n;
                        break;
                    }
                    else if (n == 0)
                    {
                        // EOF - pipe closed
                        result = -2;
                        break;
                    }
                    else if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else
            {
                break;
            }
        }

        EV_SET(&change, fd, filter, EV_DISABLE, 0, 0, NULL);
        kevent(kq, &change, 1, NULL, 0, NULL);

        return result;
    }

    static int ReadNonBlockingPipe(int kq, int fd, void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(kq, fd, buf, count, timeout, ReadIOFunc, EVFILT_READ);
    }

    static int WriteNonBlockingPipe(int kq, int fd, const void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(kq, fd, (void *)buf, count, timeout, WriteIOFunc, EVFILT_WRITE);
    }
#else
    static int DoNonBlockingPipeIO(int fd, void *buf, size_t count, int timeout, ssize_t (*io_func)(int fd, void *buf, size_t count), short filter)
    {
        struct pollfd pfd;
        pfd.fd = fd;
        pfd.events = filter;

        while (1)
        {
            int poll_ret = poll(&pfd, 1, timeout);
            if (poll_ret > 0)
            {
                if (pfd.revents & filter)
                {
                    int n = io_func(fd, buf, count);
                    if (n > 0)
                    {
                        return n;
                    }
                    else if (n == 0)
                    {
                        // EOF - pipe closed
                        return -2;
                    }
                    else if (errno == EAGAIN || errno == EWOULDBLOCK)
                    {
                        continue;
                    }
                    else if (errno == EINTR)
                    {
                        continue;
                    }
                    else
                    {
                        return -1;
                    }
                }
                else if (pfd.revents & (POLLERR | POLLHUP | POLLNVAL))
                {
                    return -1;
                }
            }
            else if (poll_ret == 0)
            {
                // Check for timeout or EOF.
                int n = io_func(fd, buf, count);
                if (n > 0)
                {
                    return n;
                }
                else if (n == 0)
                {
                    // EOF - pipe closed
                    return -2;
                }
                else if (errno == EINTR || errno == EAGAIN || errno == EWOULDBLOCK)
                {
                    // Timeout.
                    return 0;
                }
                else
                {
                    return -1;
                }
            }
            else if (errno == EINTR)
            {
                continue;
            }
            else
            {
                return -1;
            }
        }

        return -1;
    }

    static int ReadNonBlockingPipe(int kq, int fd, void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(fd, buf, count, timeout, ReadIOFunc, POLLIN);
    }

    static int
    WriteNonBlockingPipe(int kq, int fd, const void *buf, size_t count, int timeout)
    {
        return DoNonBlockingPipeIO(fd, (void *)buf, count, timeout, WriteIOFunc, POLLOUT);
    }
#endif // HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

#ifdef __APPLE__
    LPCSTR GetApplicationGroupId() const
    {
        return m_applicationGroupId[0] == '\0' ? nullptr : m_applicationGroupId;
    }
#else
    LPCSTR GetApplicationGroupId() const
    {
        return nullptr;
    }
#endif // __APPLE__

public:
    PAL_RuntimeStartupHelper(DWORD dwProcessId, PPAL_STARTUP_CALLBACK pfnCallback, PVOID parameter) :
        m_ref(1),
        m_canceled(false),
        m_error(NO_ERROR),
        m_callback(pfnCallback),
        m_parameter(parameter),
        m_threadId(0),
        m_threadHandle(NULL),
        m_processId(dwProcessId),
        m_startupSem(SEM_FAILED),
        m_continueSem(SEM_FAILED)
#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        , m_runtimeEventsThreadId(0)
        , m_runtimeEventsThreadHandle(NULL)
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
    {
    }

    ~PAL_RuntimeStartupHelper()
    {
        if (m_startupSem != SEM_FAILED)
        {
            sem_close(m_startupSem);
            sem_unlink(m_startupSemName);
        }

        if (m_continueSem != SEM_FAILED)
        {
            sem_close(m_continueSem);
            sem_unlink(m_continueSemName);
        }

        if (m_threadHandle != NULL)
        {
            CloseHandle(m_threadHandle);
        }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        unlink(m_startupPipeName);
        unlink(m_continuePipeName);

        if (m_runtimeEventsThreadHandle != NULL)
        {
            CloseHandle(m_runtimeEventsThreadHandle);
        }
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
    }

    LONG AddRef()
    {
        LONG ref = InterlockedIncrement(&m_ref);
        return ref;
    }

    LONG Release()
    {
        LONG ref = InterlockedDecrement(&m_ref);
        if (ref == 0)
        {
            InternalDelete(this);
        }
        return ref;
    }

    PAL_ERROR GetSemError()
    {
        PAL_ERROR pe;
        switch (errno)
        {
            case ENOENT:
                pe = ERROR_NOT_FOUND;
                break;
            case EACCES:
                pe = ERROR_INVALID_ACCESS;
                break;
            case EINVAL:
            case ENAMETOOLONG:
                pe = ERROR_INVALID_NAME;
                break;
            case ENOMEM:
                pe = ERROR_OUTOFMEMORY;
                break;
            case EEXIST:
                pe = ERROR_ALREADY_EXISTS;
                break;
            case ENOSPC:
                pe = ERROR_TOO_MANY_SEMAPHORES;
                break;
            default:
                pe = ERROR_INVALID_PARAMETER;
                break;
        }
        return pe;
    }

    PAL_ERROR Register(LPCWSTR lpApplicationGroupId)
    {
        CPalThread *pThread = InternalGetCurrentThread();
        PAL_ERROR pe = NO_ERROR;
        BOOL ret;
        UnambiguousProcessDescriptor unambiguousProcessDescriptor;
        SIZE_T osThreadId = 0;

#ifdef __APPLE__
        if (lpApplicationGroupId != NULL)
        {
            /* Convert to ASCII */
            int applicationGroupIdLength = WideCharToMultiByte(CP_ACP, 0, lpApplicationGroupId, -1, m_applicationGroupId, sizeof(m_applicationGroupId), NULL, NULL);
            if (applicationGroupIdLength == 0)
            {
                pe = GetLastError();
                TRACE("applicationGroupId: Failed to convert to multibyte string (%u)\n", pe);
                if (pe == ERROR_INSUFFICIENT_BUFFER)
                {
                    pe = ERROR_BAD_LENGTH;
                }
                goto exit;
            }
        }
        else
        {
            // Indicate that group ID is not being used
            m_applicationGroupId[0] = '\0';
        }
#endif // __APPLE__

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        PAL_GetTransportPipeName(m_startupPipeName, m_processId, GetApplicationGroupId(), RuntimeStartupPipeName);
        PAL_GetTransportPipeName(m_continuePipeName, m_processId, GetApplicationGroupId(), RuntimeContinuePipeName);

        TRACE("PAL_RuntimeStartupHelper.Register creating startup '%s' continue '%s' pipes\n", m_startupPipeName, m_continuePipeName);

        CreatePipe(m_continuePipeName);
        CreatePipe(m_startupPipeName);
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

        // See semaphore name format for details about this value. We store it so that
        // it can be used by the cleanup code that removes the semaphore with sem_unlink.
        ret = GetProcessIdDisambiguationKey(m_processId, &m_processIdDisambiguationKey);

        // If GetProcessIdDisambiguationKey failed for some reason, it should set the value
        // to 0. We expect that anyone else opening the semaphore name will also fail and thus
        // will also try to use 0 as the value.
        _ASSERTE(ret == TRUE || m_processIdDisambiguationKey == 0);

        unambiguousProcessDescriptor.Init(m_processId, m_processIdDisambiguationKey);
        CreateSemaphoreName(m_startupSemName, RuntimeStartupSemaphoreName, unambiguousProcessDescriptor, GetApplicationGroupId());
        CreateSemaphoreName(m_continueSemName, RuntimeContinueSemaphoreName, unambiguousProcessDescriptor, GetApplicationGroupId());

        TRACE("PAL_RuntimeStartupHelper.Register creating startup '%s' continue '%s' semaphores\n", m_startupSemName, m_continueSemName);

        // Create the continue semaphore first so we don't race with PAL_NotifyRuntimeStarted. This open will fail if another
        // debugger is trying to attach to this process because the name will already exist.
        m_continueSem = sem_open(m_continueSemName, O_CREAT | O_EXCL, S_IRWXU, 0);
        if (m_continueSem == SEM_FAILED)
        {
            TRACE("sem_open(continue) failed: errno is %d (%s)\n", errno, strerror(errno));
            pe = GetSemError();
            goto exit;
        }

        // Create the debuggee startup semaphore so the runtime (debuggee) knows to wait for a debugger connection.
        m_startupSem = sem_open(m_startupSemName, O_CREAT | O_EXCL, S_IRWXU, 0);
        if (m_startupSem == SEM_FAILED)
        {
            TRACE("sem_open(startup) failed: errno is %d (%s)\n", errno, strerror(errno));
            pe = GetSemError();
            goto exit;
        }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        // Add a reference for the thread handler
        AddRef();
        pe = InternalCreateThread(
            pThread,
            NULL,
            0,
            ::StartupHelperRuntimeEventsThread,
            this,
            0,
            UserCreatedThread,
            &osThreadId,
            &m_runtimeEventsThreadHandle);

        if (NO_ERROR != pe)
        {
            TRACE("InternalCreateThread failed %d\n", pe);
            Release();
            goto exit;
        }

        m_runtimeEventsThreadId = (DWORD)osThreadId;
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

        // Add a reference for the thread handler
        AddRef();
        pe = InternalCreateThread(
            pThread,
            NULL,
            0,
            ::StartupHelperThread,
            this,
            0,
            UserCreatedThread,
            &osThreadId,
            &m_threadHandle);

        if (NO_ERROR != pe)
        {
            TRACE("InternalCreateThread failed %d\n", pe);
            Release();
            goto exit;
        }
        m_threadId = (DWORD)osThreadId;
    exit:
        return pe;
    }

    void Unregister()
    {
        m_canceled = true;

        // Tell the runtime to continue
        if (sem_post(m_continueSem) != 0)
        {
            ASSERT("sem_post(continueSem) failed: errno is %d (%s)\n", errno, strerror(errno));
        }

        // Tell the worker thread to continue
        if (sem_post(m_startupSem) != 0)
        {
            ASSERT("sem_post(startupSem) failed: errno is %d (%s)\n", errno, strerror(errno));
        }

        // Don't need to wait for the worker threads if unregister called on it
        if (m_threadId != (DWORD)THREADSilentGetCurrentThreadId())
        {
            // Wait for work thread to exit for 60 seconds
            WaitForSingleObject(m_threadHandle, 60 * 1000);
        }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
        if (m_runtimeEventsThreadId != (DWORD)THREADSilentGetCurrentThreadId())
        {
            // Wait for runtime events thread to exit for 60 seconds
            WaitForSingleObject(m_runtimeEventsThreadHandle, 60 * 1000);
        }
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES
    }

    //
    // There are a couple race conditions that need to be considered here:
    //
    // * On launch, between the fork and execv in the PAL's CreateProcess where the target process
    //   may contain a coreclr module image if the debugger process is running managed code. This
    //   makes just checking if the coreclr module exists not enough.
    //
    // * On launch (after the execv) or attach when the coreclr is loaded but before the DAC globals
    //   table is initialized where it is too soon to use/initialize the DAC on the debugger side.
    //
    // They are both fixed by check if the one of transport pipe files has been created.
    //
    bool IsCoreClrProcessReady()
    {
        char pipeName[MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH];

        PAL_GetTransportPipeName(pipeName, m_processId, GetApplicationGroupId(), "in");

        struct stat buf;
        if (stat(pipeName, &buf) == 0)
        {
            TRACE("IsCoreClrProcessReady: stat(%s) SUCCEEDED\n", pipeName);
            return true;
        }
        TRACE("IsCoreClrProcessReady: stat(%s) FAILED: errno is %d (%s)\n", pipeName, errno, strerror(errno));
        return false;
    }

    PAL_ERROR InvokeStartupCallback()
    {
        ProcessModules *listHead = NULL;
        PAL_ERROR pe = NO_ERROR;
        DWORD count;

        if (m_canceled)
        {
            goto exit;
        }

        // Enumerate all the modules in the process and invoke the callback
        // for the coreclr module if found.
        listHead = CreateProcessModules(m_processId, &count);
        if (listHead == NULL)
        {
            ERROR("CreateProcessModules failed for pid %d\n", m_processId);
            pe = ERROR_INVALID_PARAMETER;
            goto exit;
        }

        for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
        {
            bool found = false;

            PAL_CPP_TRY
            {
                found = m_callback(entry->GetName(), entry->GetBaseAddress(), m_parameter);
            }
            PAL_CPP_CATCH_ALL
            {
                ERROR("InvokeStartupCallback exception in callback %p %s\n", entry->GetBaseAddress(), entry->GetName());
                pe = ERROR_ACCESS_DENIED;
                goto exit;
            }
            PAL_CPP_ENDTRY

            if (found)
            {
                TRACE("InvokeStartupCallback found module %p %s\n", entry->GetBaseAddress(), entry->GetName());
                break;
            }
        }

    exit:
        // Wake up the runtime
        if (sem_post(m_continueSem) != 0)
        {
            ASSERT("sem_post(continueSem) failed: errno is %d (%s)\n", errno, strerror(errno));
        }
        if (listHead != NULL)
        {
            DestroyProcessModules(listHead);
        }
        return pe;
    }

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
    void StartupHelperRuntimeEventsThread()
    {
        PAL_ERROR pe = NO_ERROR;
        int offset = 0;
        int kq = -1;
        int continuePipeFd = -1;
        int startupPipeFd = -1;

#if HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT
        kq = kqueue();
        if (kq == -1)
        {
            TRACE("StartupHelperRuntimeEventsThread: kqueue() failed: %d (%s)\n", errno, strerror(errno));
            goto exit;
        }
#endif // HAVE_KQUEUE && !HAVE_BROKEN_FIFO_KEVENT

        TRACE("StartupHelperRuntimeEventsThread: opening continue '%s' pipe\n", m_continuePipeName);

        continuePipeFd = OpenNonBlockingPipe(kq, m_continuePipeName, O_WRONLY, &m_canceled);
        if (continuePipeFd == -1)
        {
            if (m_canceled)
            {
                TRACE("StartupHelperRuntimeEventsThread: canceled opening continue pipe\n");
            }
            else
            {
                TRACE("StartupHelperRuntimeEventsThread: failed opening continue pipe\n");
            }
            goto exit;
        }

        TRACE("StartupHelperRuntimeEventsThread: opening startup '%s' pipe\n", m_startupPipeName);

        startupPipeFd = OpenNonBlockingPipe(kq, m_startupPipeName, O_RDONLY, &m_canceled);
        if (startupPipeFd == -1)
        {
            if (m_canceled)
            {
                TRACE("StartupHelperRuntimeEventsThread: canceled opening startup pipe\n");
            }
            else
            {
                TRACE("StartupHelperRuntimeEventsThread: failed opening startup pipe\n");
            }
            goto exit;
        }

        TRACE("StartupHelperRuntimeEventsThread: waiting on started event\n");

        {
            unsigned char event = (unsigned char)RuntimeEvent_Unknown;
            unsigned char *buffer = &event;
            int bytesToRead = sizeof(event);
            int bytesRead = 0;

            do
            {
                bytesRead = ReadNonBlockingPipe(kq, startupPipeFd, buffer + offset, bytesToRead - offset, 1000);
                if (bytesRead > 0)
                {
                    offset += bytesRead;
                }
                else if (bytesRead == 0)
                {
                    // Timeout.
                    continue;
                }
                else if (bytesRead == -2 && offset == 0)
                {
                    // Not connected yet, retry.
                    continue;
                }
                else
                {
                    // Error or EOF
                    break;
                }
            }
            while (!m_canceled && offset < bytesToRead);

            if (offset == bytesToRead && event == (unsigned char)RuntimeEvent_Started)
            {
                TRACE("StartupHelperRuntimeEventsThread: received started event\n");
            }
            else if (m_canceled)
            {
                TRACE("StartupHelperRuntimeEventsThread: canceled waiting for started event\n");
            }
            else
            {
                TRACE("StartupHelperRuntimeEventsThread: received invalid event\n");
                m_error = ERROR_INVALID_PARAMETER;
            }
            
            sem_post(m_startupSem);
        }

        TRACE("StartupHelperRuntimeEventsThread: waiting on debugger\n");

        while (sem_wait(m_continueSem) != 0)
        {
            if (EINTR == errno)
            {
                TRACE("StartupHelperRuntimeEventsThread: sem_wait() failed with EINTR; re-waiting\n");
                continue;
            }
            TRACE("StartupHelperRuntimeEventsThread: sem_wait(contniue) failed: errno is %d (%s)\n", errno, strerror(errno));
        }

        TRACE("StartupHelperRuntimeEventsThread: sending continue event\n");

        {
            unsigned char event = (unsigned char)RuntimeEvent_Continue;
            unsigned char *buffer = &event;
            int bytesToWrite = sizeof(event);
            int bytesWritten = 0;

            offset = 0;
            do
            {
                bytesWritten = WriteNonBlockingPipe(kq, continuePipeFd, buffer + offset, bytesToWrite - offset, 1000);
                if (bytesWritten > 0)
                {
                    offset += bytesWritten;
                }
            }
            while (bytesWritten > 0 && offset < bytesToWrite);

            if (offset != bytesToWrite)
            {
                TRACE("StartupHelperRuntimeEventsThread: failed sending continue event\n");
            }
        }

    exit:

        if (startupPipeFd != -1)
        {
            CloseFd(startupPipeFd);
        }

        if (continuePipeFd != -1)
        {
            CloseFd(continuePipeFd);
        }

        if (kq != -1)
        {
            CloseFd(kq);
        }
    }

#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

    void StartupHelperThread()
    {
        PAL_ERROR pe = NO_ERROR;

        if (IsCoreClrProcessReady())
        {
            pe = InvokeStartupCallback();
        }
        else {
            TRACE("sem_wait(startup)\n");

            // Wait until the coreclr runtime (debuggee) starts up
            while (sem_wait(m_startupSem) != 0)
            {
                if (EINTR == errno)
                {
                    TRACE("sem_wait() failed with EINTR; re-waiting");
                    continue;
                }
                TRACE("sem_wait(startup) failed: errno is %d (%s)\n", errno, strerror(errno));
                pe = GetSemError();
            }

            if (pe == NO_ERROR && m_error != NO_ERROR)
            {
                pe = m_error;
            }

            if (pe == NO_ERROR)
            {
                pe = InvokeStartupCallback();
            }
        }

        // Invoke the callback on errors
        if (pe != NO_ERROR && !m_canceled)
        {
            SetLastError(pe);
            m_callback(NULL, NULL, m_parameter);
        }
    }
};

#ifdef ENABLE_RUNTIME_EVENTS_OVER_PIPES
static
DWORD
StartupHelperRuntimeEventsThread(LPVOID p)
{
    TRACE("StartupHelperRuntimeEventsThread: starting\n");

    PAL_RuntimeStartupHelper *helper = (PAL_RuntimeStartupHelper *)p;
    helper->StartupHelperRuntimeEventsThread();
    helper->Release();

    TRACE("StartupHelperRuntimeEventsThread: finished\n");

    return 0;
}
#endif // ENABLE_RUNTIME_EVENTS_OVER_PIPES

static
DWORD
StartupHelperThread(LPVOID p)
{
    TRACE("PAL's StartupHelperThread starting\n");

    PAL_RuntimeStartupHelper *helper = (PAL_RuntimeStartupHelper *)p;
    helper->StartupHelperThread();
    helper->Release();
    return 0;
}

/*++
    PAL_RegisterForRuntimeStartup

Parameters:
    dwProcessId - process id of runtime process
    lpApplicationGroupId - A string representing the application group ID of a sandboxed
                           process running in Mac. Pass NULL if the process is not
                           running in a sandbox and other platforms.
    pfnCallback - function to callback for coreclr module found
    parameter - data to pass to callback
    ppUnregisterToken - pointer to put PAL_UnregisterForRuntimeStartup token.

Return value:
    PAL_ERROR

Note:
    If the modulePath or hModule is NULL when the callback is invoked, an error occured
    and GetLastError() will return the Win32 error code.

    The callback is always invoked on a separate thread and this API returns immediately.

    Only the first coreclr module is currently supported.

--*/
DWORD
PALAPI
PAL_RegisterForRuntimeStartup(
    IN DWORD dwProcessId,
    IN LPCWSTR lpApplicationGroupId,
    IN PPAL_STARTUP_CALLBACK pfnCallback,
    IN PVOID parameter,
    OUT PVOID *ppUnregisterToken)
{
    _ASSERTE(pfnCallback != NULL);
    _ASSERTE(ppUnregisterToken != NULL);

    PAL_RuntimeStartupHelper *helper = InternalNew<PAL_RuntimeStartupHelper>(dwProcessId, pfnCallback, parameter);

    // Create the debuggee startup semaphore so the runtime (debuggee) knows to wait for
    // a debugger connection.
    PAL_ERROR pe = helper->Register(lpApplicationGroupId);
    if (NO_ERROR != pe)
    {
        helper->Release();
        helper = NULL;
    }

    *ppUnregisterToken = helper;
    return pe;
}

/*++
    PAL_UnregisterForRuntimeStartup

    Stops/cancels startup notification. This API can be called in the startup callback. Otherwise,
    it will block until the callback thread finishes and no more callbacks will be initiated after
    this API returns.

Parameters:
    dwUnregisterToken - token from PAL_RegisterForRuntimeStartup or NULL.

Return value:
    PAL_ERROR
--*/
DWORD
PALAPI
PAL_UnregisterForRuntimeStartup(
    IN PVOID pUnregisterToken)
{
    if (pUnregisterToken != NULL)
    {
        PAL_RuntimeStartupHelper *helper = (PAL_RuntimeStartupHelper *)pUnregisterToken;
        helper->Unregister();
        helper->Release();
    }
    return NO_ERROR;
}

#ifdef __APPLE__

// We use 7bits from each byte, so this computes the extra size we need to encode a given byte count
constexpr int GetExtraEncodedAreaSize(UINT rawByteCount)
{
    return (rawByteCount+6)/7;
}
const int SEMAPHORE_ENCODED_NAME_EXTRA_LENGTH = GetExtraEncodedAreaSize(sizeof(UnambiguousProcessDescriptor));
const int SEMAPHORE_ENCODED_NAME_LENGTH =
    sizeof(UnambiguousProcessDescriptor) + /* For process ID + disambiguationKey */
    SEMAPHORE_ENCODED_NAME_EXTRA_LENGTH; /* For base 255 extra encoding space */

static_assert_no_msg(MAX_APPLICATION_GROUP_ID_LENGTH
    + 1 /* For / */
    + 2 /* For ST/CO name prefix */
    + SEMAPHORE_ENCODED_NAME_LENGTH /* For encoded name string */
    + 1 /* For null terminator */
    <= CLR_SEM_MAX_NAMELEN);

// In Apple we are limited by the length of the semaphore name. However, the characters which can be used in the
// name can be anything between 1 and 255 (since 0 will terminate the string). Thus, we encode each byte b in
// unambiguousProcessDescriptor as b ? b : 1, and mark an additional bit indicating if b is 0 or not. We use 7 bits
// out of each extra byte so 1 bit will always be '1'. This will ensure that our extra bytes are never 0 which are
// invalid characters. Thus we need an extra byte for each 7 input bytes. Hence, only extra 2 bytes for the name string.
void EncodeSemaphoreName(char *encodedSemName, const UnambiguousProcessDescriptor& unambiguousProcessDescriptor)
{
    const unsigned char *buffer = (const unsigned char *)&unambiguousProcessDescriptor;
    char *extraEncodingBits = encodedSemName + sizeof(UnambiguousProcessDescriptor);

    // Reset the extra encoding bit area
    for (int i=0; i<SEMAPHORE_ENCODED_NAME_EXTRA_LENGTH; i++)
    {
        extraEncodingBits[i] = 0x80;
    }

    // Encode each byte in unambiguousProcessDescriptor
    for (int i=0; i<sizeof(UnambiguousProcessDescriptor); i++)
    {
        unsigned char b = buffer[i];
        encodedSemName[i] = b ? b : 1;
        extraEncodingBits[i/7] |= (b ? 0 : 1) << (i%7);
    }
}
#endif

void CreateSemaphoreName(char semName[CLR_SEM_MAX_NAMELEN], LPCSTR semaphoreName, const UnambiguousProcessDescriptor& unambiguousProcessDescriptor, LPCSTR applicationGroupId)
{
    int length = 0;

#ifdef __APPLE__
    if (applicationGroupId != nullptr)
    {
        // We assume here that applicationGroupId has been already tested for length and is less than MAX_APPLICATION_GROUP_ID_LENGTH
        length = sprintf_s(semName, CLR_SEM_MAX_NAMELEN, "%s/%s", applicationGroupId, semaphoreName);
        _ASSERTE(length > 0 && length < CLR_SEM_MAX_NAMELEN);

        EncodeSemaphoreName(semName+length, unambiguousProcessDescriptor);
        length += SEMAPHORE_ENCODED_NAME_LENGTH;
        semName[length] = 0;
    }
    else
#endif // __APPLE__
    {
        length = sprintf_s(
            semName,
            CLR_SEM_MAX_NAMELEN,
            RuntimeSemaphoreNameFormat,
            semaphoreName,
            HashSemaphoreName(unambiguousProcessDescriptor.m_processId, unambiguousProcessDescriptor.m_disambiguationKey));
    }

    _ASSERTE(length > 0 && length < CLR_SEM_MAX_NAMELEN );
}

/*++
 Function:
  GetProcessIdDisambiguationKey

  Get a numeric value that can be used to disambiguate between processes with the same PID,
  provided that one of them is still running. The numeric value can mean different things
  on different platforms, so it should not be used for any other purpose. Under the hood,
  it is implemented based on the creation time of the process.
--*/
BOOL
GetProcessIdDisambiguationKey(DWORD processId, UINT64 *disambiguationKey)
{
    if (disambiguationKey == nullptr)
    {
        _ASSERTE(!"disambiguationKey argument cannot be null!");
        return FALSE;
    }

    *disambiguationKey = 0;

#if defined(__APPLE__) || defined(__FreeBSD__)

    // On OS X, we return the process start time expressed in Unix time (the number of seconds
    // since the start of the Unix epoch).
    struct kinfo_proc info = {};
    size_t size = sizeof(info);
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, processId };
    int ret = ::sysctl(mib, sizeof(mib)/sizeof(*mib), &info, &size, nullptr, 0);

    if (ret == 0)
    {
#if defined(__APPLE__)
        timeval procStartTime = info.kp_proc.p_starttime;
#else // __FreeBSD__
        timeval procStartTime = info.ki_start;
#endif
        long secondsSinceEpoch = procStartTime.tv_sec;

        *disambiguationKey = secondsSinceEpoch;
        return TRUE;
    }
    else
    {
        _ASSERTE(!"Failed to get start time of a process.");
        return FALSE;
    }

#elif defined(__NetBSD__)

    // On NetBSD, we return the process start time expressed in Unix time (the number of seconds
    // since the start of the Unix epoch).
    kvm_t *kd;
    int cnt;
    struct kinfo_proc2 *info;

    kd = kvm_open(nullptr, nullptr, nullptr, KVM_NO_FILES, "kvm_open");
    if (kd == nullptr)
    {
        _ASSERTE(!"Failed to get start time of a process.");
        return FALSE;
    }

    info = kvm_getproc2(kd, KERN_PROC_PID, processId, sizeof(struct kinfo_proc2), &cnt);
    if (info == nullptr || cnt < 1)
    {
        kvm_close(kd);
        _ASSERTE(!"Failed to get start time of a process.");
        return FALSE;
    }

    kvm_close(kd);

    long secondsSinceEpoch = info->p_ustart_sec;
    *disambiguationKey = secondsSinceEpoch;

    return TRUE;

#elif HAVE_PROCFS_STAT

    // Here we read /proc/<pid>/stat file to get the start time for the process.
    // We return this value (which is expressed in jiffies since boot time).

    // Making something like: /proc/123/stat
    char statFileName[64];

    INDEBUG(int chars = )
    snprintf(statFileName, sizeof(statFileName), "/proc/%d/stat", processId);
    _ASSERTE(chars > 0 && chars <= (int)sizeof(statFileName));

    FILE *statFile = fopen(statFileName, "r");
    if (statFile == nullptr)
    {
        TRACE("GetProcessIdDisambiguationKey: fopen() FAILED");
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    char *line = nullptr;
    size_t lineLen = 0;
    if (getline(&line, &lineLen, statFile) == -1)
    {
        TRACE("GetProcessIdDisambiguationKey: getline() FAILED");
        SetLastError(ERROR_INVALID_HANDLE);
        free(line);
        fclose(statFile);
        return FALSE;
    }

    unsigned long long starttime;

    // According to `man proc`, the second field in the stat file is the filename of the executable,
    // in parentheses. Tokenizing the stat file using spaces as separators breaks when that name
    // has spaces in it, so we start using sscanf_s after skipping everything up to and including the
    // last closing paren and the space after it.
    char *scanStartPosition = strrchr(line, ')') + 2;

    // All the format specifiers for the fields in the stat file are provided by 'man proc'.
    int sscanfRet = sscanf_s(scanStartPosition,
        "%*c %*d %*d %*d %*d %*d %*u %*u %*u %*u %*u %*u %*u %*d %*d %*d %*d %*d %*d %llu \n",
         &starttime);

    free(line);
    fclose(statFile);

    if (sscanfRet != 1)
    {
        _ASSERTE(!"Failed to parse stat file contents with sscanf_s.");
        return FALSE;
    }

    *disambiguationKey = starttime;
    return TRUE;

#else
    // If this is not OS X and we don't have /proc, we just return FALSE.
    WARN("GetProcessIdDisambiguationKey was called but is not implemented on this platform!");
    return FALSE;
#endif
}

/*++
 Function:
  PAL_GetTransportName

  Builds the transport IPC names from the process id.
--*/
VOID
PALAPI
PAL_GetTransportName(
    const unsigned int MAX_TRANSPORT_NAME_LENGTH,
    OUT char *name,
    IN const char *prefix,
    IN DWORD id,
    IN const char *applicationGroupId,
    IN const char *suffix)
{
    *name = '\0';
    DWORD dwRetVal = 0;
    UINT64 disambiguationKey = 0;
    PathCharString formatBufferString;
    BOOL ret = GetProcessIdDisambiguationKey(id, &disambiguationKey);
    char *formatBuffer = formatBufferString.OpenStringBuffer(MAX_TRANSPORT_NAME_LENGTH-1);
    if (formatBuffer == nullptr)
    {
        ERROR("Out Of Memory");
        return;
    }

    // If GetProcessIdDisambiguationKey failed for some reason, it should set the value
    // to 0. We expect that anyone else making the pipe name will also fail and thus will
    // also try to use 0 as the value.
    _ASSERTE(ret == TRUE || disambiguationKey == 0);
#ifdef __APPLE__
    if (nullptr != applicationGroupId)
    {
        // Verify the length of the application group ID
        int applicationGroupIdLength = strlen(applicationGroupId);
        if (applicationGroupIdLength > MAX_APPLICATION_GROUP_ID_LENGTH)
        {
            ERROR("The length of applicationGroupId is larger than MAX_APPLICATION_GROUP_ID_LENGTH");
            return;
        }

        // In sandbox, all IPC files (locks, pipes) should be written to the application group
        // container. The path returned by GetTempPathA will be unique for each process and cannot
        // be used for IPC between two different processes
        if (!GetApplicationContainerFolder(formatBufferString, applicationGroupId, applicationGroupIdLength))
        {
            ERROR("Out Of Memory");
            return;
        }

        // Verify the size of the path won't exceed maximum allowed size
        if (formatBufferString.GetCount() >= MAX_TRANSPORT_NAME_LENGTH)
        {
            ERROR("GetApplicationContainerFolder returned a path that was larger than MAX_TRANSPORT_NAME_LENGTH");
            return;
        }
    }
    else
#endif // __APPLE__
    {
        // Get a temp file location
        dwRetVal = ::GetTempPathA(MAX_TRANSPORT_NAME_LENGTH, formatBuffer);
        if (dwRetVal == 0)
        {
            ERROR("GetTempPath failed (0x%08x)", ::GetLastError());
            return;
        }
        if (dwRetVal > MAX_TRANSPORT_NAME_LENGTH)
        {
            ERROR("GetTempPath returned a path that was larger than MAX_TRANSPORT_NAME_LENGTH");
            return;
        }
    }

    if (strncat_s(formatBuffer, MAX_TRANSPORT_NAME_LENGTH, IpcNameFormat, strlen(IpcNameFormat)) == STRUNCATE)
    {
        ERROR("TransportPipeName was larger than MAX_TRANSPORT_NAME_LENGTH");
        return;
    }

    int chars = snprintf(name, MAX_TRANSPORT_NAME_LENGTH, formatBuffer, prefix, id, disambiguationKey, suffix);
    _ASSERTE(chars > 0 && (unsigned int)chars < MAX_TRANSPORT_NAME_LENGTH);
}

/*++
 Function:
  PAL_GetTransportPipeName

  Builds the transport pipe names from the process id.
--*/
VOID
PALAPI
PAL_GetTransportPipeName(
    OUT char *name,
    IN DWORD id,
    IN const char *applicationGroupId,
    IN const char *suffix)
{
    PAL_GetTransportName(
        MAX_DEBUGGER_TRANSPORT_PIPE_NAME_LENGTH,
        name,
        TwoWayNamedPipePrefix,
        id,
        applicationGroupId,
        suffix);
}

/*++
Function:
  OpenProcess

See MSDN doc.

Notes :
dwDesiredAccess is ignored (all supported operations will be allowed)
bInheritHandle is ignored (no inheritance)
--*/
HANDLE
PALAPI
OpenProcess(
        DWORD dwDesiredAccess,
        BOOL bInheritHandle,
        DWORD dwProcessId)
{
    PAL_ERROR palError;
    CPalThread *pThread;
    IPalObject *pobjProcess = NULL;
    IPalObject *pobjProcessRegistered = NULL;
    IDataLock *pDataLock;
    CProcProcessLocalData *pLocalData;
    CObjectAttributes oa;
    HANDLE hProcess = NULL;

    PERF_ENTRY(OpenProcess);
    ENTRY("OpenProcess(dwDesiredAccess=0x%08x, bInheritHandle=%d, "
          "dwProcessId = 0x%08x)\n",
          dwDesiredAccess, bInheritHandle, dwProcessId );

    pThread = InternalGetCurrentThread();

    if (0 == dwProcessId)
    {
        palError = ERROR_INVALID_PARAMETER;
        goto OpenProcessExit;
    }

    palError = g_pObjectManager->AllocateObject(
        pThread,
        &otProcess,
        &oa,
        &pobjProcess
        );

    if (NO_ERROR != palError)
    {
        goto OpenProcessExit;
    }

    palError = pobjProcess->GetProcessLocalData(
        pThread,
        WriteLock,
        &pDataLock,
        reinterpret_cast<void **>(&pLocalData)
        );

    if (NO_ERROR != palError)
    {
        goto OpenProcessExit;
    }

    pLocalData->dwProcessId = dwProcessId;
    pDataLock->ReleaseLock(pThread, TRUE);

    palError = g_pObjectManager->RegisterObject(
        pThread,
        pobjProcess,
        &aotProcess,
        &hProcess,
        &pobjProcessRegistered
        );

    //
    // pobjProcess was invalidated by the above call, so NULL
    // it out here
    //

    pobjProcess = NULL;

    //
    // TODO: check to see if the process actually exists?
    //

OpenProcessExit:

    if (NULL != pobjProcess)
    {
        pobjProcess->ReleaseReference(pThread);
    }

    if (NULL != pobjProcessRegistered)
    {
        pobjProcessRegistered->ReleaseReference(pThread);
    }

    if (NO_ERROR != palError)
    {
        pThread->SetLastError(palError);
    }

    LOGEXIT("OpenProcess returns HANDLE %p\n", hProcess);
    PERF_EXIT(OpenProcess);
    return hProcess;
}

/*++
Function:
  EnumProcessModules

Abstract
  Returns a process's module list

Return
  TRUE if it succeeded, FALSE otherwise

Notes
  This API is tricky because the module handles are never closed/freed so there can't be any
  allocations for the module handle or name strings, etc. The "handles" are actually the base
  addresses of the modules. The module handles should only be used by GetModuleFileNameExW
  below.
--*/
BOOL
PALAPI
EnumProcessModules(
    IN HANDLE hProcess,
    OUT HMODULE *lphModule,
    IN DWORD cb,
    OUT LPDWORD lpcbNeeded)
{
    PERF_ENTRY(EnumProcessModules);
    ENTRY("EnumProcessModules(hProcess=0x%08x, cb=%d)\n", hProcess, cb);

    BOOL result = TRUE;
    DWORD count = 0;
    ProcessModules *listHead = GetProcessModulesFromHandle(hProcess, &count);
    if (listHead != NULL)
    {
        for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
        {
            if (cb <= 0)
            {
                break;
            }
            cb -= sizeof(HMODULE);
            *lphModule = (HMODULE)entry->GetBaseAddress();
            lphModule++;
        }
    }
    else
    {
        result = FALSE;
    }

    if (lpcbNeeded)
    {
        // This return value isn't exactly up to spec because it should return the actual
        // number of modules in the process even if "cb" isn't big enough but for our use
        // it works just fine.
        (*lpcbNeeded) = count * sizeof(HMODULE);
    }

    LOGEXIT("EnumProcessModules returns %d\n", result);
    PERF_EXIT(EnumProcessModules);
    return result;
}

/*++
Function:
  GetModuleFileNameExW

  Used only with module handles returned from EnumProcessModule (for dbgshim).

--*/
DWORD
PALAPI
GetModuleFileNameExW(
    IN HANDLE hProcess,
    IN HMODULE hModule,
    OUT LPWSTR lpFilename,
    IN DWORD nSize
)
{
    DWORD result = 0;
    DWORD count = 0;

    ProcessModules *listHead = GetProcessModulesFromHandle(hProcess, &count);
    if (listHead != NULL)
    {
        for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
        {
            if ((HMODULE)entry->GetBaseAddress() == hModule)
            {
                // Convert CHAR string into WCHAR string
                result = MultiByteToWideChar(CP_ACP, 0, entry->GetName(), -1, lpFilename, nSize);
                break;
            }
        }
    }

    return result;
}

/*++
Function:
 GetProcessModulesFromHandle

Abstract
  Returns a process's module list

Return
  ProcessModules * list

--*/
ProcessModules *
GetProcessModulesFromHandle(
    IN HANDLE hProcess,
    OUT LPDWORD lpCount)
{
    CPalThread* pThread = InternalGetCurrentThread();
    CProcProcessLocalData *pLocalData = NULL;
    ProcessModules *listHead = NULL;
    IPalObject *pobjProcess = NULL;
    IDataLock *pDataLock = NULL;
    PAL_ERROR palError = NO_ERROR;
    DWORD dwProcessId = 0;
    DWORD count = 0;

    _ASSERTE(lpCount != NULL);

    if (hPseudoCurrentProcess == hProcess)
    {
        pobjProcess = g_pobjProcess;
        pobjProcess->AddReference();
    }
    else
    {
        CAllowedObjectTypes aotProcess(otiProcess);

        palError = g_pObjectManager->ReferenceObjectByHandle(
            pThread,
            hProcess,
            &aotProcess,
            &pobjProcess);

        if (NO_ERROR != palError)
        {
            pThread->SetLastError(ERROR_INVALID_HANDLE);
            goto exit;
        }
    }

    palError = pobjProcess->GetProcessLocalData(
        pThread,
        WriteLock,
        &pDataLock,
        reinterpret_cast<void **>(&pLocalData));

    _ASSERTE(NO_ERROR == palError);

    dwProcessId = pLocalData->dwProcessId;
    listHead = pLocalData->pProcessModules;
    count = pLocalData->cProcessModules;

    // If the module list hasn't been created yet, create it now
    if (listHead == NULL)
    {
        listHead = CreateProcessModules(dwProcessId, &count);
        if (listHead == NULL)
        {
            pThread->SetLastError(ERROR_INVALID_PARAMETER);
            goto exit;
        }

        if (pLocalData != NULL)
        {
            pLocalData->pProcessModules = listHead;
            pLocalData->cProcessModules = count;
        }
    }

exit:
    if (NULL != pDataLock)
    {
        pDataLock->ReleaseLock(pThread, TRUE);
    }
    if (NULL != pobjProcess)
    {
        pobjProcess->ReleaseReference(pThread);
    }

    *lpCount = count;
    return listHead;
}

/*++
Function:
  CreateProcessModules

Abstract
  Returns a process's module list

Return
  ProcessModules * list

--*/
ProcessModules *
CreateProcessModules(
    IN DWORD dwProcessId,
    OUT LPDWORD lpCount)
{
    ProcessModules *listHead = NULL;
    _ASSERTE(lpCount != NULL);

#if defined(__APPLE__)

    // For OS X, the "vmmap" command outputs something similar to the /proc/*/maps file so popen the
    // command and read the relevant lines:
    //
    // ...
    // ==== regions for process 347  (non-writable and writable regions are interleaved)
    // REGION TYPE                      START - END             [ VSIZE] PRT/MAX SHRMOD  REGION DETAIL
    // __TEXT                 000000010446d000-0000000104475000 [   32K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/corerun
    // __DATA                 0000000104475000-0000000104476000 [    4K] rw-/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/corerun
    // __LINKEDIT             0000000104476000-000000010447a000 [   16K] r--/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/corerun
    // Kernel Alloc Once      000000010447a000-000000010447b000 [    4K] rw-/rwx SM=PRV
    // MALLOC (admin)         000000010447b000-000000010447c000 [    4K] r--/rwx SM=ZER
    // ...
    // MALLOC (admin)         00000001044ab000-00000001044ac000 [    4K] r--/rwx SM=PRV
    // __TEXT                 00000001044ac000-0000000104c84000 [ 8032K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 0000000104c84000-0000000104c85000 [    4K] rwx/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 0000000104c85000-000000010513b000 [ 4824K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 000000010513b000-000000010513c000 [    4K] rwx/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __TEXT                 000000010513c000-000000010516f000 [  204K] r-x/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __DATA                 000000010516f000-00000001051ce000 [  380K] rw-/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __DATA                 00000001051ce000-00000001051fa000 [  176K] rw-/rwx SM=PRV  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // __LINKEDIT             00000001051fa000-0000000105bac000 [ 9928K] r--/rwx SM=COW  /Users/mikem/coreclr/bin/Product/OSx.x64.Debug/libcoreclr.dylib
    // VM_ALLOCATE            0000000105bac000-0000000105bad000 [    4K] r--/rw- SM=SHM
    // MALLOC (admin)         0000000105bad000-0000000105bae000 [    4K] r--/rwx SM=ZER
    // MALLOC                 0000000105bae000-0000000105baf000 [    4K] rw-/rwx SM=ZER

    // OS X Sierra (10.12.4 Beta)
    // REGION TYPE                      START - END             [ VSIZE  RSDNT  DIRTY   SWAP] PRT/MAX SHRMOD PURGE    REGION DETAIL
    // Stack                  00007fff5a930000-00007fff5b130000 [ 8192K    32K    32K     0K] rw-/rwx SM=PRV          thread 0
    // __TEXT                 00007fffa4a0b000-00007fffa4a0d000 [    8K     8K     0K     0K] r-x/r-x SM=COW          /usr/lib/libSystem.B.dylib
    // __TEXT                 00007fffa4bbe000-00007fffa4c15000 [  348K   348K     0K     0K] r-x/r-x SM=COW          /usr/lib/libc++.1.dylib

    // NOTE: the module path can have spaces in the name
    // __TEXT                 0000000196220000-00000001965b4000 [ 3664K  2340K     0K     0K] r-x/rwx SM=COW          /Volumes/Builds/builds/devmain/rawproduct/debug/build/out/Applications/Microsoft Excel.app/Contents/SharedSupport/PowerQuery/libcoreclr.dylib

    // NOTE: Sometimes vmmap hides full paths to some process modules (.dylibs in non-system folders), causing debugger not to work.
    // __TEXT                 000000010d8bd000-000000010ddce000 [ 5188K  5188K     0K     0K] r-x/rwx SM=COW          /Users/USER/*/libcoreclr.dylib
    // So now we get modules information by iterating over regions using proc_pidinfo().  See dotnet/runtime#42888.
    int count = 0;

    uint64_t addr = 0;
    while (true)
    {
        struct proc_regionwithpathinfo rwpi;
        int sz = proc_pidinfo(dwProcessId, PROC_PIDREGIONPATHINFO, addr, &rwpi, sizeof rwpi);
        if (sz != sizeof rwpi)
        {
            if (sz == 0 && errno == EINVAL)
                break; // ok

            DestroyProcessModules(listHead);
            listHead = NULL;
            count = 0;
            break; // unknown error
        }

        const char *moduleName = rwpi.prp_vip.vip_path;

        bool dup = false;
        for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
        {
            if (strcmp(moduleName, entry->_name) == 0)
            {
                dup = true;
                break;
            }
        }

        // Does the offset in the module correspond to a valid MachO header?
        bool mightBeMachOHeader = rwpi.prp_prinfo.pri_offset == 0;

        if (!dup && mightBeMachOHeader)
        {
            int cbModuleName = strlen(moduleName) + 1;
            ProcessModules *entry = (ProcessModules *)malloc(sizeof(ProcessModules) + cbModuleName);
            if (entry == NULL)
            {
                DestroyProcessModules(listHead);
                listHead = NULL;
                count = 0;
                break; // no memory
            }
            memcpy_s(entry->_name, cbModuleName, moduleName, cbModuleName);
            entry->_minimumAddress = 0;
            entry->_baseAddress = (void *)rwpi.prp_prinfo.pri_address;
            entry->_next = listHead;
            listHead = entry;
            count++;
        }

        addr = rwpi.prp_prinfo.pri_address + rwpi.prp_prinfo.pri_size;
    }

    *lpCount = count;

#elif HAVE_PROCFS_MAPS

    // Here we read /proc/<pid>/maps file in order to parse it and figure out what it says
    // about a library we are looking for. This file looks something like this:
    //
    // [address]      [perms] [offset] [dev] [inode]     [pathname] - HEADER is not preset in an actual file
    //
    // 35b1800000-35b1820000 r-xp 00000000 08:02 135522  /usr/lib64/ld-2.15.so
    // 35b1a1f000-35b1a20000 r--p 0001f000 08:02 135522  /usr/lib64/ld-2.15.so
    // 35b1a20000-35b1a21000 rw-p 00020000 08:02 135522  /usr/lib64/ld-2.15.so
    // 35b1a21000-35b1a22000 rw-p 00000000 00:00 0       [heap]
    // 35b1c00000-35b1dac000 r-xp 00000000 08:02 135870  /usr/lib64/libc-2.15.so
    // 35b1dac000-35b1fac000 ---p 001ac000 08:02 135870  /usr/lib64/libc-2.15.so
    // 35b1fac000-35b1fb0000 r--p 001ac000 08:02 135870  /usr/lib64/libc-2.15.so
    // 35b1fb0000-35b1fb2000 rw-p 001b0000 08:02 135870  /usr/lib64/libc-2.15.so

    // Making something like: /proc/123/maps
    char mapFileName[100];
    char *line = NULL;
    size_t lineLen = 0;
    int count = 0;
    ssize_t read;

    INDEBUG(int chars = )
    snprintf(mapFileName, sizeof(mapFileName), "/proc/%d/maps", dwProcessId);
    _ASSERTE(chars > 0 && chars <= (int)sizeof(mapFileName));

    FILE *mapsFile = fopen(mapFileName, "r");
    if (mapsFile == NULL)
    {
        goto exit;
    }

    // Reading maps file line by line
    while ((read = getline(&line, &lineLen, mapsFile)) != -1)
    {
        void *startAddress, *endAddress, *offset;
        int devHi, devLo, inode;
        char moduleName[PATH_MAX];

        if (sscanf_s(line, "%p-%p %*[-rwxsp] %p %x:%x %d %[^\n]\n", &startAddress, &endAddress, &offset, &devHi, &devLo, &inode, moduleName, ARRAY_SIZE(moduleName)) == 7)
        {
            if (inode != 0)
            {
                bool dup = false;
                for (ProcessModules *entry = listHead; entry != NULL; entry = entry->GetNext())
                {
                    if (strcmp(moduleName, entry->GetName()) == 0)
                    {
                        if (entry->_baseAddress == 0 && offset == 0)
                        {
                            entry->_baseAddress = startAddress;
                        }
                        entry->_minimumAddress = std::min(startAddress, entry->_minimumAddress);
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                {
                    int cbModuleName = strlen(moduleName) + 1;
                    ProcessModules *entry = (ProcessModules *)malloc(sizeof(ProcessModules) + cbModuleName);
                    if (entry == NULL)
                    {
                        DestroyProcessModules(listHead);
                        listHead = NULL;
                        count = 0;
                        break;
                    }
                    strcpy_s(entry->_name, cbModuleName, moduleName);
                    entry->_baseAddress = 0;
                    entry->_minimumAddress = startAddress;
                    if (offset == 0)
                    {
                        entry->_baseAddress = startAddress;
                    }
                    entry->_next = listHead;
                    listHead = entry;
                    count++;
                }
            }
        }
    }

    *lpCount = count;

    free(line); // We didn't allocate line, but as per contract of getline we should free it
    fclose(mapsFile);
exit:

#else
    _ASSERTE(!"Not implemented on this platform");
#endif
    return listHead;
}

/*++
Function:
    DestroyProcessModules

Abstract
  Cleans up the process module table.

Return
  None

--*/
VOID
DestroyProcessModules(IN ProcessModules *listHead)
{
    for (ProcessModules *entry = listHead; entry != NULL; )
    {
        ProcessModules *next = entry->GetNext();
        free(entry);
        entry = next;
    }
}

/*++
Function:
  PROCAbort()

  Aborts the process after calling the shutdown cleanup handler. This function
  should be called instead of calling abort() directly.

Parameters:
  none

  Does not return
--*/
#if !defined(HOST_ARM)
PAL_NORETURN
#endif
VOID
PROCAbort(int signal, siginfo_t* siginfo)
{
    // Abort the process after waiting for the core dump to complete
    abort();
}

/*++
Function:
  PROCGetProcessIDFromHandle

Abstract
  Return the process ID from a process handle

Parameter
  hProcess:  process handle

Return
  Return the process ID, or 0 if it's not a valid handle
--*/
DWORD
PROCGetProcessIDFromHandle(
        HANDLE hProcess)
{
    PAL_ERROR palError;
    IPalObject *pobjProcess = NULL;
    CPalThread *pThread = InternalGetCurrentThread();

    DWORD dwProcessId = 0;

    if (hPseudoCurrentProcess == hProcess)
    {
        dwProcessId = gPID;
        goto PROCGetProcessIDFromHandleExit;
    }


    palError = g_pObjectManager->ReferenceObjectByHandle(
        pThread,
        hProcess,
        &aotProcess,
        &pobjProcess
        );

    if (NO_ERROR == palError)
    {
        IDataLock *pDataLock;
        CProcProcessLocalData *pLocalData;

        palError = pobjProcess->GetProcessLocalData(
            pThread,
            ReadLock,
            &pDataLock,
            reinterpret_cast<void **>(&pLocalData)
            );

        if (NO_ERROR == palError)
        {
            dwProcessId = pLocalData->dwProcessId;
            pDataLock->ReleaseLock(pThread, FALSE);
        }

        pobjProcess->ReleaseReference(pThread);
    }

PROCGetProcessIDFromHandleExit:

    return dwProcessId;
}

PAL_ERROR
CorUnix::InitializeProcessData(
    void
    )
{
    PAL_ERROR palError = NO_ERROR;
    bool fLockInitialized = FALSE;

    pGThreadList = NULL;
    g_dwThreadCount = 0;

    InternalInitializeCriticalSection(&g_csProcess);
    fLockInitialized = TRUE;

    if (NO_ERROR != palError)
    {
        if (fLockInitialized)
        {
            InternalDeleteCriticalSection(&g_csProcess);
        }
    }

    return palError;
}

/*++
Function:
  CreateInitialProcessAndThreadObjects

Abstract
  Creates the IPalObjects that represent the current process
  and the initial thread

Parameter
  pThread - the initial thread

Return
  PAL_ERROR
--*/

PAL_ERROR
CorUnix::CreateInitialProcessAndThreadObjects(
    CPalThread *pThread
    )
{
    PAL_ERROR palError = NO_ERROR;
    HANDLE hThread;
    IPalObject *pobjProcess = NULL;
    IDataLock *pDataLock;
    CProcProcessLocalData *pLocalData;
    CObjectAttributes oa;
    HANDLE hProcess;

    //
    // Create initial thread object
    //

    palError = CreateThreadObject(pThread, pThread, &hThread);
    if (NO_ERROR != palError)
    {
        goto CreateInitialProcessAndThreadObjectsExit;
    }

    //
    // This handle isn't needed
    //

    (void) g_pObjectManager->RevokeHandle(pThread, hThread);

    //
    // Create and initialize process object
    //

    palError = g_pObjectManager->AllocateObject(
        pThread,
        &otProcess,
        &oa,
        &pobjProcess
        );

    if (NO_ERROR != palError)
    {
        ERROR("Unable to allocate process object");
        goto CreateInitialProcessAndThreadObjectsExit;
    }

    palError = pobjProcess->GetProcessLocalData(
        pThread,
        WriteLock,
        &pDataLock,
        reinterpret_cast<void **>(&pLocalData)
        );

    if (NO_ERROR != palError)
    {
        ASSERT("Unable to access local data");
        goto CreateInitialProcessAndThreadObjectsExit;
    }

    pLocalData->dwProcessId = gPID;
    pLocalData->ps = PS_RUNNING;
    pDataLock->ReleaseLock(pThread, TRUE);

    palError = g_pObjectManager->RegisterObject(
        pThread,
        pobjProcess,
        &aotProcess,
        &hProcess,
        &g_pobjProcess
        );

    //
    // pobjProcess is invalidated by the call to RegisterObject, so
    // NULL it out here to prevent it from being released later
    //

    pobjProcess = NULL;

    if (NO_ERROR != palError)
    {
        ASSERT("Failure registering process object");
        goto CreateInitialProcessAndThreadObjectsExit;
    }

    //
    // There's no need to keep this handle around, so revoke
    // it now
    //

    g_pObjectManager->RevokeHandle(pThread, hProcess);

CreateInitialProcessAndThreadObjectsExit:

    if (NULL != pobjProcess)
    {
        pobjProcess->ReleaseReference(pThread);
    }

    return palError;
}


/*++
Function:
  PROCCleanupInitialProcess

Abstract
  Cleanup all the structures for the initial process.

Parameter
  VOID

Return
  VOID

--*/
VOID
PROCCleanupInitialProcess(VOID)
{
    CPalThread *pThread = InternalGetCurrentThread();

    InternalEnterCriticalSection(pThread, &g_csProcess);

    /* Free the application directory */
    free(g_lpwstrAppDir);

    /* Free the stored command line */
    free(g_lpwstrCmdLine);

    InternalLeaveCriticalSection(pThread, &g_csProcess);

    //
    // Object manager shutdown will handle freeing the underlying
    // thread and process data
    //

}

/*++
Function:
  PROCAddThread

Abstract
  Add a thread to the thread list of the current process

Parameter
  pThread:   Thread object

--*/
VOID
CorUnix::PROCAddThread(
    CPalThread *pCurrentThread,
    CPalThread *pTargetThread
    )
{
    /* protect the access of the thread list with critical section for
       mutithreading access */
    InternalEnterCriticalSection(pCurrentThread, &g_csProcess);

    pTargetThread->SetNext(pGThreadList);
    pGThreadList = pTargetThread;
    g_dwThreadCount += 1;

    TRACE("Thread 0x%p (id %#x) added to the process thread list\n",
          pTargetThread, pTargetThread->GetThreadId());

    InternalLeaveCriticalSection(pCurrentThread, &g_csProcess);
}


/*++
Function:
  PROCRemoveThread

Abstract
  Remove a thread form the thread list of the current process

Parameter
  CPalThread *pThread : thread object to remove

(no return value)
--*/
VOID
CorUnix::PROCRemoveThread(
    CPalThread *pCurrentThread,
    CPalThread *pTargetThread
    )
{
    CPalThread *curThread, *prevThread;

    /* protect the access of the thread list with critical section for
       mutithreading access */
    InternalEnterCriticalSection(pCurrentThread, &g_csProcess);

    curThread = pGThreadList;

    /* if thread list is empty */
    if (curThread == NULL)
    {
        ASSERT("Thread list is empty.\n");
        goto EXIT;
    }

    /* do we remove the first thread? */
    if (curThread == pTargetThread)
    {
        pGThreadList =  curThread->GetNext();
        TRACE("Thread 0x%p (id %#x) removed from the process thread list\n",
            pTargetThread, pTargetThread->GetThreadId());
        goto EXIT;
    }

    prevThread = curThread;
    curThread = curThread->GetNext();
    /* find the thread to remove */
    while (curThread != NULL)
    {
        if (curThread == pTargetThread)
        {
            /* found, fix the chain list */
            prevThread->SetNext(curThread->GetNext());
            g_dwThreadCount -= 1;
            TRACE("Thread %p removed from the process thread list\n", pTargetThread);
            goto EXIT;
        }

        prevThread = curThread;
        curThread = curThread->GetNext();
    }

    WARN("Thread %p not removed (it wasn't found in the list)\n", pTargetThread);

EXIT:
    InternalLeaveCriticalSection(pCurrentThread, &g_csProcess);
}


/*++
Function:
  PROCProcessLock

Abstract
  Enter the critical section associated to the current process

Parameter
  void

Return
  void
--*/
VOID
PROCProcessLock(
    VOID)
{
    CPalThread * pThread =
        (PALIsThreadDataInitialized() ? InternalGetCurrentThread() : NULL);

    InternalEnterCriticalSection(pThread, &g_csProcess);
}


/*++
Function:
  PROCProcessUnlock

Abstract
  Leave the critical section associated to the current process

Parameter
  void

Return
  void
--*/
VOID
PROCProcessUnlock(
    VOID)
{
    CPalThread * pThread =
        (PALIsThreadDataInitialized() ? InternalGetCurrentThread() : NULL);

    InternalLeaveCriticalSection(pThread, &g_csProcess);
}


/*++
Function:
  TerminateCurrentProcessNoExit

Abstract:
    Terminate current Process, but leave the caller alive

Parameters:
    BOOL bTerminateUnconditionally - If this is set, the PAL will exit as
    quickly as possible. In particular, it will not unload DLLs.

Return value :
    No return

Note:
  This function is used in ExitThread and TerminateProcess

--*/
VOID
CorUnix::TerminateCurrentProcessNoExit(BOOL bTerminateUnconditionally)
{
    BOOL locked;
    DWORD old_terminator;

    old_terminator = InterlockedCompareExchange(&terminator, GetCurrentThreadId(), 0);

    if (0 != old_terminator && GetCurrentThreadId() != old_terminator)
    {
        /* another thread has already initiated the termination process. we
           could just block on the PALInitLock critical section, but then
           PROCSuspendOtherThreads would hang... so sleep forever here, we're
           terminating anyway

           Update: [TODO] PROCSuspendOtherThreads has been removed. Can this
           code be changed? */

        /* note that if *this* thread has already started the termination
           process, we want to proceed. the only way this can happen is if a
           call to DllMain (from ExitProcess) brought us here (because DllMain
           called ExitProcess, or TerminateProcess, or ExitThread);
           TerminateProcess won't call DllMain, so there's no danger to get
           caught in an infinite loop */
        WARN("termination already started from another thread; blocking.\n");
        poll(NULL, 0, INFTIM);
    }

    /* Try to lock the initialization count to prevent multiple threads from
       terminating/initializing the PAL simultaneously */

    /* note : it's also important to take this lock before the process lock,
       because Init/Shutdown take the init lock, and the functions they call
       may take the process lock. We must do it in the same order to avoid
       deadlocks */

    locked = PALInitLock();
    if(locked && PALIsInitialized())
    {
        PALCommonCleanup();
    }
}
#ifdef __APPLE__
bool GetApplicationContainerFolder(PathCharString& buffer, const char *applicationGroupId, int applicationGroupIdLength)
{
    const char *homeDir = getpwuid(getuid())->pw_dir;
    int homeDirLength = strlen(homeDir);

    // The application group container folder is defined as:
    // /user/{loginname}/Library/Group Containers/{AppGroupId}/
    return buffer.Set(homeDir, homeDirLength)
        && buffer.Append(APPLICATION_CONTAINER_BASE_PATH_SUFFIX)
        && buffer.Append(applicationGroupId, applicationGroupIdLength)
        && buffer.Append('/');
}
#endif // __APPLE__


/* Internal function definitions **********************************************/

/*++
Function:
  getFileName

Abstract:
    Helper function for CreateProcessW, it retrieves the executable filename
    from the application name, and the command line.

Parameters:
    IN  lpApplicationName:  first parameter from CreateProcessW (an unicode string)
    IN  lpCommandLine: second parameter from CreateProcessW (an unicode string)
    OUT lpFileName: file to be executed (the new process)

Return:
    TRUE: if the file name is retrieved
    FALSE: otherwise

--*/
static
BOOL
getFileName(
       LPCWSTR lpApplicationName,
       LPWSTR lpCommandLine,
       PathCharString& lpPathFileName)
{
    LPWSTR lpEnd;
    WCHAR wcEnd;
    char * lpFileName;
    PathCharString lpFileNamePS;
    char *lpTemp;

    if (lpApplicationName)
    {
        int length = WideCharToMultiByte(CP_ACP, 0, lpApplicationName, -1,
                                            NULL, 0, NULL, NULL);

        /* if only a file name is specified, prefix it with "./" */
        if ((*lpApplicationName != '.') && (*lpApplicationName != '/'))
        {
            length += 2;
            lpTemp = lpPathFileName.OpenStringBuffer(length);

            if (strcpy_s(lpTemp, length, "./") != SAFECRT_SUCCESS)
            {
                ERROR("strcpy_s failed!\n");
                return FALSE;
            }
            lpTemp+=2;

       }
       else
       {
            lpTemp = lpPathFileName.OpenStringBuffer(length);
       }

        /* Convert to ASCII */
        length = WideCharToMultiByte(CP_ACP, 0, lpApplicationName, -1,
                                     lpTemp, length, NULL, NULL);
        if (length == 0)
        {
            lpPathFileName.CloseBuffer(0);
            ASSERT("WideCharToMultiByte failure\n");
            return FALSE;
        }

        lpPathFileName.CloseBuffer(length -1);

        return TRUE;
    }
    else
    {
        /* use the Command line */

        /* filename should be the first token of the command line */

        /* first skip all leading whitespace */
        lpCommandLine = UTIL_inverse_wcspbrk(lpCommandLine,W16_WHITESPACE);
        if(NULL == lpCommandLine)
        {
            ERROR("CommandLine contains only whitespace!\n");
            return FALSE;
        }

        /* check if it is starting with a quote (") character */
        if (*lpCommandLine == 0x0022)
        {
            lpCommandLine++; /* skip the quote */

            /* file name ends with another quote */
            lpEnd = PAL_wcschr(lpCommandLine+1, 0x0022);

            /* if no quotes found, set lpEnd to the end of the Command line */
            if (lpEnd == NULL)
                lpEnd = lpCommandLine + PAL_wcslen(lpCommandLine);
        }
        else
        {
            /* filename is end out by a whitespace */
            lpEnd = PAL_wcspbrk(lpCommandLine, W16_WHITESPACE);

            /* if no whitespace found, set lpEnd to end of the Command line */
            if (lpEnd == NULL)
            {
                lpEnd = lpCommandLine + PAL_wcslen(lpCommandLine);
            }
        }

        if (lpEnd == lpCommandLine)
        {
            ERROR("application name and command line are both empty!\n");
            return FALSE;
        }

        /* replace the last character by a null */
        wcEnd = *lpEnd;
        *lpEnd = 0x0000;

        /* Convert to ASCII */
        int size = 0;
        int length = (PAL_wcslen(lpCommandLine)+1) * sizeof(WCHAR);
        lpFileName = lpFileNamePS.OpenStringBuffer(length);
        if (NULL == lpFileName)
        {
            ERROR("Not Enough Memory!\n");
            return FALSE;
        }
        if (!(size = WideCharToMultiByte(CP_ACP, 0, lpCommandLine, -1,
                                 lpFileName, length, NULL, NULL)))
        {
            ASSERT("WideCharToMultiByte failure\n");
            return FALSE;
        }

        lpFileNamePS.CloseBuffer(size - 1);
        /* restore last character */
        *lpEnd = wcEnd;

        if (!getPath(lpFileNamePS, lpPathFileName))
        {
            /* file is not in the path */
            return FALSE;
        }
    }
    return TRUE;
}

/*++
Function:
    checkFileType

Abstract:
    Return the type of the file.

Parameters:
    IN  lpFileName:  file name

Return:
    FILE_DIR: Directory
    FILE_UNIX: Unix executable file
    FILE_ERROR: Error
--*/
static
int
checkFileType( LPCSTR lpFileName)
{
    struct stat stat_data;

    /* check if the file exist */
    if ( access(lpFileName, F_OK) != 0 )
    {
        return FILE_ERROR;
    }

    /* if it's not a PE/COFF file, check if it is executable */
    if ( -1 != stat( lpFileName, &stat_data ) )
    {
        if((stat_data.st_mode & S_IFMT) == S_IFDIR )
        {
            /*The given file is a directory*/
            return FILE_DIR;
        }
        if ( UTIL_IsExecuteBitsSet( &stat_data ) )
        {
            return FILE_UNIX;
        }
        else
        {
            return FILE_ERROR;
        }
    }
    return FILE_ERROR;

}


/*++
Function:
  buildArgv

Abstract:
    Helper function for CreateProcessW, it builds the array of argument in
    a format than can be passed to execve function.lppArgv is allocated
    in this function and must be freed by the caller.

Parameters:
    IN  lpCommandLine: second parameter from CreateProcessW (an unicode string)
    IN  lpAppPath: canonical name of the application to launched
    OUT lppArgv: array of arguments to be passed to the new process

Return:
    the number of arguments

note: this doesn't yet match precisely the behavior of Windows, but should be
sufficient.
what's here:
1) stripping nonquoted whitespace
2) handling of quoted parameters and quoted "parts" of parameters, removal of
   doublequotes (<aaaa"b bbb b"ccc> becomes <aaaab bbb bccc>)
3) \" as an escaped doublequote, both within doublequoted sequences and out
what's known missing :
1) \\ as an escaped backslash, but only if the string of '\'
   is followed by a " (escaped or not)
2) "alternate" escape sequence : double-doublequote within a double-quoted
    argument (<"aaa a""aa aaa">) expands to a single-doublequote(<aaa a"aa aaa>)
note that there may be other special cases
--*/
static
char **
buildArgv(
      LPCWSTR lpCommandLine,
      PathCharString& lpAppPath,
      UINT *pnArg)
{
    CPalThread *pThread = NULL;
    UINT iWlen;
    char *lpAsciiCmdLine;
    char *pChar;
    char **lppArgv;
    char **lppTemp;
    UINT i,j;

    *pnArg = 0;

    iWlen = WideCharToMultiByte(CP_ACP,0,lpCommandLine,-1,NULL,0,NULL,NULL);

    if(0 == iWlen)
    {
        ASSERT("Can't determine length of command line\n");
        return NULL;
    }

    pThread = InternalGetCurrentThread();
    /* make sure to allocate enough space, up for the worst case scenario */
    int iLength = (iWlen + lpAppPath.GetCount() + 2);
    lpAsciiCmdLine = (char *) malloc(iLength);

    if (lpAsciiCmdLine == NULL)
    {
        ERROR("Unable to allocate memory\n");
        return NULL;
    }

    pChar = lpAsciiCmdLine;

    /* put the canonical name of the application as the first parameter */
    if ((strcpy_s(lpAsciiCmdLine, iLength, "\"") != SAFECRT_SUCCESS) ||
        (strcat_s(lpAsciiCmdLine, iLength, lpAppPath) != SAFECRT_SUCCESS) ||
        (strcat_s(lpAsciiCmdLine, iLength,  "\"") != SAFECRT_SUCCESS) ||
        (strcat_s(lpAsciiCmdLine, iLength, " ") != SAFECRT_SUCCESS))
    {
        ERROR("strcpy_s/strcat_s failed!\n");
        free(lpAsciiCmdLine);
        return NULL;
    }

    pChar = lpAsciiCmdLine + strlen (lpAsciiCmdLine);

    /* let's skip the first argument in the command line */

    /* strip leading whitespace; function returns NULL if there's only
        whitespace, so the if statement below will work correctly */
    lpCommandLine = UTIL_inverse_wcspbrk((LPWSTR)lpCommandLine, W16_WHITESPACE);

    if (lpCommandLine)
    {
        LPCWSTR stringstart = lpCommandLine;

        do
        {
            /* find first whitespace or dquote character */
            lpCommandLine = PAL_wcspbrk(lpCommandLine,W16_WHITESPACE_DQUOTE);
            if(NULL == lpCommandLine)
            {
                /* no whitespace or dquote found : first arg is only arg */
                break;
            }
            else if('"' == *lpCommandLine)
            {
                /* got a dquote; skip over it if it's escaped; make sure we
                    don't try to look before the first character in the
                    string */
                if(lpCommandLine > stringstart && '\\' == lpCommandLine[-1])
                {
                    lpCommandLine++;
                    continue;
                }

                /* found beginning of dquoted sequence, run to the end */
                /* don't stop if we hit an escaped dquote */
                lpCommandLine++;
                while( *lpCommandLine )
                {
                    lpCommandLine = PAL_wcschr(lpCommandLine, '"');
                    if(NULL == lpCommandLine)
                    {
                        /* no ending dquote, arg runs to end of string */
                        break;
                    }
                    if('\\' != lpCommandLine[-1])
                    {
                        /* dquote is not escaped, dquoted sequence is over*/
                        break;
                    }
                    lpCommandLine++;
                }
                if(NULL == lpCommandLine || '\0' == *lpCommandLine)
                {
                    /* no terminating dquote */
                    break;
                }

                /* step over dquote, keep looking for end of arg */
                lpCommandLine++;
            }
            else
            {
                /* found whitespace : end of arg. */
                lpCommandLine++;
                break;
            }
        }while(lpCommandLine);
    }

    /* Convert to ASCII */
    if (lpCommandLine)
    {
        if (!WideCharToMultiByte(CP_ACP, 0, lpCommandLine, -1,
                                 pChar, iWlen+1, NULL, NULL))
        {
            ASSERT("Unable to convert to a multibyte string\n");
            free(lpAsciiCmdLine);
            return NULL;
        }
    }

    pChar = lpAsciiCmdLine;

    /* loops through all the arguments, to find out how many arguments there
       are; while looping replace whitespace by \0 */

    /* skip leading whitespace (and replace by '\0') */
    /* note : there shouldn't be any, command starts either with PE loader name
       or computed application path, but this won't hurt */
    while (*pChar)
    {
        if (!isspace((unsigned char) *pChar))
        {
           break;
        }
        WARN("unexpected whitespace in command line!\n");
        *pChar++ = '\0';
    }

    while (*pChar)
    {
        (*pnArg)++;

        /* find end of current arg */
        while(*pChar && !isspace((unsigned char) *pChar))
        {
            if('"' == *pChar)
            {
                /* skip over dquote if it's escaped; make sure we don't try to
                   look before the start of the string for the \ */
                if(pChar > lpAsciiCmdLine && '\\' == pChar[-1])
                {
                    pChar++;
                    continue;
                }

                /* found leading dquote : look for ending dquote */
                pChar++;
                while (*pChar)
                {
                    pChar = strchr(pChar,'"');
                    if(NULL == pChar)
                    {
                        /* no ending dquote found : argument extends to the end
                           of the string*/
                        break;
                    }
                    if('\\' != pChar[-1])
                    {
                        /* found a dquote, and it's not escaped : quoted
                           sequence is over*/
                        break;
                    }
                    /* found a dquote, but it was escaped : skip over it, keep
                       looking */
                    pChar++;
                }
                if(NULL == pChar || '\0' == *pChar)
                {
                    /* reached the end of the string : we're done */
                    break;
                }
            }
            pChar++;
        }
        if(NULL == pChar)
        {
            /* reached the end of the string : we're done */
            break;
        }
        /* reached end of arg; replace trailing whitespace by '\0', to split
           arguments into separate strings */
        while (isspace((unsigned char) *pChar))
        {
            *pChar++ = '\0';
        }
    }

    /* allocate lppargv according to the number of arguments
       in the command line */
    lppArgv = (char **) malloc((((*pnArg)+1) * sizeof(char *)));

    if (lppArgv == NULL)
    {
        free(lpAsciiCmdLine);
        return NULL;
    }

    lppTemp = lppArgv;

    /* at this point all parameters are separated by NULL
       we need to fill the array of arguments; we must also remove all dquotes
       from arguments (new process shouldn't see them) */
    for (i = *pnArg, pChar = lpAsciiCmdLine; i; i--)
    {
        /* skip NULLs */
        while (!*pChar)
        {
            pChar++;
        }

        *lppTemp = pChar;

        /* go to the next parameter, removing dquotes as we go along */
        j = 0;
        while (*pChar)
        {
            /* copy character if it's not a dquote */
            if('"' != *pChar)
            {
                /* if it's the \ of an escaped dquote, skip over it, we'll
                   copy the " instead */
                if( '\\' == pChar[0] && '"' == pChar[1] )
                {
                    pChar++;
                }
                (*lppTemp)[j++] = *pChar;
            }
            pChar++;
        }
        /* re-NULL terminate the argument */
        (*lppTemp)[j] = '\0';

        lppTemp++;
    }

    *lppTemp = NULL;

    return lppArgv;
}


/*++
Function:
  getPath

Abstract:
    Helper function for CreateProcessW, it looks in the path environment
    variable to find where the process to executed is.

Parameters:
    IN  lpFileName: file name to search in the path
    OUT lpPathFileName: returned string containing the path and the filename

Return:
    TRUE if found
    FALSE otherwise
--*/
static
BOOL
getPath(
      PathCharString& lpFileNameString,
      PathCharString& lpPathFileName)
{
    LPSTR lpPath;
    LPSTR lpNext;
    LPSTR lpCurrent;
    LPWSTR lpwstr;
    INT n;
    INT nextLen;
    INT slashLen;
    CPalThread *pThread = NULL;
    LPCSTR lpFileName = lpFileNameString.GetString();

    /* if a path is specified, only look there */
    if(strchr(lpFileName, '/'))
    {
        if (access (lpFileName, F_OK) == 0)
        {
            if (!lpPathFileName.Set(lpFileNameString))
            {
                TRACE("Set of StackString failed!\n");
                return FALSE;
            }

            TRACE("file %s exists\n", lpFileName);
            return TRUE;
        }
        else
        {
            TRACE("file %s doesn't exist.\n", lpFileName);
            return FALSE;
        }
    }

    /* first look in directory from which the application loaded */
    lpwstr = g_lpwstrAppDir;

    if (lpwstr)
    {
        /* convert path to multibyte, check buffer size */
        n = WideCharToMultiByte(CP_ACP, 0, lpwstr, -1, NULL, 0,
            NULL, NULL);

        if (!lpPathFileName.Reserve(n + lpFileNameString.GetCount() + 1 ))
        {
            ERROR("StackString Reserve failed!\n");
            return FALSE;
        }

        lpPath = lpPathFileName.OpenStringBuffer(n);

        n = WideCharToMultiByte(CP_ACP, 0, lpwstr, -1, lpPath, n,
            NULL, NULL);

        if (n == 0)
        {
            lpPathFileName.CloseBuffer(0);
            ASSERT("WideCharToMultiByte failure!\n");
            return FALSE;
        }

        lpPathFileName.CloseBuffer(n - 1);

        lpPathFileName.Append("/", 1);
        lpPathFileName.Append(lpFileNameString);

        if (access(lpPathFileName, F_OK) == 0)
        {
            TRACE("found %s in application directory (%s)\n", lpFileName, lpPathFileName.GetString());
            return TRUE;
        }
    }

    /* then try the current directory */
    if (!lpPathFileName.Reserve(lpFileNameString.GetCount()  + 2))
    {
        ERROR("StackString Reserve failed!\n");
        return FALSE;
    }

    lpPathFileName.Set("./", 2);
    lpPathFileName.Append(lpFileNameString);

    if (access (lpPathFileName, R_OK) == 0)
    {
        TRACE("found %s in current directory.\n", lpFileName);
        return TRUE;
    }

    pThread = InternalGetCurrentThread();

    /* Then try to look in the path */
    lpPath = EnvironGetenv("PATH");

    if (!lpPath)
    {
        ERROR("EnvironGetenv returned NULL for $PATH\n");
        return FALSE;
    }

    lpNext = lpPath;

    /* search in every path directory */
    TRACE("looking for file %s in $PATH (%s)\n", lpFileName, lpPath);
    while (lpNext)
    {
        /* skip all leading ':' */
        while(*lpNext==':')
        {
            lpNext++;
        }

        /* search for ':' */
        lpCurrent = strchr(lpNext, ':');
        if (lpCurrent)
        {
            *lpCurrent++ = '\0';
        }

        nextLen = strlen(lpNext);
        slashLen = (lpNext[nextLen-1] == '/') ? 0:1;

        if (!lpPathFileName.Reserve(nextLen + lpFileNameString.GetCount() + 1))
        {
            free(lpPath);
            ERROR("StackString ran out of memory for full path\n");
            return FALSE;
        }

        lpPathFileName.Set(lpNext, nextLen);

        if( slashLen == 1)
        {
            /* append a '/' if there's no '/' at the end of the path */
            lpPathFileName.Append("/", 1);
        }

        lpPathFileName.Append(lpFileNameString);

        if ( access (lpPathFileName, F_OK) == 0)
        {
            TRACE("Found %s in $PATH element %s\n", lpFileName, lpNext);
            free(lpPath);
            return TRUE;
        }

        lpNext = lpCurrent;  /* search in the next directory */
    }

    free(lpPath);
    TRACE("File %s not found in $PATH\n", lpFileName);
    return FALSE;
}
