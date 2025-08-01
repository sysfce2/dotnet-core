// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Provides CLR methods that get translated to database functions when used in LINQ to Entities queries.
///     The methods on this class are accessed via <see cref="EF.Functions" />.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-database-functions">Database functions</see>, and
///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Cosmos with EF Core</see> for more information and examples.
/// </remarks>
public static class CosmosDbFunctionsExtensions
{
    /// <summary>
    ///     Returns a boolean indicating if the property has been assigned a value. Corresponds to the Cosmos <c>IS_DEFINED</c> function.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-database-functions">Database functions</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Cosmos with EF Core</see>
    ///     for more information and examples.
    /// </remarks>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="expression">The expression to check.</param>
    /// <seealso href="https://learn.microsoft.com/azure/cosmos-db/nosql/query/is-defined">Cosmos <c>IS_DEFINED_</c> function</seealso>
    public static bool IsDefined(this DbFunctions _, object? expression)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(IsDefined)));

    /// <summary>
    ///     Coalesces a Cosmos <c>undefined</c> value via the <c>??</c> operator.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-database-functions">Database functions</see>, and
    ///     <see href="https://aka.ms/efcore-docs-cosmos">Accessing Cosmos with EF Core</see>
    ///     for more information and examples.
    /// </remarks>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="expression1">
    ///     The expression to coalesce. This expression will be returned unless it is <c>undefined</c>, in which case
    ///     <paramref name="expression2" /> will be returned.
    /// </param>
    /// <param name="expression2">The expression to be returned if <paramref name="expression1" /> is <c>undefined</c>.</param>
    /// <seealso href="https://learn.microsoft.com/azure/cosmos-db/nosql/query/ternary-coalesce-operators#coalesce-operator">
    ///     Cosmos coalesce operator
    /// </seealso>
    public static T CoalesceUndefined<T>(
        this DbFunctions _,
        T expression1,
        T expression2)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(CoalesceUndefined)));

    /// <summary>
    ///     Checks if the specified property contains the given keyword using full-text search.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="property">The property to search.</param>
    /// <param name="keyword">The keyword to search for.</param>
    /// <returns><see langword="true" /> if the property contains the keyword; otherwise, <see langword="false" />.</returns>
    public static bool FullTextContains(this DbFunctions _, string property, string keyword)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(FullTextContains)));

    /// <summary>
    ///     Checks if the specified property contains all the given keywords using full-text search.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="property">The property to search.</param>
    /// <param name="keywords">The keywords to search for.</param>
    /// <returns><see langword="true" /> if the property contains all the keywords; otherwise, <see langword="false" />.</returns>
    public static bool FullTextContainsAll(this DbFunctions _, string property, params string[] keywords)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(FullTextContainsAll)));

    /// <summary>
    ///     Checks if the specified property contains any of the given keywords using full-text search.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="property">The property to search.</param>
    /// <param name="keywords">The keywords to search for.</param>
    /// <returns><see langword="true" /> if the property contains any of the keywords; otherwise, <see langword="false" />.</returns>
    public static bool FullTextContainsAny(this DbFunctions _, string property, params string[] keywords)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(FullTextContainsAny)));

    /// <summary>
    ///     Returns the full-text search score for the specified property and keywords.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="property">The property to score.</param>
    /// <param name="keywords">The keywords to score by.</param>
    /// <returns>The full-text search score.</returns>
    public static double FullTextScore(this DbFunctions _, string property, params string[] keywords)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(FullTextScore)));

    /// <summary>
    ///     Combines scores provided by two or more specified functions.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="scores">Scoring function calls to be combined.</param>
    /// <returns>The combined score.</returns>
    public static double Rrf(this DbFunctions _, params double[] scores)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(Rrf)));

    #region VectorDistance

    /// <summary>
    ///     Returns the distance between two vectors, given a distance function (aka similarity measure).
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="vector1">The first vector.</param>
    /// <param name="vector2">The second vector.</param>
    /// <param name="useBruteForce">
    ///     An optional boolean specifying how the computed value is used in an <c>ORDER BY</c> expression.
    ///     If <see langword="true"/>, then brute force is used. A value of <see langword="false" /> uses any index defined on the vector
    ///     property, if it exists. Default value is <see langword="false" />.
    /// </param>
    /// <param name="options">An optional object used to specify options for the vector distance calculation.</param>
    public static double VectorDistance(
        this DbFunctions _,
        ReadOnlyMemory<float> vector1,
        ReadOnlyMemory<float> vector2,
        [NotParameterized] bool? useBruteForce = null,
        [NotParameterized] VectorDistanceOptions? options = null)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VectorDistance)));

    /// <summary>
    ///     Returns the distance between two vectors, given a distance function (aka similarity measure).
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="vector1">The first vector.</param>
    /// <param name="vector2">The second vector.</param>
    /// <param name="useBruteForce">
    ///     An optional boolean specifying how the computed value is used in an <c>ORDER BY</c> expression.
    ///     If <see langword="true"/>, then brute force is used. A value of <see langword="false" /> uses any index defined on the vector
    ///     property, if it exists. Default value is <see langword="false" />.
    /// </param>
    /// <param name="options">An optional object used to specify options for the vector distance calculation.</param>
    public static double VectorDistance(
        this DbFunctions _,
        ReadOnlyMemory<byte> vector1,
        ReadOnlyMemory<byte> vector2,
        [NotParameterized] bool? useBruteForce = null,
        [NotParameterized] VectorDistanceOptions? options = null)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VectorDistance)));

    /// <summary>
    ///     Returns the distance between two vectors, given a distance function (aka similarity measure).
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions" /> instance.</param>
    /// <param name="vector1">The first vector.</param>
    /// <param name="vector2">The second vector.</param>
    /// <param name="useBruteForce">
    ///     An optional boolean specifying how the computed value is used in an <c>ORDER BY</c> expression.
    ///     If <see langword="true"/>, then brute force is used. A value of <see langword="false" /> uses any index defined on the vector
    ///     property, if it exists. Default value is <see langword="false" />.
    /// </param>
    /// <param name="options">An optional object used to specify options for the vector distance calculation.</param>
    public static double VectorDistance(
        this DbFunctions _,
        ReadOnlyMemory<sbyte> vector1,
        ReadOnlyMemory<sbyte> vector2,
        [NotParameterized] bool? useBruteForce = null,
        [NotParameterized] VectorDistanceOptions? options = null)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(VectorDistance)));

    #endregion VectorDistance
}
