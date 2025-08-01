// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Microsoft.EntityFrameworkCore.Metadata;

/// <summary>
///     Represents a complex property of a structural type.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see> for more information and examples.
/// </remarks>
public class RuntimeComplexProperty : RuntimePropertyBase, IRuntimeComplexProperty
{
    private readonly bool _isNullable;

    // Warning: Never access these fields directly as access needs to be thread-safe
    private IClrCollectionAccessor? _collectionAccessor;
    private bool _collectionAccessorInitialized;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    [EntityFrameworkInternal]
    public RuntimeComplexProperty(
        string name,
        Type clrType,
        string targetTypeName,
        [DynamicallyAccessedMembers(IEntityType.DynamicallyAccessedMemberTypes)] Type targetType,
        PropertyInfo? propertyInfo,
        FieldInfo? fieldInfo,
        RuntimeTypeBase declaringType,
        PropertyAccessMode propertyAccessMode,
        bool nullable,
        bool collection,
        ChangeTrackingStrategy changeTrackingStrategy,
        PropertyInfo? indexerPropertyInfo,
        bool propertyBag,
        string? discriminatorProperty,
        object? discriminatorValue,
        int propertyCount,
        int complexPropertyCount)
        : base(name, propertyInfo, fieldInfo, propertyAccessMode)
    {
        DeclaringType = declaringType;
        ClrType = clrType;
        _isNullable = nullable;
        IsCollection = collection;
        ComplexType = new RuntimeComplexType(
            targetTypeName, targetType, this, changeTrackingStrategy, indexerPropertyInfo, propertyBag,
            discriminatorProperty, discriminatorValue,
            propertyCount: propertyCount,
            complexPropertyCount: complexPropertyCount);
    }

    /// <summary>
    ///     Gets the type of value that this property-like object holds.
    /// </summary>
    [DynamicallyAccessedMembers(IProperty.DynamicallyAccessedMemberTypes)]
    protected override Type ClrType { get; }

    /// <summary>
    ///     Gets the type that this property belongs to.
    /// </summary>
    public override RuntimeTypeBase DeclaringType { get; }

    /// <summary>
    ///     Gets the type of value that this property-like object holds.
    /// </summary>
    public virtual RuntimeComplexType ComplexType { get; }

    /// <inheritdoc />
    public override bool IsCollection { get; }

    /// <inheritdoc />
    public override object? Sentinel
        => null;

    /// <summary>
    ///     Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
        => ((IReadOnlyComplexProperty)this).ToDebugString(MetadataDebugStringOptions.SingleLineDefault);

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    [EntityFrameworkInternal]
    public virtual DebugView DebugView
        => new(
            () => ((IReadOnlyComplexProperty)this).ToDebugString(),
            () => ((IReadOnlyComplexProperty)this).ToDebugString(MetadataDebugStringOptions.LongDefault));

    /// <inheritdoc />
    IReadOnlyTypeBase IReadOnlyPropertyBase.DeclaringType
    {
        [DebuggerStepThrough]
        get => DeclaringType;
    }

    /// <inheritdoc />
    ITypeBase IPropertyBase.DeclaringType
    {
        [DebuggerStepThrough]
        get => DeclaringType;
    }

    /// <inheritdoc />

    IComplexType IComplexProperty.ComplexType
    {
        [DebuggerStepThrough]
        get => ComplexType;
    }

    /// <inheritdoc />
    IReadOnlyComplexType IReadOnlyComplexProperty.ComplexType
    {
        [DebuggerStepThrough]
        get => ComplexType;
    }

    /// <inheritdoc />
    bool IReadOnlyComplexProperty.IsNullable
    {
        [DebuggerStepThrough]
        get => _isNullable;
    }

    /// <inheritdoc />
    [DebuggerStepThrough]
    IClrCollectionAccessor? IPropertyBase.GetCollectionAccessor()
        => NonCapturingLazyInitializer.EnsureInitialized(
            ref _collectionAccessor,
            ref _collectionAccessorInitialized,
            this,
            static complexProperty => ((IComplexProperty)complexProperty).IsCollection
                ? RuntimeFeature.IsDynamicCodeSupported
                    ? ClrCollectionAccessorFactory.Instance.Create(complexProperty)
                    : throw new InvalidOperationException(CoreStrings.NativeAotNoCompiledModel)
                : null);
}
