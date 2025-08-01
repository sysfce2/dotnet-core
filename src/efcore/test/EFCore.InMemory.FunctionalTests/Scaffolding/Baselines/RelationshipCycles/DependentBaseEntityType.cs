// <auto-generated />
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.InMemory.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage.Json;

#pragma warning disable 219, 612, 618
#nullable disable

namespace TestNamespace
{
    [EntityFrameworkInternal]
    public partial class DependentBaseEntityType
    {
        public static RuntimeEntityType Create(RuntimeModel model, RuntimeEntityType baseEntityType = null)
        {
            var runtimeEntityType = model.AddEntityType(
                "Microsoft.EntityFrameworkCore.Scaffolding.CompiledModelTestBase+DependentBase<long?>",
                typeof(CompiledModelTestBase.DependentBase<long?>),
                baseEntityType,
                propertyCount: 2,
                navigationCount: 1,
                foreignKeyCount: 1,
                keyCount: 2);

            var id = runtimeEntityType.AddProperty(
                "Id",
                typeof(long?),
                propertyInfo: typeof(CompiledModelTestBase.DependentBase<long?>).GetProperty("Id", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                fieldInfo: typeof(CompiledModelTestBase.DependentBase<long?>).GetField("<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                valueGenerated: ValueGenerated.OnAdd,
                afterSaveBehavior: PropertySaveBehavior.Throw);
            id.SetGetter(
                long? (CompiledModelTestBase.DependentBase<long?> instance) => DependentBaseUnsafeAccessors<long?>.Id(instance),
                bool (CompiledModelTestBase.DependentBase<long?> instance) => !(DependentBaseUnsafeAccessors<long?>.Id(instance).HasValue));
            id.SetSetter(
                CompiledModelTestBase.DependentBase<long?> (CompiledModelTestBase.DependentBase<long?> instance, long? value) =>
                {
                    DependentBaseUnsafeAccessors<long?>.Id(instance) = value;
                    return instance;
                });
            id.SetMaterializationSetter(
                CompiledModelTestBase.DependentBase<long?> (CompiledModelTestBase.DependentBase<long?> instance, long? value) =>
                {
                    DependentBaseUnsafeAccessors<long?>.Id(instance) = value;
                    return instance;
                });
            id.SetAccessors(
                long? (IInternalEntry entry) => (entry.FlaggedAsStoreGenerated(0) ? entry.ReadStoreGeneratedValue<long?>(0) : (entry.FlaggedAsTemporary(0) && !(DependentBaseUnsafeAccessors<long?>.Id(((CompiledModelTestBase.DependentBase<long?>)(entry.Entity))).HasValue) ? entry.ReadTemporaryValue<long?>(0) : DependentBaseUnsafeAccessors<long?>.Id(((CompiledModelTestBase.DependentBase<long?>)(entry.Entity))))),
                long? (IInternalEntry entry) => DependentBaseUnsafeAccessors<long?>.Id(((CompiledModelTestBase.DependentBase<long?>)(entry.Entity))),
                long? (IInternalEntry entry) => entry.ReadOriginalValue<long?>(id, 0),
                long? (IInternalEntry entry) => ((InternalEntityEntry)(entry)).ReadRelationshipSnapshotValue<long?>(id, 0));
            id.SetPropertyIndexes(
                index: 0,
                originalValueIndex: 0,
                shadowIndex: -1,
                relationshipIndex: 0,
                storeGenerationIndex: 0);
            id.TypeMapping = InMemoryTypeMapping.Default.Clone(
                comparer: new ValueComparer<long>(
                    bool (long v1, long v2) => v1 == v2,
                    int (long v) => ((object)v).GetHashCode(),
                    long (long v) => v),
                keyComparer: new ValueComparer<long>(
                    bool (long v1, long v2) => v1 == v2,
                    int (long v) => ((object)v).GetHashCode(),
                    long (long v) => v),
                providerValueComparer: new ValueComparer<long>(
                    bool (long v1, long v2) => v1 == v2,
                    int (long v) => ((object)v).GetHashCode(),
                    long (long v) => v),
                clrType: typeof(long),
                jsonValueReaderWriter: JsonInt64ReaderWriter.Instance);
            id.SetCurrentValueComparer(new EntryCurrentValueComparer<long?>(id));
            id.SetComparer(new NullableValueComparer<long>(id.TypeMapping.Comparer));
            id.SetKeyComparer(new NullableValueComparer<long>(id.TypeMapping.KeyComparer));

            var principalId = runtimeEntityType.AddProperty(
                "PrincipalId",
                typeof(long),
                afterSaveBehavior: PropertySaveBehavior.Throw,
                sentinel: 0L);
            principalId.SetAccessors(
                long (IInternalEntry entry) => (entry.FlaggedAsStoreGenerated(1) ? entry.ReadStoreGeneratedValue<long>(1) : (entry.FlaggedAsTemporary(1) && entry.ReadShadowValue<long>(0) == 0L ? entry.ReadTemporaryValue<long>(1) : entry.ReadShadowValue<long>(0))),
                long (IInternalEntry entry) => entry.ReadShadowValue<long>(0),
                long (IInternalEntry entry) => entry.ReadOriginalValue<long>(principalId, 1),
                long (IInternalEntry entry) => ((InternalEntityEntry)(entry)).ReadRelationshipSnapshotValue<long>(principalId, 1));
            principalId.SetPropertyIndexes(
                index: 1,
                originalValueIndex: 1,
                shadowIndex: 0,
                relationshipIndex: 1,
                storeGenerationIndex: 1);
            principalId.TypeMapping = InMemoryTypeMapping.Default.Clone(
                comparer: new ValueComparer<long>(
                    bool (long v1, long v2) => v1 == v2,
                    int (long v) => ((object)v).GetHashCode(),
                    long (long v) => v),
                keyComparer: new ValueComparer<long>(
                    bool (long v1, long v2) => v1 == v2,
                    int (long v) => ((object)v).GetHashCode(),
                    long (long v) => v),
                providerValueComparer: new ValueComparer<long>(
                    bool (long v1, long v2) => v1 == v2,
                    int (long v) => ((object)v).GetHashCode(),
                    long (long v) => v),
                clrType: typeof(long),
                jsonValueReaderWriter: JsonInt64ReaderWriter.Instance);
            principalId.SetCurrentValueComparer(new EntryCurrentValueComparer<long>(principalId));

            var key = runtimeEntityType.AddKey(
                new[] { id });
            runtimeEntityType.SetPrimaryKey(key);

            var key0 = runtimeEntityType.AddKey(
                new[] { principalId });

            return runtimeEntityType;
        }

        public static RuntimeForeignKey CreateForeignKey1(RuntimeEntityType declaringEntityType, RuntimeEntityType principalEntityType)
        {
            var runtimeForeignKey = declaringEntityType.AddForeignKey(new[] { declaringEntityType.FindProperty("PrincipalId") },
                principalEntityType.FindKey(new[] { principalEntityType.FindProperty("PrincipalId") }),
                principalEntityType,
                deleteBehavior: DeleteBehavior.Cascade,
                unique: true,
                required: true);

            var principal = declaringEntityType.AddNavigation("Principal",
                runtimeForeignKey,
                onDependent: true,
                typeof(CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>),
                propertyInfo: typeof(CompiledModelTestBase.DependentBase<long?>).GetProperty("Principal", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                fieldInfo: typeof(CompiledModelTestBase.DependentBase<long?>).GetField("<Principal>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));

            principal.SetGetter(
                CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> (CompiledModelTestBase.DependentBase<long?> instance) => DependentBaseUnsafeAccessors<long?>.Principal(instance),
                bool (CompiledModelTestBase.DependentBase<long?> instance) => DependentBaseUnsafeAccessors<long?>.Principal(instance) == null);
            principal.SetSetter(
                CompiledModelTestBase.DependentBase<long?> (CompiledModelTestBase.DependentBase<long?> instance, CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> value) =>
                {
                    DependentBaseUnsafeAccessors<long?>.Principal(instance) = value;
                    return instance;
                });
            principal.SetMaterializationSetter(
                CompiledModelTestBase.DependentBase<long?> (CompiledModelTestBase.DependentBase<long?> instance, CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> value) =>
                {
                    DependentBaseUnsafeAccessors<long?>.Principal(instance) = value;
                    return instance;
                });
            principal.SetAccessors(
                CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> (IInternalEntry entry) => DependentBaseUnsafeAccessors<long?>.Principal(((CompiledModelTestBase.DependentBase<long?>)(entry.Entity))),
                CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> (IInternalEntry entry) => DependentBaseUnsafeAccessors<long?>.Principal(((CompiledModelTestBase.DependentBase<long?>)(entry.Entity))),
                null,
                CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> (IInternalEntry entry) => entry.GetCurrentValue<CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>>(principal));
            principal.SetPropertyIndexes(
                index: 0,
                originalValueIndex: -1,
                shadowIndex: -1,
                relationshipIndex: 2,
                storeGenerationIndex: -1);
            var dependent = principalEntityType.AddNavigation("Dependent",
                runtimeForeignKey,
                onDependent: false,
                typeof(CompiledModelTestBase.DependentBase<long?>),
                propertyInfo: typeof(CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>).GetProperty("Dependent", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                fieldInfo: typeof(CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>).GetField("<Dependent>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));

            dependent.SetGetter(
                CompiledModelTestBase.DependentBase<long?> (CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> instance) => PrincipalDerivedUnsafeAccessors<CompiledModelTestBase.DependentBase<long?>>.Dependent(instance),
                bool (CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> instance) => PrincipalDerivedUnsafeAccessors<CompiledModelTestBase.DependentBase<long?>>.Dependent(instance) == null);
            dependent.SetSetter(
                CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> (CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> instance, CompiledModelTestBase.DependentBase<long?> value) =>
                {
                    PrincipalDerivedUnsafeAccessors<CompiledModelTestBase.DependentBase<long?>>.Dependent(instance) = value;
                    return instance;
                });
            dependent.SetMaterializationSetter(
                CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> (CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>> instance, CompiledModelTestBase.DependentBase<long?> value) =>
                {
                    PrincipalDerivedUnsafeAccessors<CompiledModelTestBase.DependentBase<long?>>.Dependent(instance) = value;
                    return instance;
                });
            dependent.SetAccessors(
                CompiledModelTestBase.DependentBase<long?> (IInternalEntry entry) => PrincipalDerivedUnsafeAccessors<CompiledModelTestBase.DependentBase<long?>>.Dependent(((CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>)(entry.Entity))),
                CompiledModelTestBase.DependentBase<long?> (IInternalEntry entry) => PrincipalDerivedUnsafeAccessors<CompiledModelTestBase.DependentBase<long?>>.Dependent(((CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>)(entry.Entity))),
                null,
                CompiledModelTestBase.DependentBase<long?> (IInternalEntry entry) => entry.GetCurrentValue<CompiledModelTestBase.DependentBase<long?>>(dependent));
            dependent.SetPropertyIndexes(
                index: 1,
                originalValueIndex: -1,
                shadowIndex: -1,
                relationshipIndex: 3,
                storeGenerationIndex: -1);
            return runtimeForeignKey;
        }

        public static void CreateAnnotations(RuntimeEntityType runtimeEntityType)
        {
            var id = runtimeEntityType.FindProperty("Id");
            var principalId = runtimeEntityType.FindProperty("PrincipalId");
            var key = runtimeEntityType.FindKey(new[] { id });
            key.SetPrincipalKeyValueFactory(KeyValueFactoryFactory.CreateSimpleNullableFactory<long?, long>(key));
            key.SetIdentityMapFactory(IdentityMapFactoryFactory.CreateFactory<long?>(key));
            var key0 = runtimeEntityType.FindKey(new[] { principalId });
            key0.SetPrincipalKeyValueFactory(KeyValueFactoryFactory.CreateSimpleNonNullableFactory<long>(key0));
            key0.SetIdentityMapFactory(IdentityMapFactoryFactory.CreateFactory<long>(key0));
            var principal = runtimeEntityType.FindNavigation("Principal");
            runtimeEntityType.SetOriginalValuesFactory(
                ISnapshot (IInternalEntry source) =>
                {
                    var structuralType = ((CompiledModelTestBase.DependentBase<long?>)(source.Entity));
                    return ((ISnapshot)(new Snapshot<long?, long>((source.GetCurrentValue<long?>(id) == null ? null : ((ValueComparer<long?>)(((IProperty)id).GetValueComparer())).Snapshot(source.GetCurrentValue<long?>(id))), ((ValueComparer<long>)(((IProperty)principalId).GetValueComparer())).Snapshot(source.GetCurrentValue<long>(principalId)))));
                });
            runtimeEntityType.SetStoreGeneratedValuesFactory(
                ISnapshot () => ((ISnapshot)(new Snapshot<long?, long>((default(long? ) == null ? null : ((ValueComparer<long?>)(((IProperty)id).GetValueComparer())).Snapshot(default(long? ))), ((ValueComparer<long>)(((IProperty)principalId).GetValueComparer())).Snapshot(default(long))))));
            runtimeEntityType.SetTemporaryValuesFactory(
                ISnapshot (IInternalEntry source) => ((ISnapshot)(new Snapshot<long?, long>(default(long? ), default(long)))));
            runtimeEntityType.SetShadowValuesFactory(
                ISnapshot (IDictionary<string, object> source) => ((ISnapshot)(new Snapshot<long>((source.ContainsKey("PrincipalId") ? ((long)(source["PrincipalId"])) : 0L)))));
            runtimeEntityType.SetEmptyShadowValuesFactory(
                ISnapshot () => ((ISnapshot)(new Snapshot<long>(default(long)))));
            runtimeEntityType.SetRelationshipSnapshotFactory(
                ISnapshot (IInternalEntry source) =>
                {
                    var structuralType = ((CompiledModelTestBase.DependentBase<long?>)(source.Entity));
                    return ((ISnapshot)(new Snapshot<long?, long, object>((source.GetCurrentValue<long?>(id) == null ? null : ((ValueComparer<long?>)(((IProperty)id).GetKeyValueComparer())).Snapshot(source.GetCurrentValue<long?>(id))), ((ValueComparer<long>)(((IProperty)principalId).GetKeyValueComparer())).Snapshot(source.GetCurrentValue<long>(principalId)), source.GetCurrentValue<CompiledModelTestBase.PrincipalDerived<CompiledModelTestBase.DependentBase<long?>>>(principal))));
                });
            runtimeEntityType.SetCounts(new PropertyCounts(
                propertyCount: 2,
                navigationCount: 1,
                complexPropertyCount: 0,
                complexCollectionCount: 0,
                originalValueCount: 2,
                shadowCount: 1,
                relationshipCount: 3,
                storeGeneratedCount: 2));

            Customize(runtimeEntityType);
        }

        static partial void Customize(RuntimeEntityType runtimeEntityType);
    }
}
