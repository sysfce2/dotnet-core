// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Cosmos.Metadata.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Cosmos-specific extension methods for <see cref="EntityTypeBuilder" />.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
/// </remarks>
public static class CosmosEntityTypeBuilderExtensions
{
    /// <summary>
    ///     Configures the container that the entity type maps to when targeting Azure Cosmos.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the container.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder ToContainer(
        this EntityTypeBuilder entityTypeBuilder,
        string? name)
    {
        Check.NullButNotEmpty(name);

        entityTypeBuilder.Metadata.SetContainer(name);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the container that the entity type maps to when targeting Azure Cosmos.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the container.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> ToContainer<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string? name)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)ToContainer((EntityTypeBuilder)entityTypeBuilder, name);

    /// <summary>
    ///     Configures the container that the entity type maps to when targeting Azure Cosmos.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the container.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>
    ///     The same builder instance if the configuration was applied,
    ///     <see langword="null" /> otherwise.
    /// </returns>
    public static IConventionEntityTypeBuilder? ToContainer(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        string? name,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetContainer(name, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetContainer(name, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the container that the entity type maps to can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the container.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetContainer(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        string? name,
        bool fromDataAnnotation = false)
    {
        Check.NullButNotEmpty(name);

        return entityTypeBuilder.CanSetAnnotation(CosmosAnnotationNames.ContainerName, name, fromDataAnnotation);
    }

    /// <summary>
    ///     Configures the property name that the entity is mapped to when stored as an embedded document.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the parent property.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static OwnedNavigationBuilder ToJsonProperty(
        this OwnedNavigationBuilder entityTypeBuilder,
        string? name)
    {
        entityTypeBuilder.OwnedEntityType.SetContainingPropertyName(name);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the property name that the entity is mapped to when stored as an embedded document.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the parent property.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static OwnedNavigationBuilder<TOwnerEntity, TDependentEntity> ToJsonProperty<TOwnerEntity, TDependentEntity>(
        this OwnedNavigationBuilder<TOwnerEntity, TDependentEntity> entityTypeBuilder,
        string? name)
        where TOwnerEntity : class
        where TDependentEntity : class
    {
        entityTypeBuilder.OwnedEntityType.SetContainingPropertyName(name);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the property name that the entity is mapped to when stored as an embedded document.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the parent property.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>
    ///     The same builder instance if the configuration was applied,
    ///     <see langword="null" /> otherwise.
    /// </returns>
    public static IConventionEntityTypeBuilder? ToJsonProperty(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        string? name,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetJsonProperty(name, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetContainingPropertyName(name, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the parent property name to which the entity type is mapped to can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the parent property.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetJsonProperty(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        string? name,
        bool fromDataAnnotation = false)
    {
        Check.NullButNotEmpty(name);

        return entityTypeBuilder.CanSetAnnotation(CosmosAnnotationNames.PropertyName, name, fromDataAnnotation);
    }

    /// <summary>
    ///     Configures the properties that are used to store the parts of a simple or
    ///     <see href="https://aka.ms/efcore-cosmos-hpkdocs">hierarchical partition key</see>.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the first or only partition key property.</param>
    /// <param name="additionalPropertyNames">The names of additional properties that will form a hierarchical partition key.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder HasPartitionKey(
        this EntityTypeBuilder entityTypeBuilder,
        string? name,
        params string[]? additionalPropertyNames)
    {
        Check.NullButNotEmpty(name);
        Check.HasNoEmptyElements(additionalPropertyNames);

        if (name is null)
        {
            entityTypeBuilder.Metadata.SetPartitionKeyPropertyNames(null);
        }
        else
        {
            var propertyNames = new List<string> { name };
            propertyNames.AddRange(additionalPropertyNames);
            entityTypeBuilder.Metadata.SetPartitionKeyPropertyNames(propertyNames);
        }

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the properties that are used to store the parts of a simple or
    ///     <see href="https://aka.ms/efcore-cosmos-hpkdocs">hierarchical partition key</see>.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the first or only partition key property.</param>
    /// <param name="additionalPropertyNames">The names of additional properties that will form a hierarchical partition key.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasPartitionKey<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string? name,
        params string[]? additionalPropertyNames)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasPartitionKey((EntityTypeBuilder)entityTypeBuilder, name, additionalPropertyNames);

    /// <summary>
    ///     Configures the properties that are used to store the parts of a simple or
    ///     <see href="https://aka.ms/efcore-cosmos-hpkdocs">hierarchical partition key</see>.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="propertyExpression">The properties that will form the partition key.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasPartitionKey<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        Expression<Func<TEntity, TProperty>> propertyExpression)
        where TEntity : class
    {
        Check.NotNull(propertyExpression);

        entityTypeBuilder.Metadata.SetPartitionKeyPropertyNames(
            propertyExpression.GetMemberAccessList().Select(e => e.GetSimpleMemberName()).ToList());

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the property that is used to store the partition key.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the partition key property.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>
    ///     The same builder instance if the configuration was applied,
    ///     <see langword="null" /> otherwise.
    /// </returns>
    [Obsolete("Use HasPartitionKey(IReadOnlyList<string>, bool)")]
    public static IConventionEntityTypeBuilder? HasPartitionKey(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        string? name,
        bool fromDataAnnotation = false)
        => entityTypeBuilder.HasPartitionKey(name == null ? null : [name], fromDataAnnotation);

    /// <summary>
    ///     Returns a value indicating whether the property that is used to store the partition key can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="name">The name of the partition key property.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    [Obsolete("Use HasPartitionKey(IReadOnlyList<string>, bool)")]
    public static bool CanSetPartitionKey(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        string? name,
        bool fromDataAnnotation = false)
        => entityTypeBuilder.CanSetPartitionKey(name == null ? null : [name], fromDataAnnotation);

    /// <summary>
    ///     Configures the properties that are used to store the parts of a
    ///     <see href="https://aka.ms/efcore-cosmos-hpkdocs">hierarchical partition key</see>.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="propertyNames">The names of the properties that will form the hierarchical partition key.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>The same builder instance if the configuration was applied, <see langword="null" /> otherwise.</returns>
    public static IConventionEntityTypeBuilder? HasPartitionKey(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        IReadOnlyList<string>? propertyNames,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetPartitionKey(propertyNames, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetPartitionKeyPropertyNames(propertyNames, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the properties that are used to store the parts of a hierarchical partition key
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="names">The name of the partition key properties.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetPartitionKey(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        IReadOnlyList<string>? names,
        bool fromDataAnnotation = false)
        => entityTypeBuilder.CanSetAnnotation(
            CosmosAnnotationNames.PartitionKeyNames,
            names is null ? names : Check.HasNoEmptyElements(names),
            fromDataAnnotation);

    /// <summary>
    ///     Configures this entity to use CosmosDb etag concurrency checks.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder UseETagConcurrency(this EntityTypeBuilder entityTypeBuilder)
    {
        entityTypeBuilder.Property<string>("_etag")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures this entity to use CosmosDb etag concurrency checks.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> UseETagConcurrency<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)UseETagConcurrency((EntityTypeBuilder)entityTypeBuilder);

    /// <summary>
    ///     Forces model building to always create a "__id" shadow property mapped to the JSON "id". This was the default
    ///     behavior before EF Core 9.0.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="alwaysCreate">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder HasShadowId(
        this EntityTypeBuilder entityTypeBuilder,
        bool? alwaysCreate = true)
    {
        entityTypeBuilder.Metadata.SetHasShadowId(alwaysCreate);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Forces model building to always create a "__id" shadow property mapped to the JSON "id". This was the default
    ///     behavior before EF Core 9.0. Note that an "__id" property is always created if the primary key is not a single string property,
    ///     or if the discriminator is included in the JSON "id".
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="alwaysCreate">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasShadowId<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        bool? alwaysCreate = true)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasShadowId((EntityTypeBuilder)entityTypeBuilder, alwaysCreate);

    /// <summary>
    ///     Forces model building to always create a "__id" shadow property mapped to the JSON "id". This was the default
    ///     behavior before EF Core 9.0.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="alwaysCreate">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>The same builder instance if the configuration was applied, <see langword="null" /> otherwise.</returns>
    public static IConventionEntityTypeBuilder? HasShadowId(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        bool? alwaysCreate,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetShadowId(alwaysCreate, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetHasShadowId(alwaysCreate, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the setting for always creating the "__id" property can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="alwaysCreate">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetShadowId(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        bool? alwaysCreate,
        bool fromDataAnnotation = false)
    {
        Check.NotNull(entityTypeBuilder);

        return entityTypeBuilder.CanSetAnnotation(CosmosAnnotationNames.HasShadowId, alwaysCreate, fromDataAnnotation);
    }

    /// <summary>
    ///     Includes the discriminator value of the entity type in the JSON "id" value. This was the default behavior before EF Core 9.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to include the discriminator, <see langword="false" /> to not include the discriminator,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder HasDiscriminatorInJsonId(
        this EntityTypeBuilder entityTypeBuilder,
        bool? includeDiscriminator = true)
    {
        entityTypeBuilder.Metadata.SetDiscriminatorInKey(
            includeDiscriminator == null
                ? null
                : includeDiscriminator.Value
                    ? IdDiscriminatorMode.EntityType
                    : IdDiscriminatorMode.None);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Includes the discriminator value of the root entity type in the JSON "id" value. This allows types with the same
    ///     primary key to be saved in the same container, while still allowing "ReadItem" to be used for lookups of an unknown type.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to include the discriminator, <see langword="false" /> to not include the discriminator,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder HasRootDiscriminatorInJsonId(
        this EntityTypeBuilder entityTypeBuilder,
        bool? includeDiscriminator = true)
    {
        entityTypeBuilder.Metadata.SetDiscriminatorInKey(
            includeDiscriminator == null
                ? null
                : includeDiscriminator.Value
                    ? IdDiscriminatorMode.RootEntityType
                    : IdDiscriminatorMode.None);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Includes the discriminator value of the entity type in the JSON "id" value. This was the default behavior before EF Core 9.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to include the discriminator, <see langword="false" /> to not include the discriminator,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasDiscriminatorInJsonId<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        bool? includeDiscriminator = true)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasDiscriminatorInJsonId((EntityTypeBuilder)entityTypeBuilder, includeDiscriminator);

    /// <summary>
    ///     Includes the discriminator value of the root entity type in the JSON "id" value. This allows types with the same
    ///     primary key to be saved in the same container, while still allowing "ReadItem" to be used for lookups of an unknown type.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to include the discriminator, <see langword="false" /> to not include the discriminator,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasRootDiscriminatorInJsonId<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        bool? includeDiscriminator = true)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasRootDiscriminatorInJsonId((EntityTypeBuilder)entityTypeBuilder, includeDiscriminator);

    /// <summary>
    ///     Includes the discriminator value of the entity type in the JSON "id" value. This was the default behavior before EF Core 9.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>The same builder instance if the configuration was applied, <see langword="null" /> otherwise.</returns>
    public static IConventionEntityTypeBuilder? HasDiscriminatorInJsonId(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        bool? includeDiscriminator,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetDiscriminatorInJsonId(includeDiscriminator, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetDiscriminatorInKey(
            includeDiscriminator == null
                ? null
                : includeDiscriminator.Value
                    ? IdDiscriminatorMode.EntityType
                    : IdDiscriminatorMode.None, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Includes the discriminator value of the root entity type in the JSON "id" value. This allows types with the same
    ///     primary key to be saved in the same container, while still allowing "ReadItem" to be used for lookups of an unknown type.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>The same builder instance if the configuration was applied, <see langword="null" /> otherwise.</returns>
    public static IConventionEntityTypeBuilder? HasRootDiscriminatorInJsonId(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        bool? includeDiscriminator,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetRootDiscriminatorInJsonId(includeDiscriminator, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetDiscriminatorInKey(
            includeDiscriminator == null
                ? null
                : includeDiscriminator.Value
                    ? IdDiscriminatorMode.EntityType
                    : IdDiscriminatorMode.None, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the setting for including the discriminator can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetDiscriminatorInJsonId(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        bool? includeDiscriminator,
        bool fromDataAnnotation = false)
    {
        Check.NotNull(entityTypeBuilder);

        return entityTypeBuilder.CanSetAnnotation(
            CosmosAnnotationNames.DiscriminatorInKey,
            includeDiscriminator == null
                ? null
                : includeDiscriminator.Value
                    ? IdDiscriminatorMode.EntityType
                    : IdDiscriminatorMode.None, fromDataAnnotation);
    }

    /// <summary>
    ///     Returns a value indicating whether the setting for including the discriminator can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="includeDiscriminator">
    ///     <see langword="true" /> to force __id creation, <see langword="false" /> to not force __id creation,
    ///     <see langword="null" /> to revert to the default setting.
    /// </param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetRootDiscriminatorInJsonId(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        bool? includeDiscriminator,
        bool fromDataAnnotation = false)
    {
        Check.NotNull(entityTypeBuilder);

        return entityTypeBuilder.CanSetAnnotation(
            CosmosAnnotationNames.DiscriminatorInKey,
            includeDiscriminator == null
                ? null
                : includeDiscriminator.Value
                    ? IdDiscriminatorMode.RootEntityType
                    : IdDiscriminatorMode.None, fromDataAnnotation);
    }

    /// <summary>
    ///     Configures the time to live for analytical store in seconds at container scope.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder HasAnalyticalStoreTimeToLive(
        this EntityTypeBuilder entityTypeBuilder,
        int? seconds)
    {
        entityTypeBuilder.Metadata.SetAnalyticalStoreTimeToLive(seconds);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the time to live for analytical store in seconds at container scope.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasAnalyticalStoreTimeToLive<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        int? seconds)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasAnalyticalStoreTimeToLive((EntityTypeBuilder)entityTypeBuilder, seconds);

    /// <summary>
    ///     Configures the time to live for analytical store in seconds at container scope.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>
    ///     The same builder instance if the configuration was applied,
    ///     <see langword="null" /> otherwise.
    /// </returns>
    public static IConventionEntityTypeBuilder? HasAnalyticalStoreTimeToLive(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        int? seconds,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetAnalyticalStoreTimeToLive(seconds, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetAnalyticalStoreTimeToLive(seconds, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the time to live for analytical store can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetAnalyticalStoreTimeToLive(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        int? seconds,
        bool fromDataAnnotation = false)
    {
        Check.NotNull(entityTypeBuilder);

        return entityTypeBuilder.CanSetAnnotation(CosmosAnnotationNames.AnalyticalStoreTimeToLive, seconds, fromDataAnnotation);
    }

    /// <summary>
    ///     Configures the default time to live in seconds at container scope.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder HasDefaultTimeToLive(
        this EntityTypeBuilder entityTypeBuilder,
        int? seconds)
    {
        entityTypeBuilder.Metadata.SetDefaultTimeToLive(seconds);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the default time to live in seconds at container scope.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasDefaultTimeToLive<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        int? seconds)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasDefaultTimeToLive((EntityTypeBuilder)entityTypeBuilder, seconds);

    /// <summary>
    ///     Configures the default time to live in seconds at container scope.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns>
    ///     The same builder instance if the configuration was applied,
    ///     <see langword="null" /> otherwise.
    /// </returns>
    public static IConventionEntityTypeBuilder? HasDefaultTimeToLive(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        int? seconds,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetDefaultTimeToLive(seconds, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetDefaultTimeToLive(seconds, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the default time to live can be set
    ///     from the current configuration source
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="seconds">The time to live.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the configuration can be applied.</returns>
    public static bool CanSetDefaultTimeToLive(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        int? seconds,
        bool fromDataAnnotation = false)
        => entityTypeBuilder.CanSetAnnotation(CosmosAnnotationNames.DefaultTimeToLive, seconds, fromDataAnnotation);

    /// <summary>
    ///     Configures the manual provisioned throughput offering.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="throughput">The throughput to set.</param>
    public static EntityTypeBuilder HasManualThroughput(this EntityTypeBuilder entityTypeBuilder, int? throughput)
    {
        entityTypeBuilder.Metadata.SetThroughput(throughput, autoscale: false);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the manual provisioned throughput offering.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="throughput">The throughput to set.</param>
    public static EntityTypeBuilder<TEntity> HasManualThroughput<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        int? throughput)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasManualThroughput((EntityTypeBuilder)entityTypeBuilder, throughput);

    /// <summary>
    ///     Configures the autoscale provisioned throughput offering.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="throughput">The throughput to set.</param>
    public static EntityTypeBuilder HasAutoscaleThroughput(this EntityTypeBuilder entityTypeBuilder, int? throughput)
    {
        entityTypeBuilder.Metadata.SetThroughput(throughput, autoscale: true);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the autoscale provisioned throughput offering.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="throughput">The throughput to set.</param>
    public static EntityTypeBuilder<TEntity> HasAutoscaleThroughput<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        int? throughput)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasAutoscaleThroughput((EntityTypeBuilder)entityTypeBuilder, throughput);

    /// <summary>
    ///     Configures the provisioned throughput.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="throughput">The throughput to set.</param>
    /// <param name="autoscale">Whether autoscale is enabled.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    public static IConventionEntityTypeBuilder? HasThroughput(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        int? throughput,
        bool autoscale,
        bool fromDataAnnotation = false)
    {
        if (!entityTypeBuilder.CanSetThroughput(throughput, autoscale, fromDataAnnotation))
        {
            return null;
        }

        entityTypeBuilder.Metadata.SetThroughput(throughput, autoscale, fromDataAnnotation);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Returns a value indicating whether the given throughput can be set.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-modeling">Modeling entity types and relationships</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Azure Cosmos DB with EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="throughput">The throughput to set.</param>
    /// <param name="autoscale">Whether autoscale is enabled.</param>
    /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
    /// <returns><see langword="true" /> if the given container name can be set as default.</returns>
    public static bool CanSetThroughput(
        this IConventionEntityTypeBuilder entityTypeBuilder,
        int? throughput,
        bool autoscale,
        bool fromDataAnnotation = false)
    {
        var existingAnnotation = entityTypeBuilder.Metadata.FindAnnotation(CosmosAnnotationNames.Throughput);
        if (existingAnnotation == null)
        {
            return true;
        }

        var configurationSource = fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention;
        if (configurationSource.Overrides(existingAnnotation.GetConfigurationSource()))
        {
            return true;
        }

        var existingThroughput = (ThroughputProperties?)existingAnnotation.Value;
        return autoscale
            ? existingThroughput?.Throughput == throughput
            : existingThroughput?.AutoscaleMaxThroughput == throughput;
    }
}
