// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using NuGet.CommandLine.XPlat.ListPackage;
using NuGet.CommandLine.XPlat.Utility;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Model;
using NuGet.Protocol.Providers;
using NuGet.Protocol.Resources;
using NuGet.Versioning;

namespace NuGet.CommandLine.XPlat
{
    internal class ListPackageCommandRunner : IListPackageCommandRunner
    {
        private const string ProjectAssetsFile = "ProjectAssetsFile";
        private const string ProjectName = "MSBuildProjectName";
        private const int GenericSuccessExitCode = 0;
        private const int GenericFailureExitCode = 1;
        private Dictionary<PackageSource, SourceRepository> _sourceRepositoryCache;

        public ListPackageCommandRunner()
        {
            _sourceRepositoryCache = new Dictionary<PackageSource, SourceRepository>();
        }

        public async Task<int> ExecuteCommandAsync(ListPackageArgs listPackageArgs)
        {
            IReportRenderer reportRenderer = listPackageArgs.Renderer;
            (int exitCode, ListPackageReportModel reportModel) = await GetReportDataAsync(listPackageArgs);
            reportRenderer.Render(reportModel);
            return exitCode;
        }

        internal async Task<(int, ListPackageReportModel)> GetReportDataAsync(ListPackageArgs listPackageArgs)
        {
            // It's important not to print anything to console from below methods and sub method calls, because it'll affect both json/console outputs.
            var listPackageReportModel = new ListPackageReportModel(listPackageArgs);
            if (!File.Exists(listPackageArgs.Path))
            {
                listPackageArgs.Renderer.AddProblem(problemType: ProblemType.Error,
                    text: string.Format(CultureInfo.CurrentCulture,
                        Strings.ListPkg_ErrorFileNotFound,
                        listPackageArgs.Path));
                return (GenericFailureExitCode, listPackageReportModel);
            }

            PopulateSourceRepositoryCache(listPackageArgs);

            //If the given file is a solution, get the list of projects
            //If not, then it's a project, which is put in a list
            string fileExtension = Path.GetExtension(listPackageArgs.Path);
            var projectsPaths =
                (fileExtension.Equals(".sln", PathUtility.GetStringComparisonBasedOnOS()) ||
                    fileExtension.Equals(".slnx", PathUtility.GetStringComparisonBasedOnOS()) ||
                    fileExtension.Equals(".slnf", PathUtility.GetStringComparisonBasedOnOS()))
                    ? MSBuildAPIUtility.GetProjectsFromSolution(listPackageArgs.Path).Where(File.Exists)
                    : [listPackageArgs.Path];

            MSBuildAPIUtility msBuild = listPackageReportModel.MSBuildAPIUtility;

            foreach (string projectPath in projectsPaths)
            {
                await GetProjectMetadataAsync(projectPath, listPackageReportModel, msBuild, listPackageArgs);
            }

            // if there is any error then return failure code.
            int exitCode = (
                listPackageArgs.Renderer.GetProblems().Any(p => p.ProblemType == ProblemType.Error)
                || listPackageReportModel.Projects.Where(p => p.ProjectProblems != null).SelectMany(p => p.ProjectProblems).Any(p => p.ProblemType == ProblemType.Error))
                ? GenericFailureExitCode : GenericSuccessExitCode;

            return (exitCode, listPackageReportModel);
        }

        private async Task GetProjectMetadataAsync(
            string projectPath,
            ListPackageReportModel listPackageReportModel,
            MSBuildAPIUtility msBuild,
            ListPackageArgs listPackageArgs)
        {
            //Open project to evaluate properties for the assets
            //file and the name of the project
            Project project = MSBuildAPIUtility.GetProject(projectPath);
            var projectName = project.GetPropertyValue(ProjectName);
            ListPackageProjectModel projectModel = listPackageReportModel.CreateProjectReportData(projectPath: projectPath, projectName);

            if (!MSBuildAPIUtility.IsPackageReferenceProject(project))
            {
                projectModel.AddProjectInformation(problemType: ProblemType.Error,
                    string.Format(CultureInfo.CurrentCulture, Strings.Error_NotPRProject, projectPath));
                return;
            }

            var assetsPath = project.GetPropertyValue(ProjectAssetsFile);

            if (!IsProjectAssetsFileValid(assetsPath, projectPath, projectModel, out LockFile assetsFile))
            {
                return;
            }

            List<FrameworkPackages> frameworks;

            try
            {
                frameworks = MSBuildAPIUtility.GetResolvedVersions(project, listPackageArgs.Frameworks, assetsFile, listPackageArgs.IncludeTransitive);
            }
            catch (InvalidOperationException ex)
            {
                projectModel.AddProjectInformation(ProblemType.Error, ex.Message);
                return;
            }

            if (frameworks.Count > 0)
            {
                bool vulnerabilitiesCheckedFromAuditSources = false;

                if (listPackageArgs.ReportType != ReportType.Default)  // generic list package is offline -- no server lookups
                {
                    List<PackageSource> httpSources = HttpSourcesUtility.GetDisallowedInsecureHttpSources(listPackageArgs.PackageSources);
                    httpSources.AddRange(HttpSourcesUtility.GetDisallowedInsecureHttpSources(listPackageArgs.AuditSources));

                    if (httpSources.Count > 0)
                    {
                        projectModel.AddProjectInformation(ProblemType.Error, HttpSourcesUtility.BuildHttpSourceErrorMessage(httpSources, "list package"));
                        return;
                    }

                    if (listPackageArgs.ReportType == ReportType.Vulnerable && listPackageArgs.AuditSources != null && listPackageArgs.AuditSources.Count > 0)
                    {
                        await GetVulnerabilitiesFromAuditSourcesAsync(listPackageArgs, listPackageReportModel, projectModel, frameworks);
                        vulnerabilitiesCheckedFromAuditSources = true;
                    }
                    else
                    {
                        var metadata = await GetPackageMetadataAsync(frameworks, listPackageArgs);
                        await UpdatePackagesWithSourceMetadata(frameworks, metadata, listPackageArgs);
                    }
                }

                if (!vulnerabilitiesCheckedFromAuditSources)
                {
                    bool filterPackages = FilterPackages(frameworks, listPackageArgs) || ReportType.Default == listPackageArgs.ReportType;

                    if (filterPackages)
                    {
                        var hasAutoReference = false;
                        List<ListPackageReportFrameworkPackage> projectFrameworkPackages = ProjectPackagesPrintUtility.GetPackagesMetadata(frameworks, listPackageArgs, ref hasAutoReference);
                        projectModel.TargetFrameworkPackages = projectFrameworkPackages;
                        projectModel.AutoReferenceFound = hasAutoReference;
                    }
                    else
                    {
                        projectModel.TargetFrameworkPackages = new List<ListPackageReportFrameworkPackage>();
                    }
                }
            }
        }

        private static async Task GetVulnerabilitiesFromAuditSourcesAsync(
            ListPackageArgs listPackageArgs,
            ListPackageReportModel listPackageReportModel,
            ListPackageProjectModel projectModel,
            List<FrameworkPackages> frameworks)
        {
            List<IReadOnlyDictionary<string, IReadOnlyList<PackageVulnerabilityInfo>>> vulnerabilities = await GetVulnerabilityData(
                projectModel,
                listPackageReportModel,
                listPackageArgs.AuditSources,
                listPackageArgs.Logger,
                listPackageArgs.CancellationToken);

            foreach (var frameworkPackages in frameworks)
            {
                var frameworkPackage = new ListPackageReportFrameworkPackage(frameworkPackages.Framework)
                {
                    TransitivePackages = new List<ListReportPackage>(),
                    TopLevelPackages = new List<ListReportPackage>()
                };

                ProcessPackages(frameworkPackages.TopLevelPackages, vulnerabilities, frameworkPackage.TopLevelPackages);
                ProcessPackages(frameworkPackages.TransitivePackages, vulnerabilities, frameworkPackage.TransitivePackages);

                projectModel.TargetFrameworkPackages ??= new List<ListPackageReportFrameworkPackage>();
                projectModel.TargetFrameworkPackages.Add(frameworkPackage);
            }
        }

        private static void ProcessPackages(
            IEnumerable<InstalledPackageReference> packages,
            List<IReadOnlyDictionary<string, IReadOnlyList<PackageVulnerabilityInfo>>> vulnerabilities,
            List<ListReportPackage> reportPackages)
        {
            foreach (var package in packages)
            {
                var vuln = GetPackageVulnerabilities(
                    vulnerabilities,
                    package.Name,
                    package.ResolvedPackageMetadata.Identity.Version.ToNormalizedString()
                    ).ToList();

                if (vuln != null && vuln.Count > 0)
                {
                    reportPackages.Add(
                        new ListReportPackage(
                            package.Name,
                            package.OriginalRequestedVersion,
                            package.ResolvedPackageMetadata.Identity.Version.ToString(),
                            vuln));
                }
            }
        }

        private static bool IsProjectAssetsFileValid(string assetsPath, string projectPath, ListPackageProjectModel projectModel, out LockFile assetsFile)
        {
            assetsFile = null;

            if (!File.Exists(assetsPath))
            {
                projectModel.AddProjectInformation(ProblemType.Error,
                    string.Format(CultureInfo.CurrentCulture, Strings.Error_AssetsFileNotFound, projectPath));
                return false;
            }
            else
            {
                var lockFileFormat = new LockFileFormat();
                assetsFile = lockFileFormat.Read(assetsPath);

                // Assets file validation
                if (assetsFile.PackageSpec == null ||
                    assetsFile.Targets == null ||
                    assetsFile.Targets.Count == 0)
                {
                    projectModel.AddProjectInformation(ProblemType.Error,
                        string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_ErrorReadingAssetsFile, assetsPath));
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        private static async Task<List<IReadOnlyDictionary<string, IReadOnlyList<PackageVulnerabilityInfo>>>> GetVulnerabilityData(
            ListPackageProjectModel projectModel,
            ListPackageReportModel reportModel,
            IReadOnlyList<PackageSource> sources,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var vulnerabilityInfo = new List<IReadOnlyDictionary<string, IReadOnlyList<PackageVulnerabilityInfo>>>();

            foreach (var source in sources)
            {
                if (!await TryAddSourceVulnerabilityInfo(source, reportModel, logger, cancellationToken, vulnerabilityInfo))
                {
                    projectModel.AddProjectInformation(
                        ProblemType.Warning,
                        string.Format(CultureInfo.CurrentCulture, Strings.Warning_AuditSourceWithoutData, source.Name)
                    );
                }
            }

            return vulnerabilityInfo;
        }

        private static async Task<bool> TryAddSourceVulnerabilityInfo(
            PackageSource source,
            ListPackageReportModel reportModel,
            ILogger logger,
            CancellationToken cancellationToken,
            List<IReadOnlyDictionary<string, IReadOnlyList<PackageVulnerabilityInfo>>> vulnerabilityInfo)
        {
            var repository = Repository.Factory.GetCoreV3(source);
            var vulnerabilityProvider = new VulnerabilityInfoResourceV3Provider();
            var (isCreated, resource) = await vulnerabilityProvider.TryCreate(repository, cancellationToken);

            if (!isCreated || resource is not VulnerabilityInfoResourceV3 vulnerabilityResource)
            {
                return false;
            }

            reportModel.AuditSourcesUsed.Add(source);

            var vulnerabilityInfoResult = await vulnerabilityResource.GetVulnerabilityInfoAsync(
                new SourceCacheContext(),
                logger,
                cancellationToken
            );

            if (vulnerabilityInfoResult?.KnownVulnerabilities != null)
            {
                vulnerabilityInfo.AddRange(vulnerabilityInfoResult.KnownVulnerabilities);
            }

            return true;
        }

        private static IEnumerable<PackageVulnerabilityMetadata> GetPackageVulnerabilities(
            IEnumerable<IReadOnlyDictionary<string, IReadOnlyList<PackageVulnerabilityInfo>>> vulnerabilities,
            string id,
            string version)
        {
            if (vulnerabilities == null)
            {
                return Enumerable.Empty<PackageVulnerabilityMetadata>();
            }

            var parsedVersion = new NuGetVersion(version);
            foreach (var vulnFile in vulnerabilities)
            {
                if (vulnFile.TryGetValue(id, out IReadOnlyList<PackageVulnerabilityInfo> vulnPackages) && vulnPackages != null)
                {
                    return vulnPackages
                        .Where(package => package.Versions.Satisfies(parsedVersion))
                        .Select(v => new PackageVulnerabilityMetadata(v.Url, (int)v.Severity))
                        .ToList();
                }
            }

            return Enumerable.Empty<PackageVulnerabilityMetadata>();
        }

        public static bool FilterPackages(IEnumerable<FrameworkPackages> packages, ListPackageArgs listPackageArgs)
        {
            switch (listPackageArgs.ReportType)
            {
                case ReportType.Default: break; // No filtering in this case
                case ReportType.Outdated:
                    FilterPackages(
                        packages,
                        ListPackageHelper.TopLevelPackagesFilterForOutdated,
                        ListPackageHelper.TransitivePackagesFilterForOutdated);
                    break;
                case ReportType.Deprecated:
                    FilterPackages(
                        packages,
                        ListPackageHelper.PackagesFilterForDeprecated,
                        ListPackageHelper.PackagesFilterForDeprecated);
                    break;
                case ReportType.Vulnerable:
                    FilterPackages(
                        packages,
                        ListPackageHelper.PackagesFilterForVulnerable,
                        ListPackageHelper.PackagesFilterForVulnerable);
                    break;
            }

            return packages.Any(p => p.TopLevelPackages.Any() ||
                                     listPackageArgs.IncludeTransitive && p.TransitivePackages.Any());
        }

        /// <summary>
        /// Filters top-level and transitive packages.
        /// </summary>
        /// <param name="packages">The <see cref="FrameworkPackages"/> to filter.</param>
        /// <param name="topLevelPackagesFilter">The filter to be applied on all <see cref="FrameworkPackages.TopLevelPackages"/>.</param>
        /// <param name="transitivePackagesFilter">The filter to be applied on all <see cref="FrameworkPackages.TransitivePackages"/>.</param>
        private static void FilterPackages(
            IEnumerable<FrameworkPackages> packages,
            Func<InstalledPackageReference, bool> topLevelPackagesFilter,
            Func<InstalledPackageReference, bool> transitivePackagesFilter)
        {
            foreach (var frameworkPackages in packages)
            {
                frameworkPackages.TopLevelPackages = GetInstalledPackageReferencesWithFilter(
                    frameworkPackages.TopLevelPackages, topLevelPackagesFilter);

                frameworkPackages.TransitivePackages = GetInstalledPackageReferencesWithFilter(
                    frameworkPackages.TransitivePackages, transitivePackagesFilter);
            }
        }

        private static IEnumerable<InstalledPackageReference> GetInstalledPackageReferencesWithFilter(
            IEnumerable<InstalledPackageReference> references,
            Func<InstalledPackageReference, bool> filter)
        {
            var filteredReferences = new List<InstalledPackageReference>();
            foreach (var reference in references)
            {
                if (filter(reference))
                {
                    filteredReferences.Add(reference);
                }
            }

            return filteredReferences;
        }

        /// <summary>
        /// Get package metadata from all sources
        /// </summary>
        /// <param name="targetFrameworks">A <see cref="FrameworkPackages"/> per project target framework</param>
        /// <param name="listPackageArgs">List command args</param>
        /// <returns>A dictionary where the key is the package id, and the value is a list of <see cref="IPackageSearchMetadata"/>.</returns>
        private async Task<Dictionary<string, List<IPackageSearchMetadata>>> GetPackageMetadataAsync(
            List<FrameworkPackages> targetFrameworks,
            ListPackageArgs listPackageArgs)
        {
            List<string> allPackages = GetAllPackageIdentifiers(targetFrameworks, listPackageArgs.IncludeTransitive);
            var packageMetadataById = new Dictionary<string, List<IPackageSearchMetadata>>(capacity: allPackages.Count);

            int maxParallel = listPackageArgs.PackageSources.Any(s => s.IsHttp)
                ? 8 // Try to be nice to HTTP package sources
                : (Environment.ProcessorCount / listPackageArgs.PackageSources.Count) + 1;

            await ThrottledForEachAsync(allPackages,
                async (packageId, cancellationToken) => await GetPackageVersionsAsync(packageId, listPackageArgs, cancellationToken),
                packageMetadata => packageMetadataById[packageMetadata.Key] = packageMetadata.Value,
                maxParallel,
                listPackageArgs.CancellationToken);

            return packageMetadataById;

            static List<string> GetAllPackageIdentifiers(List<FrameworkPackages> frameworks, bool includeTransitive)
            {
                IEnumerable<InstalledPackageReference> intermediateEnumerable = frameworks.SelectMany(f => f.TopLevelPackages);
                if (includeTransitive)
                {
                    intermediateEnumerable = intermediateEnumerable.Concat(frameworks.SelectMany(f => f.TransitivePackages));
                }
                List<string> allPackages = intermediateEnumerable.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return allPackages;
            }
        }

        /// <summary>Run a throttled iteration of a list that performs async work, with a "single threaded" collection of results.</summary>
        /// <remarks>
        /// <para>The continuation delegate is called sequentially, so results can be safely added to non-synchronized collections.</para>
        /// <para>If any task factory invocation throws, or any task faults, the cancellation token will be triggered and the iteration will end early.</para>
        /// </remarks>
        /// <typeparam name="TItem">The item type for the input list</typeparam>
        /// <typeparam name="TResult">The result type of the async work</typeparam>
        /// <param name="items">The input list to iterate</param>
        /// <param name="taskFactory">Delegate to start async work.</param>
        /// <param name="continuation">Delegate with result of async work. Will not be called concurrently.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="maxParallel">The maximum number of tasks to allow running in parallel.</param>
        /// <returns>A task that can be awaited to wait for completion of the iteration.</returns>
        private async Task ThrottledForEachAsync<TItem, TResult>(
            IList<TItem> items,
            Func<TItem, CancellationToken, Task<TResult>> taskFactory,
            Action<TResult> continuation,
            int maxParallel,
            CancellationToken cancellationToken)
        {
            int taskCount = Math.Min(items.Count, maxParallel);
            var tasks = new Task<TResult>[taskCount];

            using CancellationTokenSource faultCancelationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // ramp up throttling (fill task array)
                int itemIndex;
                for (itemIndex = 0; itemIndex < taskCount; itemIndex++)
                {
                    tasks[itemIndex] = taskFactory(items[itemIndex], faultCancelationTokenSource.Token);
                }

                // throttling steady state (max parallel tasks running, more input items waiting to queue)
                while (itemIndex < items.Count)
                {
                    _ = await Task.WhenAny(tasks);
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        if (tasks[i].IsCompleted)
                        {
                            TResult result = await tasks[i];
                            continuation(result);

                            tasks[i] = taskFactory(items[itemIndex++], faultCancelationTokenSource.Token);
                            break;
                        }
                    }
                }

                // ramp down throttling (no more inputs waiting to start, just need to wait for last tasks to finish)
                await Task.WhenAll(tasks);
                for (int i = 0; i < tasks.Length; i++)
                {
                    TResult result = await tasks[i];
                    continuation(result);
                }
            }
            catch
            {
                // Don't leave un-awaited tasks. Request cancellation, then wait for tasks to finish.
                faultCancelationTokenSource.Cancel();

                // Make sure none of the tasks are null (factory exception during ramp-up)
                for (int i = 0; i < tasks.Length; i++)
                {
                    if (tasks[i] is null)
                    {
                        tasks[i] = Task.FromResult(default(TResult));
                    }
                }

                await Task.WhenAll(tasks);
                throw;
            }
            finally
            {
                faultCancelationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// Pre-populate _sourceRepositoryCache so source repository can be reused between different calls.
        /// </summary>
        /// <param name="listPackageArgs">List args for the token and source provider</param>
        private void PopulateSourceRepositoryCache(ListPackageArgs listPackageArgs)
        {
            IEnumerable<Lazy<INuGetResourceProvider>> providers = Repository.Provider.GetCoreV3();
            IEnumerable<PackageSource> sources = listPackageArgs.PackageSources;
            foreach (PackageSource source in sources)
            {
                SourceRepository sourceRepository = Repository.CreateSource(providers, source, FeedType.Undefined);
                _sourceRepositoryCache[source] = sourceRepository;
            }
        }

        /// <summary>
        /// Get last versions for every package from the unique packages
        /// </summary>
        /// <param name="frameworks"> List of <see cref="FrameworkPackages"/>.</param>
        /// <param name="packageMetadata">Package metadata from package sources</param>
        /// <param name="listPackageArgs">Arguments for list package to get the right latest version</param>
        internal async Task UpdatePackagesWithSourceMetadata(
            List<FrameworkPackages> frameworks,
            Dictionary<string, List<IPackageSearchMetadata>> packageMetadata,
            ListPackageArgs listPackageArgs)
        {
            foreach (var frameworkPackages in frameworks)
            {
                foreach (var topLevelPackage in frameworkPackages.TopLevelPackages)
                {
                    if (packageMetadata.TryGetValue(topLevelPackage.Name, out List<IPackageSearchMetadata> matchingPackage))
                    {
                        // Get latest metadata *only* if this is a report requiring "outdated" metadata
                        if (listPackageArgs.ReportType == ReportType.Outdated && matchingPackage.Count > 0)
                        {
                            var latestVersion = matchingPackage.Where(newVersion => MeetsConstraints(newVersion.Identity.Version, topLevelPackage, listPackageArgs)).Max(i => i.Identity.Version);

                            if (latestVersion is not null)
                            {
                                topLevelPackage.LatestPackageMetadata = matchingPackage.First(p => p.Identity.Version == latestVersion);
                                topLevelPackage.UpdateLevel = GetUpdateLevel(topLevelPackage.ResolvedPackageMetadata.Identity.Version, topLevelPackage.LatestPackageMetadata.Identity.Version);
                            }
                            else // no latest version available with the given constraints
                            {
                                topLevelPackage.LatestPackageMetadata = null;
                                topLevelPackage.UpdateLevel = UpdateLevel.NoUpdate;
                            }
                        }

                        var matchingPackagesWithDeprecationMetadata = await Task.WhenAll(
                            matchingPackage.Select(async v => new { SearchMetadata = v, DeprecationMetadata = await v.GetDeprecationMetadataAsync() }));

                        // Update resolved version with additional metadata information returned by the server.
                        var resolvedVersionFromServer = matchingPackagesWithDeprecationMetadata
                            .FirstOrDefault(v => v.SearchMetadata.Identity.Version == topLevelPackage.ResolvedPackageMetadata.Identity.Version &&
                                    (v.DeprecationMetadata != null || v.SearchMetadata?.Vulnerabilities != null));

                        if (resolvedVersionFromServer != null)
                        {
                            topLevelPackage.ResolvedPackageMetadata = resolvedVersionFromServer.SearchMetadata;
                        }
                    }
                }

                foreach (var transitivePackage in frameworkPackages.TransitivePackages)
                {
                    if (packageMetadata.TryGetValue(transitivePackage.Name, out List<IPackageSearchMetadata> matchingPackage))
                    {
                        // Get latest metadata *only* if this is a report requiring "outdated" metadata
                        if (listPackageArgs.ReportType == ReportType.Outdated && matchingPackage.Count > 0)
                        {
                            var latestVersion = matchingPackage.Where(newVersion => MeetsConstraints(newVersion.Identity.Version, transitivePackage, listPackageArgs)).Max(i => i.Identity.Version);

                            transitivePackage.LatestPackageMetadata = matchingPackage.First(p => p.Identity.Version == latestVersion);
                            transitivePackage.UpdateLevel = GetUpdateLevel(transitivePackage.ResolvedPackageMetadata.Identity.Version, transitivePackage.LatestPackageMetadata.Identity.Version);
                        }

                        var matchingPackagesWithDeprecationMetadata = await Task.WhenAll(
                            matchingPackage.Select(async v => new { SearchMetadata = v, DeprecationMetadata = await v.GetDeprecationMetadataAsync() }));

                        // Update resolved version with additional metadata information returned by the server.
                        var resolvedVersionFromServer = matchingPackagesWithDeprecationMetadata
                            .FirstOrDefault(v => v.SearchMetadata.Identity.Version == transitivePackage.ResolvedPackageMetadata.Identity.Version &&
                                    (v.DeprecationMetadata != null || v.SearchMetadata?.Vulnerabilities != null));

                        if (resolvedVersionFromServer != null)
                        {
                            transitivePackage.ResolvedPackageMetadata = resolvedVersionFromServer.SearchMetadata;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update Level is used to determine the print color for the latest
        /// version, which depends on changing major, minor or patch
        /// </summary>
        /// <param name="resolvedVersion"> Package's resolved version </param>
        /// <param name="latestVersion"> Package's latest version </param>
        /// <returns></returns>
        private UpdateLevel GetUpdateLevel(NuGetVersion resolvedVersion, NuGetVersion latestVersion)
        {
            if (latestVersion == null) return UpdateLevel.NoUpdate;
            if (resolvedVersion.Major != latestVersion.Major)
            {
                return UpdateLevel.Major;
            }
            else if (resolvedVersion.Minor != latestVersion.Minor)
            {
                return UpdateLevel.Minor;
            }
            //Patch or less important version props are different
            else if (resolvedVersion != latestVersion)
            {
                return UpdateLevel.Patch;
            }
            return UpdateLevel.NoUpdate;
        }

        /// <summary>
        /// Prepares the calls to sources for latest versions and updates
        /// the list of tasks with the requests
        /// </summary>
        /// <param name="package">The package to get the latest version for</param>
        /// <param name="listPackageArgs">List args for the token and source provider></param>
        /// <param name="cancellationToken"></param>
        /// <returns>A list of tasks for all latest versions for packages from all sources</returns>
        private async Task<KeyValuePair<string, List<IPackageSearchMetadata>>> GetPackageVersionsAsync(
            string package,
            ListPackageArgs listPackageArgs,
            CancellationToken cancellationToken)
        {
            var results = new List<IPackageSearchMetadata>();
            var sources = listPackageArgs.PackageSources;

            await ThrottledForEachAsync(sources,
                async (source, innerCancellationToken) => await GetLatestVersionPerSourceAsync(source, listPackageArgs, package, innerCancellationToken),
                continuation: results.AddRange,
                maxParallel: listPackageArgs.PackageSources.Count,
                cancellationToken);

            return new KeyValuePair<string, List<IPackageSearchMetadata>>(package, results);
        }

        /// <summary>
        /// Gets the highest version of a package from a specific source
        /// </summary>
        /// <param name="packageSource">The source to look for packages at</param>
        /// <param name="listPackageArgs">The list args for the cancellation token</param>
        /// <param name="package">Package to look for updates for</param>
        /// <param name="cancellationToken"></param>
        /// <returns>An updated package with the highest version at a single source</returns>
        private async Task<IEnumerable<IPackageSearchMetadata>> GetLatestVersionPerSourceAsync(
            PackageSource packageSource,
            ListPackageArgs listPackageArgs,
            string package,
            CancellationToken cancellationToken)
        {
            SourceRepository sourceRepository = _sourceRepositoryCache[packageSource];
            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

            using var sourceCacheContext = new SourceCacheContext();
            IEnumerable<IPackageSearchMetadata> packages =
                await packageMetadataResource.GetMetadataAsync(
                    package,
                    includePrerelease: listPackageArgs.Prerelease,
                    includeUnlisted: false,
                    sourceCacheContext: sourceCacheContext,
                    log: listPackageArgs.Logger,
                    token: listPackageArgs.CancellationToken);

            return packages;
        }

        /// <summary>
        /// Given a found version from a source and the current version and the args
        /// of list package, this function checks if the found version meets the required
        /// highest-patch, highest-minor or prerelease
        /// </summary>
        /// <param name="newVersion">Version from a source</param>
        /// <param name="package">The required package with its current version</param>
        /// <param name="listPackageArgs">Used to get the constraints</param>
        /// <returns>Whether the new version meets the constraints or not</returns>
        private bool MeetsConstraints(NuGetVersion newVersion, InstalledPackageReference package, ListPackageArgs listPackageArgs)
        {
            var result = !newVersion.IsPrerelease || listPackageArgs.Prerelease || package.ResolvedPackageMetadata.Identity.Version.IsPrerelease;

            if (listPackageArgs.HighestPatch)
            {
                result = newVersion.Minor.Equals(package.ResolvedPackageMetadata.Identity.Version.Minor) && newVersion.Major.Equals(package.ResolvedPackageMetadata.Identity.Version.Major) && result;
            }

            if (listPackageArgs.HighestMinor)
            {
                result = newVersion.Major.Equals(package.ResolvedPackageMetadata.Identity.Version.Major) && result;
            }

            return result;
        }
    }
}
