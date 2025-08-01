// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using NuGet.Packaging.Core;

namespace NuGet.PackageManagement
{
    public class PackageRestoreResult
    {
        internal static readonly PackageRestoreResult NoopRestoreResult = new(false, []);

        public PackageRestoreResult(bool restored, IEnumerable<PackageIdentity> restoredPackages, AuditCheckResult? auditCheckResult)
            : this(restored, restoredPackages)
        {
            AuditCheckResult = auditCheckResult;
        }

        public PackageRestoreResult(bool restored, IEnumerable<PackageIdentity> restoredPackages)
        {
            Restored = restored;
            RestoredPackages = restoredPackages ?? throw new ArgumentNullException(nameof(restoredPackages));
        }

        public bool Restored { get; }
        public IEnumerable<PackageIdentity> RestoredPackages { get; }
        public AuditCheckResult? AuditCheckResult { get; }
    }
}
