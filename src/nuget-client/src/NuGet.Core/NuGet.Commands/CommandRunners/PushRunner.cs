// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace NuGet.Commands
{
    /// <summary>
    /// Shared code to run the "push" command from the command line projects
    /// </summary>
    public static class PushRunner
    {
        public static async Task Run(
            ISettings settings,
            IPackageSourceProvider sourceProvider,
            IList<string> packagePaths,
            string source,
            string apiKey,
            string symbolSource,
            string symbolApiKey,
            int timeoutSeconds,
            bool disableBuffering,
            bool noSymbols,
            bool noServiceEndpoint,
            bool skipDuplicate,
            ILogger logger)
        {
            source = CommandRunnerUtility.ResolveSource(sourceProvider, source);
            symbolSource = CommandRunnerUtility.ResolveSymbolSource(sourceProvider, symbolSource);

            if (timeoutSeconds == 0)
            {
                timeoutSeconds = 5 * 60;
            }
            PackageSource packageSource = CommandRunnerUtility.GetOrCreatePackageSource(sourceProvider, source);
            var packageUpdateResource = await CommandRunnerUtility.GetPackageUpdateResource(sourceProvider, packageSource, CancellationToken.None);

            // Throw an error if an http source is used without setting AllowInsecureConnections
            if (packageSource.IsHttp && !packageSource.IsHttps && !packageSource.AllowInsecureConnections)
            {
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, Strings.Error_HttpSource_Single, "push", packageSource.Source));
            }

            packageUpdateResource.Settings = settings;
            bool allowSnupkg = false;
            SymbolPackageUpdateResourceV3 symbolPackageUpdateResource = null;

            // figure out from index.json if pushing snupkg is supported
            var sourceUri = packageUpdateResource.SourceUri;
            var symbolSourceUri = symbolSource;

            if (!string.IsNullOrEmpty(symbolSource) && !noSymbols)
            {
                //If the symbol source is set we try to get the symbol package resource to determine if Snupkg are supported.
                symbolPackageUpdateResource = await CommandRunnerUtility.GetSymbolPackageUpdateResource(sourceProvider, symbolSource, CancellationToken.None);
                if (symbolPackageUpdateResource != null)
                {
                    allowSnupkg = true;
                    symbolSourceUri = symbolPackageUpdateResource.SourceUri.AbsoluteUri;
                }
            }
            else if (!noSymbols
                && !sourceUri.IsFile
                && sourceUri.IsAbsoluteUri)
            {
                symbolPackageUpdateResource = await CommandRunnerUtility.GetSymbolPackageUpdateResource(sourceProvider, source, CancellationToken.None);
                if (symbolPackageUpdateResource != null)
                {
                    allowSnupkg = true;
                    symbolSource = symbolSourceUri = symbolPackageUpdateResource.SourceUri.AbsoluteUri;
                }
            }

            // Precedence for package API key: -ApiKey param, config
            apiKey ??= CommandRunnerUtility.GetApiKey(settings, packageUpdateResource.SourceUri.AbsoluteUri, source);

            // Precedence for symbol package API key: -SymbolApiKey param, config, package API key (Only for symbol source from SymbolPackagePublish service)
            if (!string.IsNullOrEmpty(symbolSource))
            {
                symbolApiKey ??= CommandRunnerUtility.GetApiKey(settings, symbolSourceUri, symbolSource);

                // Only allow falling back to API key when the symbol source was obtained from SymbolPackagePublish service
                if (symbolPackageUpdateResource != null)
                {
                    symbolApiKey ??= apiKey;
                }
            }

            await packageUpdateResource.PushAsync(
                packagePaths,
                symbolSourceUri,
                timeoutSeconds,
                disableBuffering,
                _ => apiKey,
                _ => symbolApiKey,
                noServiceEndpoint,
                skipDuplicate,
                allowSnupkg,
                packageSource.AllowInsecureConnections,
                logger);
        }
    }
}
