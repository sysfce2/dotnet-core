﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Razor.ExternalAccess.LegacyEditor;

[Flags]
internal enum AcceptedCharacters
{
    None = 0,
    NewLine = 1,
    WhiteSpace = 2,

    NonWhiteSpace = 4,

    AllWhiteSpace = NewLine | WhiteSpace,
    Any = AllWhiteSpace | NonWhiteSpace,

    AnyExceptNewline = NonWhiteSpace | WhiteSpace
}
