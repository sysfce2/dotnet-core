// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// ===========================================================================
// File: CEELOAD.INL
//

//
// CEELOAD.INL has inline methods from CEELOAD.H.
// ===========================================================================

#ifndef CEELOAD_INL_
#define CEELOAD_INL_

template<typename TYPE>
inline
TYPE LookupMap<TYPE>::GetValueAt(PTR_TADDR pValue, TADDR* pFlags, TADDR supportedFlags)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;
#ifndef DACCESS_COMPILE
    TYPE value = dac_cast<TYPE>(VolatileLoadWithoutBarrier(pValue)); // LookupMap's hold pointers, so we can use a data dependency instead of an explicit barrier here.
#else
    TYPE value = dac_cast<TYPE>(*pValue);
#endif

    if (pFlags)
        *pFlags = dac_cast<TADDR>(value) & supportedFlags;

    return (TYPE)(dac_cast<TADDR>(value) & ~supportedFlags);
}

#ifndef DACCESS_COMPILE

template<typename TYPE>
inline
void LookupMap<TYPE>::SetValueAt(PTR_TADDR pValue, TYPE value, TADDR flags)
{
    WRAPPER_NO_CONTRACT;

    value = dac_cast<TYPE>((dac_cast<TADDR>(value) | flags));

    VolatileStore(pValue, dac_cast<TADDR>(value));
}

//
// Specialization of Get/SetValueAt methods to support maps of pointer-sized value types
//
template<>
inline
SIZE_T LookupMap<SIZE_T>::GetValueAt(PTR_TADDR pValue, TADDR* pFlags, TADDR supportedFlags)
{
    WRAPPER_NO_CONTRACT;

    TADDR value = VolatileLoadWithoutBarrier(pValue); // LookupMap's hold pointers, so we can use a data dependency instead of an explicit barrier here.

    if (pFlags)
        *pFlags = value & supportedFlags;

    return (value & ~supportedFlags);
}

template<>
inline
void LookupMap<SIZE_T>::SetValueAt(PTR_TADDR pValue, SIZE_T value, TADDR flags)
{
    WRAPPER_NO_CONTRACT;
    VolatileStore(pValue, dac_cast<TADDR>(value | flags));
}
#endif // DACCESS_COMPILE

// Retrieve the value associated with a rid
template<typename TYPE>
TYPE LookupMap<TYPE>::GetElement(DWORD rid, TADDR* pFlags)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;

    PTR_TADDR pElement = GetElementPtr(rid);
    return (pElement != NULL) ? GetValueAt(pElement, pFlags, supportedFlags) : (TYPE)(TADDR)0;
}

// Stores an association in a map that has been previously grown to
// the required size. Will never throw or fail.
template<typename TYPE>
void LookupMap<TYPE>::SetElement(DWORD rid, TYPE value, TADDR flags)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;
    BOOL fSuccess;
    fSuccess = TrySetElement(rid, value, flags);
    _ASSERTE(fSuccess);
}


#ifndef DACCESS_COMPILE

// Try to store an association in a map. Will never throw or fail.
template<typename TYPE>
BOOL LookupMap<TYPE>::TrySetElement(DWORD rid, TYPE value, TADDR flags)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    PTR_TADDR pElement = GetElementPtr(rid);
    if (pElement == NULL)
        return FALSE;
#ifdef _DEBUG
    // Once set, the values in LookupMap should be immutable.
    TADDR oldFlags;
    TYPE oldValue = GetValueAt(pElement, &oldFlags, supportedFlags);
    _ASSERTE(oldValue == NULL || (oldValue == value && oldFlags == flags));
#endif
    // Avoid unnecessary writes - do not overwrite existing value
    if (*pElement == 0)
    {
        SetValueAt(pElement, value, flags);
    }
    return TRUE;
}

// Stores an association in a map. Grows the map as necessary.
template<typename TYPE>
void LookupMap<TYPE>::AddElement(ModuleBase * pModule, DWORD rid, TYPE value, TADDR flags)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        PRECONDITION(CheckPointer(pModule));
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
        INJECT_FAULT(ThrowOutOfMemory(););
    }
    CONTRACTL_END;

    PTR_TADDR pElement = GetElementPtr(rid);
    if (pElement == NULL)
        pElement = GrowMap(pModule, rid);
#ifdef _DEBUG
    // Once set, the values in LookupMap should be immutable.
    TADDR oldFlags;
    TYPE oldValue = GetValueAt(pElement, &oldFlags, supportedFlags);
    _ASSERTE(oldValue == (TYPE)NULL || (oldValue == value && oldFlags == flags));
#endif
    // Avoid unnecessary writes - do not overwrite existing value
    if (*pElement == 0)
    {
        SetValueAt(pElement, value, flags);
    }
}


// Ensures that the map has space for this element
template<typename TYPE>
inline
void LookupMap<TYPE>::EnsureElementCanBeStored(Module * pModule, DWORD rid)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        PRECONDITION(CheckPointer(pModule));
        THROWS;
        GC_NOTRIGGER;
        MODE_ANY;
        INJECT_FAULT(ThrowOutOfMemory(););
    }
    CONTRACTL_END;

    PTR_TADDR pElement = GetElementPtr(rid);
    if (pElement == NULL)
        GrowMap(pModule, rid);
}

#endif // DACCESS_COMPILE

// Find the given value in the table and return its RID
template<typename TYPE>
DWORD LookupMap<TYPE>::Find(TYPE value, TADDR* pFlags)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
    }
    CONTRACTL_END;

    Iterator it(this);

    DWORD rid = 0;
    while (it.Next())
    {
        TADDR flags;
        if (it.GetElementAndFlags(&flags) == value && (!pFlags || *pFlags == flags))
            return rid;
        rid++;
    }

    return 0;
}

template<typename TYPE>
inline
LookupMap<TYPE>::Iterator::Iterator(LookupMap* map)
{
    LIMITED_METHOD_DAC_CONTRACT;

    m_map = map;
    m_index = (DWORD) -1;
}

template<typename TYPE>
inline BOOL
LookupMap<TYPE>::Iterator::Next()
{
    LIMITED_METHOD_DAC_CONTRACT;

    if (!m_map || !m_map->pTable)
    {
        return FALSE;
    }

    m_index++;
    if (m_index == m_map->dwCount)
    {
        m_map = dac_cast<DPTR(LookupMap)>(m_map->pNext);
        if (!m_map || !m_map->pTable)
        {
            return FALSE;
        }
        m_index = 0;
    }

    return TRUE;
}

template<typename TYPE>
inline TYPE
LookupMap<TYPE>::Iterator::GetElement(TADDR* pFlags)
{
    SUPPORTS_DAC;
    WRAPPER_NO_CONTRACT;

    return GetValueAt(m_map->GetIndexPtr(m_index), pFlags, m_map->supportedFlags);
}

inline PTR_Assembly Module::GetAssembly() const
{
    LIMITED_METHOD_CONTRACT;
    SUPPORTS_DAC;

    return m_pAssembly;
}

inline PTR_MethodDesc Module::LookupMethodDef(mdMethodDef token)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
        SUPPORTS_DAC;
    }
    CONTRACTL_END

    _ASSERTE(TypeFromToken(token) == mdtMethodDef);
    return m_MethodDefToDescMap.GetElement(RidFromToken(token));
}

inline PTR_ILCodeVersioningState Module::LookupILCodeVersioningState(mdMethodDef token)
{
    CONTRACTL
    {
        NOTHROW;
        GC_NOTRIGGER;
        MODE_ANY;
        SUPPORTS_DAC;
    }
    CONTRACTL_END

    _ASSERTE(TypeFromToken(token) == mdtMethodDef);
    return m_ILCodeVersioningStateMap.GetElement(RidFromToken(token));
}

inline MethodDesc *Module::LookupMemberRefAsMethod(mdMemberRef token)
{
    LIMITED_METHOD_DAC_CONTRACT;

    _ASSERTE(TypeFromToken(token) == mdtMemberRef);
    TADDR flags = FALSE;
    TADDR pMemberRef = m_MemberRefMap.GetElementAndFlags(RidFromToken(token), &flags);
    return (flags & IS_FIELD_MEMBER_REF) ? NULL : dac_cast<PTR_MethodDesc>(pMemberRef);
}

inline Assembly *ModuleBase::LookupAssemblyRef(mdAssemblyRef token)
{
    WRAPPER_NO_CONTRACT;
    SUPPORTS_DAC;

    _ASSERTE(TypeFromToken(token) == mdtAssemblyRef);
    PTR_Module module= m_ManifestModuleReferencesMap.GetElement(RidFromToken(token));
    return module?module->GetAssembly():NULL;
}

#ifndef DACCESS_COMPILE
inline void Module::ForceStoreAssemblyRef(mdAssemblyRef token, Assembly *value)
{
    WRAPPER_NO_CONTRACT; // THROWS/GC_NOTRIGGER/INJECT_FAULT()/MODE_ANY
    _ASSERTE(value->GetModule());
    _ASSERTE(TypeFromToken(token) == mdtAssemblyRef);

    m_ManifestModuleReferencesMap.AddElement(this, RidFromToken(token), value->GetModule());
}

inline void Module::StoreAssemblyRef(mdAssemblyRef token, Assembly *value)
{
    WRAPPER_NO_CONTRACT;
    _ASSERTE(value->GetModule());
    _ASSERTE(TypeFromToken(token) == mdtAssemblyRef);
    m_ManifestModuleReferencesMap.TrySetElement(RidFromToken(token), value->GetModule());
}

inline mdAssemblyRef Module::FindAssemblyRef(Assembly *targetAssembly)
{
    WRAPPER_NO_CONTRACT;

    return m_ManifestModuleReferencesMap.Find(targetAssembly->GetModule()) | mdtAssemblyRef;
}

#endif //DACCESS_COMPILE

#include "nibblestream.h"

FORCEINLINE BOOL Module::FixupDelayList(TADDR pFixupList, BOOL mayUsePrecompiledPInvokeMethods)
{
    WRAPPER_NO_CONTRACT;

    COUNT_T nImportSections;
    PTR_READYTORUN_IMPORT_SECTION pImportSections = GetImportSections(&nImportSections);

    return FixupDelayListAux(pFixupList, this, &Module::FixupNativeEntry, pImportSections, nImportSections, GetReadyToRunImage(), mayUsePrecompiledPInvokeMethods);
}

template<typename Ptr, typename FixupNativeEntryCallback>
BOOL Module::FixupDelayListAux(TADDR pFixupList,
                               Ptr pThis, FixupNativeEntryCallback pfnCB,
                               PTR_READYTORUN_IMPORT_SECTION pImportSections, COUNT_T nImportSections,
                               PEDecoder * pNativeImage, BOOL mayUsePrecompiledPInvokeMethods)
{
    CONTRACTL
    {
        INSTANCE_CHECK;
        THROWS;
        GC_TRIGGERS;
        MODE_ANY;
        PRECONDITION(pFixupList != NULL);
   }
    CONTRACTL_END;

    // Fixup Encoding:
    // ==============
    //
    // The fixup list is sorted in tables. Within each table, the fixups are a
    // sorted list of INDEXes. The first INDEX in each table is encoded entirely,
    // but the remaining INDEXes store only the delta increment from the previous INDEX.
    // The encoding/compression is done by ZapperModule::CompressFixupList().
    //
    //-------------------------------------------------------------------------
    // Here is the detailed description :
    //
    // The first entry stores the m_pFixupBlob table index.
    //
    // The next entry stores the INDEX into the particular table.
    // An "entry" can be one or more nibbles. 3 bits of a nibble are used
    // to store the value, and the top bit indicates if  the following nibble
    // contains rest of the value. If the top bit is not set, then this
    // nibble is the last part of the value.
    //
    // If the next entry is non-0, it is another (delta-encoded relative to the
    // previous INDEX) INDEX belonging  to the same table. If the next entry is 0,
    // it indicates that all INDEXes in this table are done.
    //
    // When the fixups for the previous table is done, there is entry to
    // indicate the next table (delta-encoded relative to the previous table).
    // If the entry is 0, then it is the end of the entire fixup list.
    //
    //-------------------------------------------------------------------------
    // This is what the fixup list looks like:
    //
    // CorCompileTokenTable index
    //     absolute INDEX
    //     INDEX delta
    //     ...
    //     INDEX delta
    //     0
    // CorCompileTokenTable index delta
    //     absolute INDEX
    //     INDEX delta
    //     ...
    //     INDEX delta
    //     0
    // CorCompileTokenTable index delta
    //     absolute INDEX
    //     INDEX delta
    //     ...
    //     INDEX delta
    //     0
    // 0
    //
    //

    NibbleReader reader(PTR_BYTE(pFixupList), (SIZE_T)-1);

    //
    // The fixups are sorted by the sections they point to.
    // Walk the set of fixups in every sections
    //

    DWORD curTableIndex = reader.ReadEncodedU32();

    while (TRUE)
    {
        // Get the correct section to work with. This is stored in the first two nibbles (first byte)

        _ASSERTE(curTableIndex < nImportSections);
        PTR_READYTORUN_IMPORT_SECTION pImportSection = pImportSections + curTableIndex;

        COUNT_T cbData;
        TADDR pData = pNativeImage->GetDirectoryData(&pImportSection->Section, &cbData);

        // Now iterate thru the fixup entries
        SIZE_T fixupIndex = reader.ReadEncodedU32(); // Accumulate the real rva from the delta encoded rva

        while (TRUE)
        {
            CONSISTENCY_CHECK(fixupIndex * sizeof(TADDR) < cbData);

            if (!(pThis->*pfnCB)(pImportSection, fixupIndex, dac_cast<PTR_SIZE_T>(pData + fixupIndex * sizeof(TADDR)), mayUsePrecompiledPInvokeMethods))
                return FALSE;

            int delta = reader.ReadEncodedU32();

            // Delta of 0 means end of entries in this table
            if (delta == 0)
                break;

            fixupIndex += delta;
        }

        unsigned tableIndex = reader.ReadEncodedU32();

        if (tableIndex == 0)
            break;

        curTableIndex = curTableIndex + tableIndex;

    } // Done with all entries in this table

    return TRUE;
}

#ifdef FEATURE_CODE_VERSIONING
inline CodeVersionManager * Module::GetCodeVersionManager()
{
    LIMITED_METHOD_CONTRACT;
    return AppDomain::GetCurrentDomain()->GetCodeVersionManager();
}
#endif // FEATURE_CODE_VERSIONING

#endif  // CEELOAD_INL_
