﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.VisualStudio.Razor.LiveShare.Guest;

internal interface IProxyAccessor
{
    IProjectHierarchyProxy GetProjectHierarchyProxy();
}
