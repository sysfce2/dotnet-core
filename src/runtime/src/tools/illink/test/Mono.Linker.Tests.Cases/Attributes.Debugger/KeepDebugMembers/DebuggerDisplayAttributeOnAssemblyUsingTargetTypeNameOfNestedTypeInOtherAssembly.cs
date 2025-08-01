﻿using System.Diagnostics;
using Mono.Linker.Tests.Cases.Attributes.Debugger.Dependencies;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Helpers;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

[assembly: KeptAttributeAttribute(typeof(DebuggerDisplayAttribute))]
[assembly: DebuggerDisplay("{Property}", TargetTypeName = "Mono.Linker.Tests.Cases.Attributes.Debugger.Dependencies.DebuggerDisplayAttributeOnAssemblyUsingTargetTypeNameInOtherAssembly_Lib+NestedType, library")]

namespace Mono.Linker.Tests.Cases.Attributes.Debugger.KeepDebugMembers
{
    [SetupLinkerTrimMode("link")]
    [SetupCompileBefore("library.dll", new[] { "../Dependencies/DebuggerDisplayAttributeOnAssemblyUsingTargetTypeNameInOtherAssembly_Lib.cs" })]

    [KeptMemberInAssembly(PlatformAssemblies.CoreLib, typeof(DebuggerDisplayAttribute), ".ctor(System.String)")]
    [KeptMemberInAssembly(PlatformAssemblies.CoreLib, typeof(DebuggerDisplayAttribute), "set_TargetTypeName(System.String)")]

    [KeptMemberInAssembly("library.dll", typeof(DebuggerDisplayAttributeOnAssemblyUsingTargetTypeNameInOtherAssembly_Lib.NestedType), "get_NestedProperty()")]
    [KeptMemberInAssembly("library.dll", typeof(DebuggerDisplayAttributeOnAssemblyUsingTargetTypeNameInOtherAssembly_Lib.NestedType), "set_NestedProperty(System.Int32)")]
    public class DebuggerDisplayAttributeOnAssemblyUsingTargetTypeNameOfNestedTypeInOtherAssembly
    {
        public static void Main()
        {
            var foo = new DebuggerDisplayAttributeOnAssemblyUsingTargetTypeNameInOtherAssembly_Lib.NestedType();
            foo.NestedProperty = 1;
        }
    }
}
