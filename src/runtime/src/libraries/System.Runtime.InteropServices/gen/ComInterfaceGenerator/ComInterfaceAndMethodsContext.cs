﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Microsoft.Interop
{
    /// <summary>
    /// Represents an interface and all of the methods that need to be generated for it (methods declared on the interface and methods inherited from base interfaces).
    /// </summary>
    internal sealed record ComInterfaceAndMethodsContext(ComInterfaceContext Interface, SequenceEqualImmutableArray<ComMethodContext> Methods)
    {
        /// <summary>
        /// COM methods that are declared on the attributed interface declaration.
        /// </summary>
        public IEnumerable<ComMethodContext> DeclaredMethods => Methods.Where(m => !m.IsInheritedMethod);

        /// <summary>
        /// COM methods that require shadowing declarations on the derived interface.
        /// </summary>
        public IEnumerable<ComMethodContext> ShadowingMethods => Methods.Where(m => m.IsInheritedMethod && !m.IsHiddenOnDerivedInterface && !m.IsExternallyDefined);

        /// <summary>
        /// COM methods that are declared on an interface the interface inherits from.
        /// </summary>
        public IEnumerable<ComMethodContext> InheritedMethods => Methods.Where(m => m.IsInheritedMethod);

        /// <summary>
        /// The size of the vtable for this interface, including the base interface methods and IUnknown methods.
        /// </summary>
        public int VTableSize => Methods.Length == 0
                                    ? IUnknownConstants.VTableSize
                                    : 1 + Methods.Max(m => m.GenerationContext.VtableIndexData.Index);

        /// <summary>
        /// The size of the vtable for the base interface, including it's base interface methods and IUnknown methods.
        /// </summary>
        public int BaseVTableSize => VTableSize - DeclaredMethods.Count();
    }
}
