// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


#include "common.h"
#include "stubmgr.h"
#include "virtualcallstub.h"
#include "dllimportcallback.h"
#include "stubhelpers.h"
#include "asmconstants.h"
#ifdef FEATURE_COMINTEROP
#include "olecontexthelpers.h"
#endif

#ifdef LOGGING
const char *GetTType( TraceType tt)
{
    LIMITED_METHOD_CONTRACT;

    switch( tt )
    {
        case TRACE_ENTRY_STUB:                return "TRACE_ENTRY_STUB";
        case TRACE_STUB:                      return "TRACE_STUB";
        case TRACE_UNMANAGED:                 return "TRACE_UNMANAGED";
        case TRACE_MANAGED:                   return "TRACE_MANAGED";
        case TRACE_FRAME_PUSH:                return "TRACE_FRAME_PUSH";
        case TRACE_MGR_PUSH:                  return "TRACE_MGR_PUSH";
        case TRACE_OTHER:                     return "TRACE_OTHER";
        case TRACE_UNJITTED_METHOD:           return "TRACE_UNJITTED_METHOD";
        case TRACE_MULTICAST_DELEGATE_HELPER: return "TRACE_MULTICAST_DELEGATE_HELPER";
        case TRACE_EXTERNAL_METHOD_FIXUP:     return "TRACE_EXTERNAL_METHOD_FIXUP";
    }
    return "TRACE_REALLY_WACKED";
}

void LogTraceDestination(const char * szHint, PCODE stubAddr, TraceDestination * pTrace)
{
    LIMITED_METHOD_CONTRACT;
    if (pTrace->GetTraceType() == TRACE_UNJITTED_METHOD)
    {
        MethodDesc * md = pTrace->GetMethodDesc();
        LOG((LF_CORDB, LL_INFO10000, "'%s' yields '%s' to method %p for input %p.\n",
            szHint, GetTType(pTrace->GetTraceType()),
            md, stubAddr));
    }
    else
    {
        LOG((LF_CORDB, LL_INFO10000, "'%s' yields '%s' to address %p for input %p.\n",
            szHint, GetTType(pTrace->GetTraceType()),
            pTrace->GetAddress(), stubAddr));
    }
}
#endif

#ifdef _DEBUG
// Get a string representation of this TraceDestination
// Uses the supplied buffer to store the memory (or may return a string literal).
const CHAR * TraceDestination::DbgToString(SString & buffer)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    const CHAR * pValue = "unknown";

#ifndef DACCESS_COMPILE
    if (!StubManager::IsStubLoggingEnabled())
    {
        return "<unavailable while native-debugging>";
    }
    // Now that we know we're not interop-debugging, we can safely call new.
    SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;


    FAULT_NOT_FATAL();

    EX_TRY
    {
        switch(this->type)
        {
            case TRACE_ENTRY_STUB:
                buffer.Printf("TRACE_ENTRY_STUB(addr=%p)", GetAddress());
                pValue = buffer.GetUTF8();
                break;

            case TRACE_STUB:
                buffer.Printf("TRACE_STUB(addr=%p)", GetAddress());
                pValue = buffer.GetUTF8();
                break;

            case TRACE_UNMANAGED:
                buffer.Printf("TRACE_UNMANAGED(addr=%p)", GetAddress());
                pValue = buffer.GetUTF8();
                break;

            case TRACE_MANAGED:
                buffer.Printf("TRACE_MANAGED(addr=%p)", GetAddress());
                pValue = buffer.GetUTF8();
                break;

            case TRACE_UNJITTED_METHOD:
            {
                MethodDesc * md = this->GetMethodDesc();
                buffer.Printf("TRACE_UNJITTED_METHOD(md=%p, %s::%s)", md, md->m_pszDebugClassName, md->m_pszDebugMethodName);
                pValue = buffer.GetUTF8();
            }
                break;

            case TRACE_FRAME_PUSH:
                buffer.Printf("TRACE_FRAME_PUSH(addr=%p)", GetAddress());
                pValue = buffer.GetUTF8();
                break;

            case TRACE_MGR_PUSH:
                buffer.Printf("TRACE_MGR_PUSH(addr=%p, sm=%s)", GetAddress(), this->GetStubManager()->DbgGetName());
                pValue = buffer.GetUTF8();
                break;

            case TRACE_MULTICAST_DELEGATE_HELPER:
                pValue = "TRACE_MULTICAST_DELEGATE_HELPER";
                break;

            case TRACE_EXTERNAL_METHOD_FIXUP:
                pValue = "TRACE_EXTERNAL_METHOD_FIXUP";
                break;

            case TRACE_OTHER:
                pValue = "TRACE_OTHER";
                break;
        }
    }
    EX_CATCH
    {
        pValue = "(OOM while printing TD)";
    }
    EX_END_CATCH
#endif
    return pValue;
}
#endif


void TraceDestination::InitForUnjittedMethod(MethodDesc * pDesc)
{
    CONTRACTL
    {
        GC_NOTRIGGER;
        NOTHROW;
        MODE_ANY;

        PRECONDITION(CheckPointer(pDesc));
    }
    CONTRACTL_END;

    _ASSERTE(pDesc->SanityCheck());

    {
        // If this is a wrapper stub, then find the real method that it will go to and patch that.
        // This is more than just a convenience - converted wrapper MD to real MD is required for correct behavior.
        // Wrapper MDs look like unjitted MethodDescs. So when the debugger patches one,
        // it won't actually bind + apply the patch (it'll wait for the jit-complete instead).
        // But if the wrapper MD is for prejitted code, then we'll never get the Jit-complete.
        // Thus it'll miss the patch completely.
        if (pDesc->IsWrapperStub())
        {
            MethodDesc * pNewDesc = NULL;

            FAULT_NOT_FATAL();


#ifndef DACCESS_COMPILE
            EX_TRY
            {
                pNewDesc = pDesc->GetExistingWrappedMethodDesc();
            }
            EX_CATCH
            {
                // In case of an error, we'll just stick w/ the original method desc.
            } EX_END_CATCH
#else
            // @todo - DAC needs this too, but the method is currently not DACized.
            // However, we don't throw here b/c the error may not be fatal.
            // DacNotImpl();
#endif

            if (pNewDesc != NULL)
            {
                pDesc = pNewDesc;

                LOG((LF_CORDB, LL_INFO10000, "TD::UnjittedMethod: wrapper md: %p --> %p\n", pDesc, pNewDesc));

            }
        }
    }


    this->type = TRACE_UNJITTED_METHOD;
    this->pDesc = pDesc;
    this->stubManager = NULL;
}


// Initialize statics.
#ifdef _DEBUG
SString * StubManager::s_pDbgStubManagerLog = NULL;
CrstStatic StubManager::s_DbgLogCrst;

#endif

SPTR_IMPL(StubManager, StubManager, g_pFirstManager);

CrstStatic StubManager::s_StubManagerListCrst;

//-----------------------------------------------------------
// For perf reasons, the stub managers are now kept in a two
// tier system: all stub managers but the VirtualStubManagers
// are in the first tier. A VirtualStubManagerManager takes
// care of all VirtualStubManagers, and is iterated last of
// all. It does a smarter job of looking up the owning
// manager for virtual stubs, checking the current and shared
// appdomains before checking the remaining managers.
//
// Thus, this iterator will run the regular list until it
// hits the end, then it will check the VSMM, then it will
// end.
//-----------------------------------------------------------
class StubManagerIterator
{
  public:
    StubManagerIterator();
    ~StubManagerIterator();

    void Reset();
    BOOL Next();
    PTR_StubManager Current();

  protected:
    enum SMI_State
    {
        SMI_START,
        SMI_NORMAL,
        SMI_VIRTUALCALLSTUBMANAGER,
        SMI_END
    };

    SMI_State               m_state;
    PTR_StubManager m_pCurMgr;
    SimpleReadLockHolder    m_lh;
};

//-----------------------------------------------------------
// Ctor
//-----------------------------------------------------------
StubManagerIterator::StubManagerIterator()
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;

    Reset();
}

void StubManagerIterator::Reset()
{
    LIMITED_METHOD_DAC_CONTRACT;
    m_pCurMgr = NULL;
    m_state = SMI_START;
}

//-----------------------------------------------------------
// Ctor
//-----------------------------------------------------------
StubManagerIterator::~StubManagerIterator()
{
    LIMITED_METHOD_DAC_CONTRACT;
}

//-----------------------------------------------------------
// Move to the next element. Iterators are created at
// start-1, so must call Next before using Current
//-----------------------------------------------------------
BOOL StubManagerIterator::Next()
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
#ifndef DACCESS_COMPILE
        CAN_TAKE_LOCK;         // because of m_lh.Assign()
#else
        CANNOT_TAKE_LOCK;
#endif
    }
    CONTRACTL_END;

    SUPPORTS_DAC;

    do {
        if (m_state == SMI_START) {
            m_state = SMI_NORMAL;
            m_pCurMgr = StubManager::g_pFirstManager;
        }
        else if (m_state == SMI_NORMAL) {
            if (m_pCurMgr != NULL) {
                m_pCurMgr = m_pCurMgr->m_pNextManager;
            }
            else {
                // If we've reached the end of the regular list of stub managers, then we
                // set the VirtualCallStubManagerManager is the current item (effectively
                // forcing it to always be the last manager checked).
                m_state = SMI_VIRTUALCALLSTUBMANAGER;
                VirtualCallStubManagerManager *pVCSMMgr = VirtualCallStubManagerManager::GlobalManager();
                m_pCurMgr = PTR_StubManager(pVCSMMgr);
#ifndef DACCESS_COMPILE
                m_lh.Assign(&pVCSMMgr->m_RWLock);
#endif
            }
        }
        else if (m_state == SMI_VIRTUALCALLSTUBMANAGER) {
            m_state = SMI_END;
            m_pCurMgr = NULL;
#ifndef DACCESS_COMPILE
            m_lh.Clear();
#endif
        }
    } while (m_state != SMI_END && m_pCurMgr == NULL);

    CONSISTENCY_CHECK(m_state == SMI_END || m_pCurMgr != NULL);
    return (m_state != SMI_END);
}

//-----------------------------------------------------------
// Get the current contents of the iterator
//-----------------------------------------------------------
PTR_StubManager StubManagerIterator::Current()
{
    LIMITED_METHOD_DAC_CONTRACT;
    CONSISTENCY_CHECK(m_state != SMI_START);
    CONSISTENCY_CHECK(m_state != SMI_END);
    CONSISTENCY_CHECK(CheckPointer(m_pCurMgr));

    return m_pCurMgr;
}

#ifndef DACCESS_COMPILE
//-----------------------------------------------------------
//-----------------------------------------------------------
StubManager::StubManager()
  : m_pNextManager(NULL)
{
    LIMITED_METHOD_CONTRACT;
}

//-----------------------------------------------------------
//-----------------------------------------------------------
StubManager::~StubManager()
{
    CONTRACTL {
        NOTHROW;
        GC_NOTRIGGER;
        CAN_TAKE_LOCK;     // StubManager::UnlinkStubManager uses a crst
        PRECONDITION(CheckPointer(this));
    } CONTRACTL_END;

    UnlinkStubManager(this);
}
#endif // #ifndef DACCESS_COMPILE

#ifdef _DEBUG_IMPL
//-----------------------------------------------------------
// Verify that the stub is owned by the given stub manager
// and no other stub manager. If a stub is claimed by multiple managers,
// then the wrong manager may claim ownership and improperly trace the stub.
//-----------------------------------------------------------
BOOL StubManager::IsSingleOwner(PCODE stubAddress, StubManager * pOwner)
{
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_FORBID_FAULT;
    STATIC_CONTRACT_CAN_TAKE_LOCK;         // courtesy StubManagerIterator

    // ensure this stubmanager owns it.
    _ASSERTE(pOwner != NULL);

    // ensure nobody else does.
    bool ownerFound = false;
    int count = 0;
    StubManagerIterator it;
    while (it.Next())
    {
        // Callers would have iterated till pOwner.
        if (!ownerFound && it.Current() != pOwner)
            continue;

        if (it.Current() == pOwner)
            ownerFound = true;

        if (it.Current()->CheckIsStub_Worker(stubAddress))
        {
            // If you hit this assert, you can tell what 2 stub managers are conflicting by inspecting their vtable.
            CONSISTENCY_CHECK_MSGF((it.Current() == pOwner), ("Stub at 0x%p is owner by multiple managers (0x%p, 0x%p)",
                (void*) stubAddress, pOwner, it.Current()));
            count++;
        }
        else
        {
            _ASSERTE(it.Current() != pOwner);
        }
    }

    _ASSERTE(ownerFound);

    // We expect pOwner to be the only one to own this stub.
    return (count == 1);
}
#endif



//-----------------------------------------------------------
//-----------------------------------------------------------
BOOL StubManager::CheckIsStub_Worker(PCODE stubStartAddress)
{
    CONTRACTL
    {
        NOTHROW;
        CAN_TAKE_LOCK;     // CheckIsStub_Internal can enter SimpleRWLock
        GC_NOTRIGGER;
    }
    CONTRACTL_END;

    SUPPORTS_DAC;

    // @todo - consider having a single check for null right up front.
    // Though this may cover bugs where stub-managers don't handle bad addresses.
    // And someone could just as easily pass (0x01) as NULL.
    if (stubStartAddress == (PCODE)NULL)
    {
        return FALSE;
    }

    struct Param
    {
        BOOL fIsStub;
        StubManager *pThis;
        TADDR stubStartAddress;
    } param;
    param.fIsStub = FALSE;
    param.pThis = this;
    param.stubStartAddress = stubStartAddress;

    // This may be called from DAC, and DAC + non-DAC have very different
    // exception handling.
#ifdef DACCESS_COMPILE
    PAL_TRY(Param *, pParam, &param)
#else
    Param *pParam = &param;
    EX_TRY
#endif
    {
		SUPPORTS_DAC;

#ifndef DACCESS_COMPILE
        // Use CheckIsStub_Internal may AV. That's ok.
        AVInRuntimeImplOkayHolder AVOkay;
#endif

        // Make a Polymorphic call to derived stub manager.
        // Try to see if this address is for a stub. If the address is
        // completely bogus, then this might fault, so we protect it
        // with SEH.
        pParam->fIsStub = pParam->pThis->CheckIsStub_Internal(pParam->stubStartAddress);
    }
#ifdef DACCESS_COMPILE
    PAL_EXCEPT(EXCEPTION_EXECUTE_HANDLER)
#else
    EX_CATCH
#endif
    {
        LOG((LF_CORDB, LL_INFO10000, "SM::CISWorker: exception indicated addr is bad.\n"));

        param.fIsStub = FALSE;
    }
#ifdef DACCESS_COMPILE
    PAL_ENDTRY
#else
    EX_END_CATCH
#endif

    return param.fIsStub;
}

//-----------------------------------------------------------
// stubAddress may be an invalid address.
//-----------------------------------------------------------
PTR_StubManager StubManager::FindStubManager(PCODE stubAddress)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        CAN_TAKE_LOCK;             // courtesy StubManagerIterator
    }
    CONTRACTL_END;

    SUPPORTS_DAC;

    StubManagerIterator it;
    while (it.Next())
    {
        if (it.Current()->CheckIsStub_Worker(stubAddress))
        {
            _ASSERTE_IMPL(IsSingleOwner(stubAddress, it.Current()));

            LOG((LF_CORDB, LL_INFO10000, "SM::FSM: %p claims %p\n",
                it.Current(), stubAddress));
            return it.Current();
        }
    }

    LOG((LF_CORDB, LL_INFO10000, "SM::FSM: no stub manager claims %p\n",
        stubAddress));
    return NULL;
}

//-----------------------------------------------------------
// Given an address, figure out a TraceDestination describing where
// the instructions at that address will eventually transfer execution to.
//-----------------------------------------------------------
BOOL StubManager::TraceStub(PCODE stubStartAddress, TraceDestination *trace)
{
    WRAPPER_NO_CONTRACT;

    StubManagerIterator it;
    while (it.Next())
    {
        StubManager * pCurrent = it.Current();
        if (!pCurrent->CheckIsStub_Worker(stubStartAddress))
            continue;

        LOG((LF_CORDB, LL_INFO10000,
                "StubManager::TraceStub: '%s' (%p) claimed %p.\n", pCurrent->DbgGetName(), pCurrent, stubStartAddress));

        _ASSERTE_IMPL(IsSingleOwner(stubStartAddress, pCurrent));

        BOOL fValid = pCurrent->DoTraceStub(stubStartAddress, trace);
#ifdef _DEBUG
        if (IsStubLoggingEnabled())
        {
            DbgWriteLog("Doing TraceStub for %p, claimed by '%s' (%p)\n", stubStartAddress, pCurrent->DbgGetName(), pCurrent);
            if (fValid)
            {
                SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;
                FAULT_NOT_FATAL();
                SString buffer;
                DbgWriteLog("  td=%s\n", trace->DbgToString(buffer));
            }
            else
            {
                DbgWriteLog("  stubmanager returned false. Does not expect to call managed code\n");
            }
        } // logging
#endif
        return fValid;
    }

    if (ExecutionManager::IsManagedCode(stubStartAddress))
    {
        LOG((LF_CORDB, LL_INFO10000,
             "StubManager::TraceStub: addr %p is managed code\n",
             stubStartAddress));

        trace->InitForManaged(stubStartAddress);

#ifdef _DEBUG
        DbgWriteLog("Doing TraceStub for Address %p is jitted code claimed by codemanager\n", stubStartAddress);
#endif
        return TRUE;
    }

    LOG((LF_CORDB, LL_INFO10000,
         "StubManager::TraceStub: addr %p unknown. TRACE_OTHER...\n",
         stubStartAddress));

#ifdef _DEBUG
    DbgWriteLog("Doing TraceStub for Address %p is unknown!!!\n", stubStartAddress);
#endif

    trace->InitForOther(stubStartAddress);
    return FALSE;
}

//-----------------------------------------------------------
//-----------------------------------------------------------
BOOL StubManager::FollowTrace(TraceDestination *trace)
{
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_FORBID_FAULT;

    while (trace->GetTraceType() == TRACE_STUB)
    {
        LOG((LF_CORDB, LL_INFO10000,
             "StubManager::FollowTrace: TRACE_STUB for %p\n",
             trace->GetAddress()));

        if (!TraceStub(trace->GetAddress(), trace))
        {
            //
            // No stub manager claimed it - it must be an EE helper or something.
            //

            trace->InitForOther(trace->GetAddress());
        }
    }

    LOG_TRACE_DESTINATION(trace, (PCODE)NULL, "StubManager::FollowTrace");

    return trace->GetTraceType() != TRACE_OTHER;
}

#ifndef DACCESS_COMPILE

//-----------------------------------------------------------
//-----------------------------------------------------------
void StubManager::AddStubManager(StubManager *mgr)
{
    WRAPPER_NO_CONTRACT;
    CONSISTENCY_CHECK(CheckPointer(g_pFirstManager, NULL_OK));
    CONSISTENCY_CHECK(CheckPointer(mgr));

    GCX_COOP_NO_THREAD_BROKEN();

    CrstHolder ch(&s_StubManagerListCrst);

    if (g_pFirstManager == NULL)
    {
        g_pFirstManager = mgr;
    }
    else
    {
        mgr->m_pNextManager = g_pFirstManager;
        g_pFirstManager = mgr;
    }

    LOG((LF_CORDB, LL_EVERYTHING, "StubManager::AddStubManager - 0x%p (vptr %p)\n", mgr, (*(PVOID*)mgr)));
}

//-----------------------------------------------------------
// NOTE: The runtime MUST be suspended to use this in a
//       truly safe manner.
//-----------------------------------------------------------
void StubManager::UnlinkStubManager(StubManager *mgr)
{
    STATIC_CONTRACT_GC_NOTRIGGER;
    STATIC_CONTRACT_NOTHROW;
    STATIC_CONTRACT_CAN_TAKE_LOCK;
    CONSISTENCY_CHECK(CheckPointer(g_pFirstManager, NULL_OK));
    CONSISTENCY_CHECK(CheckPointer(mgr));

    CrstHolder ch(&s_StubManagerListCrst);

    StubManager **m = &g_pFirstManager;
    while (*m != NULL)
    {
        if (*m == mgr)
        {
            *m = (*m)->m_pNextManager;
            return;
        }
        m = &(*m)->m_pNextManager;
    }
}

#endif // #ifndef DACCESS_COMPILE

#ifdef DACCESS_COMPILE

//-----------------------------------------------------------
//-----------------------------------------------------------
void
StubManager::EnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    // Report the global list head.
    DacEnumMemoryRegion(DacGlobalValues()->StubManager__g_pFirstManager,
                        sizeof(TADDR));

    //
    // Report the list contents.
    //

    StubManagerIterator it;
    while (it.Next())
    {
        it.Current()->DoEnumMemoryRegions(flags);
    }
}

//-----------------------------------------------------------
//-----------------------------------------------------------
void
StubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p StubManager base\n", dac_cast<TADDR>(this)));
}

#endif // #ifdef DACCESS_COMPILE

//-----------------------------------------------------------
// Initialize the global stub manager service.
//-----------------------------------------------------------
void StubManager::InitializeStubManagers()
{
#if !defined(DACCESS_COMPILE)

#if defined(_DEBUG)
    s_DbgLogCrst.Init(CrstDebuggerHeapLock, (CrstFlags)(CRST_UNSAFE_ANYMODE | CRST_DEBUGGER_THREAD | CRST_TAKEN_DURING_SHUTDOWN));
#endif
    s_StubManagerListCrst.Init(CrstDebuggerHeapLock, (CrstFlags)(CRST_UNSAFE_ANYMODE | CRST_DEBUGGER_THREAD | CRST_TAKEN_DURING_SHUTDOWN));

#endif // !DACCESS_COMPILE
}

#ifdef _DEBUG

//-----------------------------------------------------------
// Should stub-manager logging be enabled?
//-----------------------------------------------------------
bool StubManager::IsStubLoggingEnabled()
{
    // Our current logging impl uses SString, which uses new(), which can't be called
    // on the helper thread. (B/c it may deadlock. See SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE)

    // We avoid this by just not logging when native-debugging.
    if (minipal_is_native_debugger_present())
    {
        return false;
    }

    return true;
}


//-----------------------------------------------------------
// Call to reset the log. This is used at the start of a new step-operation.
// pThread is the managed thread doing the stepping.
// It should either be the current thread or the helper thread.
//-----------------------------------------------------------
void StubManager::DbgBeginLog(TADDR addrCallInstruction, TADDR addrCallTarget)
{
#ifndef DACCESS_COMPILE
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;


    // We can't call new() if another thread holds the heap lock and is then suspended by
    // an interop-debugging. Since this is debug-only logging code, we'll just skip
    // it under those cases.
    if (!IsStubLoggingEnabled())
    {
        return;
    }
    // Now that we know we're not interop-debugging, we can safely call new.
    SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;
    FAULT_NOT_FATAL();

    {
        CrstHolder ch(&s_DbgLogCrst);
        EX_TRY
        {
            if (s_pDbgStubManagerLog == NULL)
            {
                s_pDbgStubManagerLog = new SString();
            }
            s_pDbgStubManagerLog->Clear();
        }
        EX_CATCH
        {
            DbgFinishLog();
        }
        EX_END_CATCH
    }

    DbgWriteLog("Beginning Step-in. IP after Call instruction is at 0x%p, call target is at 0x%p\n",
        addrCallInstruction, addrCallTarget);
#endif
}

//-----------------------------------------------------------
// Finish logging for this thread.
// pThread is the managed thread doing the stepping.
// It should either be the current thread or the helper thread.
//-----------------------------------------------------------
void StubManager::DbgFinishLog()
{
#ifndef DACCESS_COMPILE
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    CrstHolder ch(&s_DbgLogCrst);

    // Since this is just a tool for debugging, we don't care if we call new.
    SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;
    FAULT_NOT_FATAL();

    delete s_pDbgStubManagerLog;
    s_pDbgStubManagerLog = NULL;


#endif
}


//-----------------------------------------------------------
// Write an arbitrary string to the log.
//-----------------------------------------------------------
void StubManager::DbgWriteLog(const CHAR *format, ...)
{
#ifndef DACCESS_COMPILE
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;


    if (!IsStubLoggingEnabled())
    {
        return;
    }

    // Since this is just a tool for debugging, we don't care if we call new.
    SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;
    FAULT_NOT_FATAL();

    CrstHolder ch(&s_DbgLogCrst);

    if (s_pDbgStubManagerLog == NULL)
    {
        return;
    }

    // Suppress asserts about lossy encoding conversion in SString::Printf
    CHECK chk;
    BOOL fEntered = chk.EnterAssert();

    EX_TRY
    {
        va_list args;
        va_start(args, format);
        s_pDbgStubManagerLog->AppendVPrintf(format, args);
        va_end(args);
    }
    EX_CATCH
    {
    }
    EX_END_CATCH

    if (fEntered) chk.LeaveAssert();
#endif
}



//-----------------------------------------------------------
// Get the log as a string.
//-----------------------------------------------------------
void StubManager::DbgGetLog(SString * pStringOut)
{
#ifndef DACCESS_COMPILE
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;

        PRECONDITION(CheckPointer(pStringOut));
    }
    CONTRACTL_END;

    if (!IsStubLoggingEnabled())
    {
        return;
    }

    // Since this is just a tool for debugging, we don't care if we call new.
    SUPPRESS_ALLOCATION_ASSERTS_IN_THIS_SCOPE;
    FAULT_NOT_FATAL();

    CrstHolder ch(&s_DbgLogCrst);

    if (s_pDbgStubManagerLog == NULL)
    {
        return;
    }

    EX_TRY
    {
        pStringOut->Set(*s_pDbgStubManagerLog);
    }
    EX_CATCH
    {
    }
    EX_END_CATCH
#endif
}


#endif // _DEBUG

extern "C" void STDCALL ThePreStubPatchLabel(void);

//-----------------------------------------------------------
//-----------------------------------------------------------
BOOL ThePreStubManager::DoTraceStub(PCODE stubStartAddress, TraceDestination *trace)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;

        PRECONDITION(stubStartAddress != NULL);
        PRECONDITION(CheckPointer(trace));
    }
    CONTRACTL_END;

    //
    // We cannot tell where the stub will end up
    // until after the prestub worker has been run.
    //
#if defined(TARGET_ARM64) && defined(__APPLE__)
    // On ARM64 Mac, we cannot put a breakpoint inside of ThePreStubPatchLabel
    LOG((LF_CORDB, LL_INFO10000, "TPSM::DoTraceStub: Skipping on arm64-macOS\n"));
    return FALSE;
#else
    trace->InitForFramePush(GetEEFuncEntryPoint(ThePreStubPatchLabel));

    return TRUE;
#endif //defined(TARGET_ARM64) && defined(__APPLE__)
}

//-----------------------------------------------------------
BOOL ThePreStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    LIMITED_METHOD_DAC_CONTRACT;
    return stubStartAddress == GetPreStubEntryPoint();
}


// -------------------------------------------------------
// Stub manager functions & globals
// -------------------------------------------------------

SPTR_IMPL(PrecodeStubManager, PrecodeStubManager, g_pManager);

#ifndef DACCESS_COMPILE

/* static */
void PrecodeStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    g_pManager = new PrecodeStubManager();
    StubManager::AddStubManager(g_pManager);
}

#endif // #ifndef DACCESS_COMPILE

BOOL PrecodeStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    CONTRACTL
    {
        THROWS; // address may be bad, so we may AV.
        GC_NOTRIGGER;
        SUPPORTS_DAC;
    }
    CONTRACTL_END;

    auto stubKind = RangeSectionStubManager::GetStubKind(stubStartAddress);
    if (stubKind == STUB_CODE_BLOCK_FIXUPPRECODE)
    {
        return TRUE;
    }
    else if (stubKind == STUB_CODE_BLOCK_STUBPRECODE)
    {
        Precode* pPrecode = Precode::GetPrecodeFromEntryPoint(stubStartAddress);
        switch (pPrecode->GetType())
        {
            case PRECODE_STUB:
            case PRECODE_PINVOKE_IMPORT:
            case PRECODE_UMENTRY_THUNK:
#ifdef HAS_THISPTR_RETBUF_PRECODE
            case PRECODE_THISPTR_RETBUF:
#endif // HAS_THISPTR_RETBUF_PRECODE
                return TRUE;
            default:
                return FALSE;
        }
    }
    return FALSE;
}

BOOL PrecodeStubManager::DoTraceStub(PCODE stubStartAddress,
                                     TraceDestination *trace)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
        FORBID_FAULT;
    }
    CONTRACTL_END

    LOG((LF_CORDB, LL_EVERYTHING, "PrecodeStubManager::DoTraceStub called\n"));

    MethodDesc* pMD = NULL;

    {
        // When the target slot points to the fixup part of the fixup precode, we need to compensate
        // for that to get the actual stub address
        Precode* pPrecode = NULL;
        if (RangeSectionStubManager::GetStubKind(stubStartAddress) == STUB_CODE_BLOCK_FIXUPPRECODE)
        {
            pPrecode = Precode::GetPrecodeFromEntryPoint(stubStartAddress - FixupPrecode::FixupCodeOffset, TRUE /* speculative */);
            if (pPrecode != NULL && pPrecode->GetType() != PRECODE_FIXUP)
            {
                pPrecode = NULL;
            }
        }
        if (pPrecode == NULL)
        {
            pPrecode = Precode::GetPrecodeFromEntryPoint(stubStartAddress);
        }

        _ASSERTE(pPrecode != NULL);

        switch (pPrecode->GetType())
        {
        case PRECODE_UMENTRY_THUNK:
            LOG((LF_CORDB, LL_EVERYTHING, "PrecodeStubManager::DoTraceStub called on UMEntryThunk\n"));
            // We never trace through these stubs when stepping through managed code. The reason we handle this at all is so that
            // is so that IsTransitionStub can recognize UMEntryThunks.
            return FALSE;

        case PRECODE_STUB:
            break;

#ifdef HAS_PINVOKE_IMPORT_PRECODE
        case PRECODE_PINVOKE_IMPORT:
#ifndef DACCESS_COMPILE
#if defined(TARGET_ARM64) && defined(__APPLE__)
            // On ARM64 Mac, we cannot put a breakpoint inside of PInvokeImportThunk
            LOG((LF_CORDB, LL_INFO10000, "PSM::DoTraceStub: Skipping on arm64-macOS\n"));
            return FALSE;
#else
            trace->InitForUnmanaged(GetEEFuncEntryPoint(PInvokeImportThunk));
#endif //defined(TARGET_ARM64) && defined(__APPLE__)
#else
            trace->InitForOther((PCODE)NULL);
#endif
            LOG_TRACE_DESTINATION(trace, stubStartAddress, "PrecodeStubManager::DoTraceStub - PInvoke import");
            return TRUE;
#endif // HAS_PINVOKE_IMPORT_PRECODE

#ifdef HAS_FIXUP_PRECODE
        case PRECODE_FIXUP:
            break;
#endif // HAS_FIXUP_PRECODE

#ifdef HAS_THISPTR_RETBUF_PRECODE
        case PRECODE_THISPTR_RETBUF:
            break;
#endif // HAS_THISPTR_RETBUF_PRECODE

        default:
            _ASSERTE_IMPL(!"DoTraceStub: Unexpected precode type");
            break;
        }

        PCODE target = pPrecode->GetTarget();

        // check if the method has been jitted
        if (!pPrecode->IsPointingToPrestub(target))
        {
            trace->InitForStub(target);
            LOG_TRACE_DESTINATION(trace, stubStartAddress, "PrecodeStubManager::DoTraceStub - code");
            return TRUE;
        }

        pMD = pPrecode->GetMethodDesc();
    }

    _ASSERTE(pMD != NULL);

    // If the method is not IL, then we patch the prestub because no one will ever change the call here at the
    // MethodDesc. If, however, this is an IL method, then we are at risk to have another thread backpatch the call
    // here, so we'd miss if we patched the prestub. Therefore, we go right to the IL method and patch IL offset 0
    // by using TRACE_UNJITTED_METHOD.
    if (!pMD->IsIL() && !pMD->IsILStub())
    {
        trace->InitForStub(GetPreStubEntryPoint());
    }
    else
    {
        trace->InitForUnjittedMethod(pMD);
    }

    LOG_TRACE_DESTINATION(trace, stubStartAddress, "PrecodeStubManager::DoTraceStub - prestub");
    return TRUE;
}

#ifndef DACCESS_COMPILE
BOOL PrecodeStubManager::TraceManager(Thread *thread,
                            TraceDestination *trace,
                            T_CONTEXT *pContext,
                            BYTE **pRetAddr)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
        PRECONDITION(CheckPointer(thread, NULL_OK));
        PRECONDITION(CheckPointer(trace));
        PRECONDITION(CheckPointer(pContext));
        PRECONDITION(CheckPointer(pRetAddr));
    }
    CONTRACTL_END;

    _ASSERTE(!"Unexpected call to PrecodeStubManager::TraceManager");
    return FALSE;
}
#endif

// -------------------------------------------------------
// StubLinkStubManager
// -------------------------------------------------------

SPTR_IMPL(StubLinkStubManager, StubLinkStubManager, g_pManager);

#ifndef DACCESS_COMPILE

// static
void StubLinkStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    g_pManager = new StubLinkStubManager();
    StubManager::AddStubManager(g_pManager);
}

// static
BOOL StubLinkStubManager::TraceDelegateObject(BYTE* pbDel, TraceDestination *trace)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    BYTE **ppbDest;

    // If we got here, then we're here b/c we're at the start of a delegate stub
    // need to figure out the kind of delegates we are dealing with.
    BYTE *pbDelInvocationList = *(BYTE **)(pbDel + DelegateObject::GetOffsetOfInvocationList());

    LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: invocationList: %p\n", pbDelInvocationList));

    if (pbDelInvocationList == NULL)
    {
        // A null invocationList can be one of the following:
        //  - Instance closed, Instance open non-virt, Instance open virtual, Static closed, Static opened, Unmanaged FtnPtr
        //  - Instance open virtual is complex and we need to figure out what to do (TODO).
        // For the others the logic is the following:
        // if _methodPtrAux is 0 the target is in _methodPtr, otherwise the taret is _methodPtrAux

        ppbDest = (BYTE **)(pbDel + DelegateObject::GetOffsetOfMethodPtrAux());
        if (*ppbDest == NULL)
        {
            ppbDest = (BYTE **)(pbDel + DelegateObject::GetOffsetOfMethodPtr());

            if (*ppbDest == NULL)
            {
                // it's not looking good, bail out
                LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: can't trace into it\n"));
                return FALSE;
            }
        }

        LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: ppbDest: %p *ppbDest:%p\n", ppbDest, *ppbDest));

        BOOL res = StubManager::TraceStub((PCODE) (*ppbDest), trace);

        LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: res: %s, result type: %d\n", (res ? "true" : "false"), trace->GetTraceType()));

        return res;
    }

    // invocationList is not null, so it can be one of the following:
    // Multicast, Static closed (special sig), Secure

    // rule out the static with special sig
    BYTE *pbCount = *(BYTE **)(pbDel + DelegateObject::GetOffsetOfInvocationCount());
    if (pbCount == NULL)
    {
        // it's a static closed, the target lives in _methodAuxPtr
        ppbDest = (BYTE **)(pbDel + DelegateObject::GetOffsetOfMethodPtrAux());

        if (*ppbDest == NULL)
        {
            // it's not looking good, bail out
            LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: can't trace into it\n"));
            return FALSE;
        }

        LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: ppbDest: %p *ppbDest:%p\n", ppbDest, *ppbDest));

        BOOL res = StubManager::TraceStub((PCODE) (*ppbDest), trace);

        LOG((LF_CORDB,LL_INFO10000, "SLSM::TDO: res: %d, result type: %d\n", (res ? "true" : "false"), trace->GetTraceType()));

        return res;
    }

    MethodTable *pType = *(MethodTable**)pbDelInvocationList;
    if (pType->IsDelegate())
    {
        // this is a secure delegate. The target is hidden inside this field, so recurse.
        return TraceDelegateObject(pbDelInvocationList, trace);
    }

    // Otherwise, we're going for the first invoke of the multi case.
    // In order to go to the correct spot, we have just have to fish out
    // slot 0 of the invocation list, and figure out where that's going to,
    // then put a breakpoint there.
    pbDel = *(BYTE**)(((ArrayBase *)pbDelInvocationList)->GetDataPtr());
    return TraceDelegateObject(pbDel, trace);
}

#endif // DACCESS_COMPILE

BOOL StubLinkStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;
    return GetRangeList()->IsInRange(stubStartAddress) ? TRUE : FALSE;
}

BOOL StubLinkStubManager::DoTraceStub(PCODE stubStartAddress,
                                      TraceDestination *trace)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    LOG((LF_CORDB, LL_INFO10000,
         "StubLinkStubManager::DoTraceStub: stubStartAddress=%p\n",
         stubStartAddress));

    Stub *stub = Stub::RecoverStub(stubStartAddress);

    LOG((LF_CORDB, LL_INFO10000,
         "StubLinkStubManager::DoTraceStub: stub=%p\n", stub));

    TADDR pRealAddr = 0;
    if (stub->IsInstantiatingStub())
    {
        trace->InitForManagerPush(stubStartAddress, this);
        LOG_TRACE_DESTINATION(trace, stubStartAddress, "StubLinkStubManager(InstantiatingMethod)::DoTraceStub");
        return TRUE;
    }
    else if (stub->IsShuffleThunk())
    {
        trace->InitForManagerPush(stubStartAddress, this);
        LOG_TRACE_DESTINATION(trace, stubStartAddress, "StubLinkStubManager(ShuffleThunk)::DoTraceStub");
        return TRUE;
    }

    LOG((LF_CORDB, LL_INFO10000, "StubLinkStubManager::DoTraceStub: no known target\n"));
    return FALSE;
}

#ifndef DACCESS_COMPILE

static PCODE GetStubTarget(PTR_MethodDesc pTargetMD)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        MODE_COOPERATIVE;
        PRECONDITION(pTargetMD != NULL);
    }
    CONTRACTL_END;

    NativeCodeVersion targetCode;

#ifdef FEATURE_CODE_VERSIONING
    CodeVersionManager::LockHolder codeVersioningLockHolder;
    ILCodeVersion ilcode = pTargetMD->GetCodeVersionManager()->GetActiveILCodeVersion(pTargetMD);
    targetCode = ilcode.GetActiveNativeCodeVersion(pTargetMD);
#else
    targetCode = NativeCodeVersion(pTargetMD);
#endif

    if (targetCode.IsNull() || targetCode.GetNativeCode() == (PCODE)NULL)
        return (PCODE)NULL;

    return targetCode.GetNativeCode();
}

static BOOL TraceShuffleThunk(
    TraceDestination *trace,
    T_CONTEXT *pContext,
    BYTE **pRetAddr)
{
    CONTRACTL
    {
        MODE_COOPERATIVE;
    }
    CONTRACTL_END;

    *pRetAddr = (BYTE *)StubManagerHelpers::GetReturnAddress(pContext);

    DELEGATEREF orDelegate = (DELEGATEREF)ObjectToOBJECTREF(StubManagerHelpers::GetThisPtr(pContext));
    PCODE destAddr = orDelegate->GetMethodPtrAux();
    LOG((LF_CORDB,LL_INFO10000, "TraceShuffleThunk: ppbDest: %p\n", destAddr));

    BOOL res = StubManager::TraceStub(destAddr, trace);
    LOG((LF_CORDB,LL_INFO10000, "TraceShuffleThunk: res: %d, result type: %d\n", res, trace->GetTraceType()));

    return res;
}

BOOL StubLinkStubManager::TraceManager(Thread *thread,
                                       TraceDestination *trace,
                                       T_CONTEXT *pContext,
                                       BYTE **pRetAddr)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        THROWS;
        GC_TRIGGERS;
        MODE_ANY;
        INJECT_FAULT(return FALSE;);
    }
    CONTRACTL_END

    LPVOID pc = (LPVOID)GetIP(pContext);
    *pRetAddr = (BYTE *)StubManagerHelpers::GetReturnAddress(pContext);
    LOG((LF_CORDB,LL_INFO10000, "SLSM:TM %p, retAddr is %p\n", pc, (*pRetAddr)));

    Stub *stub = Stub::RecoverStub((PCODE)pc);
    if (stub->IsInstantiatingStub())
    {
        LOG((LF_CORDB,LL_INFO10000, "SLSM:TM Instantiating method stub\n"));
        PTR_MethodDesc pMD = stub->GetInstantiatedMethodDesc();
        _ASSERTE(pMD != NULL);

        PCODE target = GetStubTarget(pMD);
        if (target == (PCODE)NULL)
        {
            LOG((LF_CORDB,LL_INFO10000, "SLSM:TM Unable to determine stub target, pMD %p\n", pMD));
            trace->InitForUnjittedMethod(pMD);
            return TRUE;
        }

        trace->InitForManaged(target);
        return TRUE;
    }
    else if (stub->IsShuffleThunk())
    {
        LOG((LF_CORDB,LL_INFO10000, "SLSM:TM ShuffleThunk\n"));
        return TraceShuffleThunk(trace, pContext, pRetAddr);
    }

    // Runtime bug if we get here. Did we make a change in StubLinkStubManager::DoTraceStub() that
    // dispatched new stubs to TraceManager without writing the code to handle them?
    _ASSERTE(!"SLSM:TM wasn't expected to handle any other stub types");
    return FALSE;
}

#endif // #ifndef DACCESS_COMPILE

// -------------------------------------------------------
// JumpStub stubs
//
// Stub manager for jump stubs created by ExecutionManager::jumpStub()
// These are currently used only on the 64-bit targets IA64 and AMD64
//
// -------------------------------------------------------

SPTR_IMPL(JumpStubStubManager, JumpStubStubManager, g_pManager);

#ifndef DACCESS_COMPILE
/* static */
void JumpStubStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    g_pManager = new JumpStubStubManager();
    StubManager::AddStubManager(g_pManager);
}
#endif // #ifndef DACCESS_COMPILE

BOOL JumpStubStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;

    // Forwarded to from RangeSectionStubManager
    return FALSE;
}

BOOL JumpStubStubManager::DoTraceStub(PCODE stubStartAddress,
                                     TraceDestination *trace)
{
    LIMITED_METHOD_CONTRACT;

    PCODE jumpTarget = decodeBackToBackJump(stubStartAddress);
    trace->InitForStub(jumpTarget);

    LOG_TRACE_DESTINATION(trace, stubStartAddress, "JumpStubStubManager::DoTraceStub");

    return TRUE;
}

//
// Stub manager for code sections. It forwards the query to the more appropriate
// stub manager, or handles the query itself.
//

SPTR_IMPL(RangeSectionStubManager, RangeSectionStubManager, g_pManager);

#ifndef DACCESS_COMPILE
/* static */
void RangeSectionStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    g_pManager = new RangeSectionStubManager();
    StubManager::AddStubManager(g_pManager);
}
#endif // #ifndef DACCESS_COMPILE

BOOL RangeSectionStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;

    switch (GetStubKind(stubStartAddress))
    {
    case STUB_CODE_BLOCK_JUMPSTUB:
    case STUB_CODE_BLOCK_STUBLINK:
    case STUB_CODE_BLOCK_METHOD_CALL_THUNK:
#ifdef FEATURE_VIRTUAL_STUB_DISPATCH
    case STUB_CODE_BLOCK_VSD_DISPATCH_STUB:
    case STUB_CODE_BLOCK_VSD_RESOLVE_STUB:
    case STUB_CODE_BLOCK_VSD_LOOKUP_STUB:
    case STUB_CODE_BLOCK_VSD_VTABLE_STUB:
#endif // FEATURE_VIRTUAL_STUB_DISPATCH
        return TRUE;
    default:
        break;
    }

    return FALSE;
}

BOOL RangeSectionStubManager::DoTraceStub(PCODE stubStartAddress, TraceDestination *trace)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
        FORBID_FAULT;
    }
    CONTRACTL_END

    switch (GetStubKind(stubStartAddress))
    {
    case STUB_CODE_BLOCK_JUMPSTUB:
        return JumpStubStubManager::g_pManager->DoTraceStub(stubStartAddress, trace);

    case STUB_CODE_BLOCK_STUBLINK:
        return StubLinkStubManager::g_pManager->DoTraceStub(stubStartAddress, trace);

#ifdef FEATURE_VIRTUAL_STUB_DISPATCH
    case STUB_CODE_BLOCK_VSD_DISPATCH_STUB:
    case STUB_CODE_BLOCK_VSD_RESOLVE_STUB:
    case STUB_CODE_BLOCK_VSD_LOOKUP_STUB:
    case STUB_CODE_BLOCK_VSD_VTABLE_STUB:
        return VirtualCallStubManagerManager::GlobalManager()->DoTraceStub(stubStartAddress, trace);
#endif // FEATURE_VIRTUAL_STUB_DISPATCH

    case STUB_CODE_BLOCK_METHOD_CALL_THUNK:
#ifdef DACCESS_COMPILE
        DacNotImpl();
#else
        trace->InitForExternalMethodFixup();
#endif
        return TRUE;

    default:
        break;
    }

    return FALSE;
}

#ifdef DACCESS_COMPILE
LPCWSTR RangeSectionStubManager::GetStubManagerName(PCODE addr)
{
    WRAPPER_NO_CONTRACT;

    switch (GetStubKind(addr))
    {
    case STUB_CODE_BLOCK_JUMPSTUB:
        return W("JumpStub");

    case STUB_CODE_BLOCK_STUBLINK:
        return W("StubLinkStub");

    case STUB_CODE_BLOCK_METHOD_CALL_THUNK:
        return W("MethodCallThunk");

#ifdef FEATURE_VIRTUAL_STUB_DISPATCH
    case STUB_CODE_BLOCK_VSD_DISPATCH_STUB:
        return W("VSD_DispatchStub");

    case STUB_CODE_BLOCK_VSD_RESOLVE_STUB:
        return W("VSD_ResolveStub");

    case STUB_CODE_BLOCK_VSD_LOOKUP_STUB:
        return W("VSD_LookupStub");

    case STUB_CODE_BLOCK_VSD_VTABLE_STUB:
        return W("VSD_VTableStub");
#endif // FEATURE_VIRTUAL_STUB_DISPATCH

    default:
        break;
    }

    return W("UnknownRangeSectionStub");
}
#endif // DACCESS_COMPILE

StubCodeBlockKind
RangeSectionStubManager::GetStubKind(PCODE stubStartAddress)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    RangeSection * pRS = ExecutionManager::FindCodeRange(stubStartAddress, ExecutionManager::ScanReaderLock);
    if (pRS == NULL)
        return STUB_CODE_BLOCK_UNKNOWN;

    return pRS->_pjit->GetStubCodeBlockKind(pRS, stubStartAddress);
}

//
// This is the stub manager for IL stubs.
//

#ifndef DACCESS_COMPILE

/* static */
void ILStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    StubManager::AddStubManager(new ILStubManager());
}

#endif // #ifndef DACCESS_COMPILE

BOOL ILStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;

    MethodDesc *pMD = ExecutionManager::GetCodeMethodDesc(stubStartAddress);

    return (pMD != NULL) && pMD->IsILStub();
}

BOOL ILStubManager::DoTraceStub(PCODE stubStartAddress,
                                TraceDestination *trace)
{
    LIMITED_METHOD_CONTRACT;

    LOG((LF_CORDB, LL_EVERYTHING, "ILStubManager::DoTraceStub called\n"));

#ifndef DACCESS_COMPILE

    PCODE traceDestination = (PCODE)NULL;

    MethodDesc* pStubMD = ExecutionManager::GetCodeMethodDesc(stubStartAddress);
    if (pStubMD != NULL && pStubMD->AsDynamicMethodDesc()->IsMulticastStub())
    {
        trace->InitForMulticastDelegateHelper();
    }
    else
    {
        // This call is going out to unmanaged code, either through pinvoke or COM interop.
        trace->InitForManagerPush(stubStartAddress, this);
    }

    LOG_TRACE_DESTINATION(trace, traceDestination, "ILStubManager::DoTraceStub");

    return TRUE;

#else // !DACCESS_COMPILE
    trace->InitForOther((PCODE)NULL);
    return FALSE;

#endif // !DACCESS_COMPILE
}

#ifndef DACCESS_COMPILE
#ifdef FEATURE_COMINTEROP
static PCODE GetCOMTarget(Object *pThis, CLRToCOMCallInfo *pCLRToCOMCallInfo)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        MODE_COOPERATIVE;
    }
    CONTRACTL_END;

    // calculate the target interface pointer
    SafeComHolder<IUnknown> pUnk;

    OBJECTREF oref = ObjectToOBJECTREF(pThis);
    GCPROTECT_BEGIN(oref);
    pUnk = ComObject::GetComIPFromRCWThrowing(&oref, pCLRToCOMCallInfo->m_pInterfaceMT);
    GCPROTECT_END();

    LPVOID *lpVtbl = *(LPVOID **)(IUnknown *)pUnk;

    PCODE target = (PCODE)lpVtbl[pCLRToCOMCallInfo->m_cachedComSlot];
    return target;
}
#endif // FEATURE_COMINTEROP

BOOL ILStubManager::TraceManager(Thread *thread,
                                 TraceDestination *trace,
                                 T_CONTEXT *pContext,
                                 BYTE **pRetAddr)
{
    // See code:ILStubCache.CreateNewMethodDesc for the code that sets flags on stub MDs

    PCODE stubIP = GetIP(pContext);
    *pRetAddr = (BYTE *)StubManagerHelpers::GetReturnAddress(pContext);

    DynamicMethodDesc *pStubMD = NonVirtualEntry2MethodDesc(stubIP)->AsDynamicMethodDesc();
    TADDR arg = StubManagerHelpers::GetHiddenArg(pContext);
    Object * pThis = StubManagerHelpers::GetThisPtr(pContext);
    LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Enter: StubMD 0x%p, HiddenArg 0x%p, ThisPtr 0x%p\n",
        pStubMD, arg, pThis));

    // See code:ILStubCache.CreateNewMethodDesc for the code that sets flags on stub MDs
    PCODE target = (PCODE)NULL;
    if (pStubMD->IsMulticastStub())
    {
        _ASSERTE(!"We should never get here. Multicast Delegates should not invoke TraceManager.");
        return FALSE;
    }
    else if (pStubMD->IsReverseStub())
    {
        if (pStubMD->IsStatic())
        {
            // This is reverse P/Invoke stub, the argument is UMEntryThunkData
            UMEntryThunkData *pEntryThunk = (UMEntryThunkData*)arg;
            target = pEntryThunk->GetManagedTarget();
            LOG((LF_CORDB, LL_INFO10000, "ILSM::TraceManager: Reverse P/Invoke case 0x%p\n", target));
        }
        else
        {
            // This is COM-to-CLR stub, the argument is the target
            target = (PCODE)arg;
            LOG((LF_CORDB, LL_INFO10000, "ILSM::TraceManager: COM-to-CLR case 0x%p\n", target));
        }
        trace->InitForManaged(target);
    }
    else if (pStubMD->HasFlags(DynamicMethodDesc::FlagIsDelegate))
    {
        // This is forward delegate P/Invoke stub, the argument is undefined
        DelegateObject *pDel = (DelegateObject *)pThis;
        target = pDel->GetMethodPtrAux();

        LOG((LF_CORDB, LL_INFO10000, "ILSM::TraceManager: Forward delegate P/Invoke case 0x%p\n", target));
        trace->InitForUnmanaged(target);
    }
    else if (pStubMD->HasFlags(DynamicMethodDesc::FlagIsCALLI))
    {
        // This is unmanaged CALLI stub, the argument is the target
        target = (PCODE)arg;

        // The value is mangled on 64-bit
#ifdef TARGET_AMD64
        target = target >> 1; // call target is encoded as (addr << 1) | 1
#endif // TARGET_AMD64

        LOG((LF_CORDB, LL_INFO10000, "ILSM::TraceManager: Unmanaged CALLI case 0x%p\n", target));
        trace->InitForUnmanaged(target);
    }
    else if (pStubMD->IsStepThroughStub())
    {
        MethodDesc* pTargetMD = pStubMD->GetILStubResolver()->GetStubTargetMethodDesc();
        if (pTargetMD == NULL)
        {
            LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Stub has no target to step through to\n"));
            return FALSE;
        }

        LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Step through to target - 0x%p\n", pTargetMD));
        target = GetStubTarget(pTargetMD);
        if (target == (PCODE)NULL)
            return FALSE;

        trace->InitForManaged(target);
    }
    else if (pStubMD->HasMDContextArg())
    {
        LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Hidden argument is MethodDesc\n"));

        // This is either direct forward P/Invoke or a CLR-to-COM call, the argument is MD
        MethodDesc *pMD = (MethodDesc *)arg;
        if (pMD->IsPInvoke())
        {
            PInvokeMethodDesc* pNMD = reinterpret_cast<PInvokeMethodDesc*>(pMD);
            _ASSERTE_IMPL(!pNMD->PInvokeTargetIsImportThunk());
            target = (PCODE)pNMD->GetPInvokeTarget();
            LOG((LF_CORDB, LL_INFO10000, "ILSM::TraceManager: Forward P/Invoke case 0x%p\n", target));
            trace->InitForUnmanaged(target);
        }
#ifdef FEATURE_COMINTEROP
        else
        {
            LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Stub is CLR-to-COM\n"));
            _ASSERTE(pMD->IsCLRToCOMCall());
            CLRToCOMCallMethodDesc *pCMD = (CLRToCOMCallMethodDesc *)pMD;
            _ASSERTE(!pCMD->IsStatic() && !pCMD->IsCtor() && "Static methods and constructors are not supported for built-in classic COM");

            if (pThis != NULL)
            {
                target = GetCOMTarget(pThis, pCMD->m_pCLRToCOMCallInfo);
                LOG((LF_CORDB, LL_INFO10000, "ILSM::TraceManager: CLR-to-COM case 0x%p\n", target));
                trace->InitForUnmanaged(target);
            }
        }
#endif // FEATURE_COMINTEROP
    }
    else if (pStubMD->IsDelegateInvokeMethodStub())
    {
        if (pThis == NULL)
            return FALSE;

        LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Delegate Invoke Method\n"));
        return StubLinkStubManager::TraceDelegateObject((BYTE*)pThis, trace);
    }
    else if (pStubMD->IsDelegateShuffleThunk())
    {
        LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: Delegate Shuffle Thunk\n"));
        return TraceShuffleThunk(trace, pContext, pRetAddr);
    }
    else
    {
        LOG((LF_CORDB, LL_INFO1000, "ILSM::TraceManager: No known target, IL Stub is a leaf\n"));
        // There's no "target" so we have nowhere to tell the debugger to move the breakpoint.
        return FALSE;
    }

    return TRUE;
}
#endif //!DACCESS_COMPILE

// This is used to recognize GenericCLRToCOMCallStub, VarargPInvokeStub, and GenericPInvokeCalliHelper.

#ifndef DACCESS_COMPILE

/* static */
void InteropDispatchStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    StubManager::AddStubManager(new InteropDispatchStubManager());
}

#endif // #ifndef DACCESS_COMPILE

PCODE TheGenericCLRToCOMCallStub(); // clrtocomcall.cpp

#ifndef DACCESS_COMPILE
static BOOL IsVarargPInvokeStub(PCODE stubStartAddress)
{
    LIMITED_METHOD_CONTRACT;

    if (stubStartAddress == GetEEFuncEntryPoint(VarargPInvokeStub))
        return TRUE;

#if !defined(TARGET_X86) && !defined(TARGET_ARM64) && !defined(TARGET_LOONGARCH64) && !defined(TARGET_RISCV64)
    if (stubStartAddress == GetEEFuncEntryPoint(VarargPInvokeStub_RetBuffArg))
        return TRUE;
#endif

    return FALSE;
}
#endif // #ifndef DACCESS_COMPILE

BOOL InteropDispatchStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    WRAPPER_NO_CONTRACT;
    //@dbgtodo dharvey implement DAC support

#ifndef DACCESS_COMPILE
#ifdef FEATURE_COMINTEROP
    if (stubStartAddress == GetEEFuncEntryPoint(GenericCLRToCOMCallStub))
    {
        return true;
    }
#endif // FEATURE_COMINTEROP

    if (IsVarargPInvokeStub(stubStartAddress))
    {
        return true;
    }

    if (stubStartAddress == GetEEFuncEntryPoint(GenericPInvokeCalliHelper))
    {
        return true;
    }

#endif // !DACCESS_COMPILE
    return false;
}

BOOL InteropDispatchStubManager::DoTraceStub(PCODE stubStartAddress, TraceDestination *trace)
{
    LIMITED_METHOD_CONTRACT;

    LOG((LF_CORDB, LL_EVERYTHING, "InteropDispatchStubManager::DoTraceStub called\n"));

#ifndef DACCESS_COMPILE
     _ASSERTE(CheckIsStub_Internal(stubStartAddress));

    trace->InitForManagerPush(stubStartAddress, this);

    LOG_TRACE_DESTINATION(trace, stubStartAddress, "InteropDispatchStubManager::DoTraceStub");

    return TRUE;

#else // !DACCESS_COMPILE
    trace->InitForOther((PCODE)NULL);
    return FALSE;

#endif // !DACCESS_COMPILE
}

#ifndef DACCESS_COMPILE

BOOL InteropDispatchStubManager::TraceManager(Thread *thread,
                                              TraceDestination *trace,
                                              T_CONTEXT *pContext,
                                              BYTE **pRetAddr)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        MODE_COOPERATIVE;
    }
    CONTRACTL_END;

    *pRetAddr = (BYTE *)StubManagerHelpers::GetReturnAddress(pContext);

    TADDR arg = StubManagerHelpers::GetHiddenArg(pContext);

    // IL stub may not exist at this point so we init directly for the target (TODO?)

    if (IsVarargPInvokeStub(GetIP(pContext)))
    {
#if defined(TARGET_ARM64) && defined(__APPLE__)
        //On ARM64 Mac, we cannot put a breakpoint inside of VarargPInvokeStub
        LOG((LF_CORDB, LL_INFO10000, "IDSM::TraceManager: Skipping on arm64-macOS\n"));
        return FALSE;
#else
        PInvokeMethodDesc *pNMD = (PInvokeMethodDesc *)arg;
        _ASSERTE(pNMD->IsPInvoke());
        PCODE target = (PCODE)pNMD->GetPInvokeTarget();

        LOG((LF_CORDB, LL_INFO10000, "IDSM::TraceManager: Vararg P/Invoke case %p\n", target));
        trace->InitForUnmanaged(target);
#endif //defined(TARGET_ARM64) && defined(__APPLE__)
    }
    else if (GetIP(pContext) == GetEEFuncEntryPoint(GenericPInvokeCalliHelper))
    {
#if defined(TARGET_ARM64) && defined(__APPLE__)
        //On ARM64 Mac, we cannot put a breakpoint inside of GenericPInvokeCalliHelper
        LOG((LF_CORDB, LL_INFO10000, "IDSM::TraceManager: Skipping on arm64-macOS\n"));
        return FALSE;
#else
        PCODE target = (PCODE)arg;
        LOG((LF_CORDB, LL_INFO10000, "IDSM::TraceManager: Unmanaged CALLI case %p\n", target));
        trace->InitForUnmanaged(target);
#endif //defined(TARGET_ARM64) && defined(__APPLE__)
    }
#ifdef FEATURE_COMINTEROP
    else
    {
        CLRToCOMCallMethodDesc *pCMD = (CLRToCOMCallMethodDesc *)arg;
        _ASSERTE(pCMD->IsCLRToCOMCall());

        Object * pThis = StubManagerHelpers::GetThisPtr(pContext);

        {
            if (!pCMD->m_pCLRToCOMCallInfo->m_pInterfaceMT->IsComEventItfType() && (pCMD->m_pCLRToCOMCallInfo->m_pILStub != NULL))
            {
                // Early-bound CLR->COM call - continue in the IL stub
                trace->InitForStub(pCMD->m_pCLRToCOMCallInfo->m_pILStub);
            }
            else
            {
                // Late-bound CLR->COM call - continue in target's IDispatch::Invoke
                OBJECTREF oref = ObjectToOBJECTREF(pThis);
                GCPROTECT_BEGIN(oref);

                MethodTable *pItfMT = pCMD->m_pCLRToCOMCallInfo->m_pInterfaceMT;
                _ASSERTE(pItfMT->GetComInterfaceType() == ifDispatch);

                SafeComHolder<IUnknown> pUnk = ComObject::GetComIPFromRCWThrowing(&oref, pItfMT);
                LPVOID *lpVtbl = *(LPVOID **)(IUnknown *)pUnk;

                PCODE target = (PCODE)lpVtbl[6]; // DISPATCH_INVOKE_SLOT;
                LOG((LF_CORDB, LL_INFO10000, "IDSM::TraceManager: CLR-to-COM late-bound case 0x%p\n", target));
                trace->InitForUnmanaged(target);

                GCPROTECT_END();
            }
        }
    }
#endif // FEATURE_COMINTEROP

    return TRUE;
}
#endif //!DACCESS_COMPILE

#if defined(TARGET_X86) && !defined(UNIX_X86_ABI)

#if !defined(DACCESS_COMPILE)

// static
void TailCallStubManager::Init()
{
    CONTRACTL
    {
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END

    StubManager::AddStubManager(new TailCallStubManager());
}

bool TailCallStubManager::IsTailCallJitHelper(PCODE code)
{
    LIMITED_METHOD_CONTRACT;

    return code == GetEEFuncEntryPoint(JIT_TailCall);
}

#endif // !DACCESS_COMPILED

BOOL TailCallStubManager::CheckIsStub_Internal(PCODE stubStartAddress)
{
    LIMITED_METHOD_DAC_CONTRACT;

    bool fIsStub = false;

#if !defined(DACCESS_COMPILE)
    fIsStub = IsTailCallJitHelper(stubStartAddress);
#endif // !DACCESS_COMPILE

    return fIsStub;
}

#if !defined(DACCESS_COMPILE)

EXTERN_C void STDCALL JIT_TailCallLeave();
EXTERN_C void STDCALL JIT_TailCallVSDLeave();

BOOL TailCallStubManager::TraceManager(Thread * pThread,
                                       TraceDestination * pTrace,
                                       T_CONTEXT * pContext,
                                       BYTE ** ppRetAddr)
{
    WRAPPER_NO_CONTRACT;
    TADDR esp = GetSP(pContext);
    TADDR ebp = GetFP(pContext);

    // Check if we are stopped at the beginning of JIT_TailCall().
    if (GetIP(pContext) == GetEEFuncEntryPoint(JIT_TailCall))
    {
        // There are two cases in JIT_TailCall().  The first one is a normal tail call.
        // The second one is a tail call to a virtual method.
        *ppRetAddr = *(reinterpret_cast<BYTE **>(ebp + sizeof(SIZE_T)));

        // Check whether this is a VSD tail call.
        SIZE_T flags = *(reinterpret_cast<SIZE_T *>(esp + JIT_TailCall_StackOffsetToFlags));
        if (flags & 0x2)
        {
            // This is a VSD tail call.
            pTrace->InitForManagerPush(GetEEFuncEntryPoint(JIT_TailCallVSDLeave), this);
            return TRUE;
        }
        else
        {
            // This is not a VSD tail call.
            pTrace->InitForManagerPush(GetEEFuncEntryPoint(JIT_TailCallLeave), this);
            return TRUE;
        }
    }
    else
    {
        if (GetIP(pContext) == GetEEFuncEntryPoint(JIT_TailCallLeave))
        {
            // This is the simple case.  The tail call goes directly to the target.  There won't be an
            // explicit frame on the stack.  We should be right at the return instruction which branches to
            // the call target.  The return address is stored in the second leafmost stack slot.
            *ppRetAddr = *(reinterpret_cast<BYTE **>(esp + sizeof(SIZE_T)));
        }
        else
        {
            _ASSERTE(GetIP(pContext) == GetEEFuncEntryPoint(JIT_TailCallVSDLeave));

            // This is the VSD case.  The tail call goes through a assembly helper function which sets up
            // and tears down an explicit frame.  In this case, the return address is at the same place
            // as on entry to JIT_TailCall().
            *ppRetAddr = *(reinterpret_cast<BYTE **>(ebp + sizeof(SIZE_T)));
        }

        // In both cases, the target address is stored in the leafmost stack slot.
        pTrace->InitForStub((PCODE)*reinterpret_cast<SIZE_T *>(esp));
        return TRUE;
    }
}

#endif // !DACCESS_COMPILE

BOOL TailCallStubManager::DoTraceStub(PCODE stubStartAddress, TraceDestination *trace)
{
    WRAPPER_NO_CONTRACT;

    LOG((LF_CORDB, LL_EVERYTHING, "TailCallStubManager::DoTraceStub called\n"));

    BOOL fResult = FALSE;

    // Make sure we are stopped at the beginning of JIT_TailCall().
    _ASSERTE(CheckIsStub_Internal(stubStartAddress));
    trace->InitForManagerPush(stubStartAddress, this);
    fResult = TRUE;

    LOG_TRACE_DESTINATION(trace, stubStartAddress, "TailCallStubManager::DoTraceStub");
    return fResult;
}

#endif // TARGET_X86 && !UNIX_X86_ABI


#ifdef DACCESS_COMPILE

void
PrecodeStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p PrecodeStubManager\n", dac_cast<TADDR>(this)));
}

void
StubLinkStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p StubLinkStubManager\n", dac_cast<TADDR>(this)));
    GetRangeList()->EnumMemoryRegions(flags);
}

void
JumpStubStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p JumpStubStubManager\n", dac_cast<TADDR>(this)));
}

void
RangeSectionStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p RangeSectionStubManager\n", dac_cast<TADDR>(this)));
}

void
ILStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p ILStubManager\n", dac_cast<TADDR>(this)));
}

void
InteropDispatchStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p InteropDispatchStubManager\n", dac_cast<TADDR>(this)));
}

void
VirtualCallStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p VirtualCallStubManager\n", dac_cast<TADDR>(this)));
#ifdef FEATURE_VIRTUAL_STUB_DISPATCH
    GetCacheEntryRangeList()->EnumMemoryRegions(flags);
#endif // FEATURE_VIRTUAL_STUB_DISPATCH
}

#if defined(TARGET_X86) && !defined(UNIX_X86_ABI)
void TailCallStubManager::DoEnumMemoryRegions(CLRDataEnumMemoryFlags flags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;
    DAC_ENUM_VTHIS();
    EMEM_OUT(("MEM: %p TailCallStubManager\n", dac_cast<TADDR>(this)));
}
#endif

#endif // #ifdef DACCESS_COMPILE
