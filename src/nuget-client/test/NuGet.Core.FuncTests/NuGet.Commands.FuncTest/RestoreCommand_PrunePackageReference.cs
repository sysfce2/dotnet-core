// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NuGet.Commands.Test;
using NuGet.Common;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Test.Utility;
using NuGet.Versioning;
using Xunit;

namespace NuGet.Commands.FuncTest
{
    [Collection(TestCollection.Name)]
    public class RestoreCommand_PrunePackageReference
    {
        // P -> A 1.0.0 -> B 1.0.0
        // Prune B 1.0.0
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_PrunesTransitivesDependencies_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";


            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);
        }

        // P -> A 1.0.0 -> B 1.0.0
        //              -> C 1.0.0
        //              -> D 1.0.0
        // Prune C 1.0.0
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferencesAndManyDependencies_PrunesTransitiveDependencies_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("A", "1.0.0")
            {
                Dependencies = [
                    new SimpleTestPackageContext("B", "1.0.0"),
                    new SimpleTestPackageContext("C", "1.0.0"),
                    new SimpleTestPackageContext("D", "1.0.0")
                    ]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""C"" : ""(,1.0.0]"" 
                }
            }
          }
        }";


            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("D");
            result.LockFile.Targets[0].Libraries[2].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().BeEmpty();

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        // P -> A 1.0.0 -> B 1.0.0
        // P -> C 1.0.0
        // Do not Prune C 1.0.0
        [Theory]
        [InlineData("net10.0", "10.0.100", true, true)]
        [InlineData("net10.0", "9.0.100", true, false)]
        [InlineData("net10.0", "", false, true)]
        [InlineData("net472", "", false, false)]
        [InlineData("net472", "10.0.100", false, false)]
        public async Task RestoreCommand_WithPrunePackageReferences_DoesNotPruneDirectDependencies(string framework, string sdkAnalysisLevel, bool usingMicrosoftNETSdk, bool shouldWarn)
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            await SetupCommonPackagesAsync(pathContext);

            var rootProject = @"
        {
          ""frameworks"": {
            ""FRAMEWORK"": {
                ""targetAlias"": ""targetAlias"",
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""C"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""C"" : ""(,1.0.0]"" 
                }
            }
          }
        }".Replace("FRAMEWORK", framework);

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.SdkAnalysisLevel = !string.IsNullOrEmpty(sdkAnalysisLevel) ? NuGetVersion.Parse(sdkAnalysisLevel) : null;
            projectSpec.RestoreMetadata.UsingMicrosoftNETSdk = usingMicrosoftNETSdk;

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);

            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("C");
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[0].Libraries[2].CompileTimeAssemblies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].CompileTimeAssemblies[0].Path.Should().EndWith("_._");
            result.LockFile.Targets[0].Libraries[2].RuntimeAssemblies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].RuntimeAssemblies[0].Path.Should().EndWith("_._");
            result.LockFile.Targets[0].Libraries[2].FrameworkAssemblies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[2].FrameworkReferences.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[2].NativeLibraries.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[2].ResourceAssemblies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[2].RuntimeTargets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].RuntimeTargets[0].Path.Should().EndWith("_._");
            result.LockFile.Targets[0].Libraries[2].ContentFiles.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].ContentFiles[0].Path.Should().EndWith("_._");

            if (shouldWarn)
            {
                result.LockFile.LogMessages.Should().HaveCount(1);
                result.LockFile.LogMessages[0].Code.Should().Be(NuGetLogCode.NU1510);
                result.LockFile.LogMessages[0].LibraryId.Should().Be("C");
                result.LockFile.LogMessages[0].TargetGraphs.Should().HaveCount(1);
                result.LockFile.LogMessages[0].TargetGraphs[0].Should().Be(framework);
            }
            else
            {
                result.LockFile.LogMessages.Should().BeEmpty();
            }
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        // P -> P2 -> B 1.0.0 -> C 1.0.0
        // P -> A 1.0.0
        // Prune B 1.0.0
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_PrunesTransitivesDependenciesThroughProjects_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0");
            var packageB = new SimpleTestPackageContext("packageB", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageC", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA,
                packageB);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";
            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            var projectSpec2 = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec, projectSpec2);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);
        }

        // P -> A 1.0.0 -> B 1.0.0
        //              -> C 1.0.0
        //              -> D 1.0.0 -> E 2.0.0
        //              -> F 1.0.0 -> G 2.0.0
        // P -> F 2.0.0 -> G 3.0.0
        // P -> H 1.0.0 -> B 2.0.0
        //
        // Prune C 1.0.0, D 1.0.0, G 2.0.0, B 1.5.0
        // Leaves: A 1.0.0, B 2.0.0, F 2.0.0, G 3.0.0, H 1.0.0
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_PrunesManyDependenciesFromSinglePackage_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                 new SimpleTestPackageContext("A", "1.0.0")
                 {
                     Dependencies = [
                         new SimpleTestPackageContext("B", "1.0.0"),
                         new SimpleTestPackageContext("C", "1.0.0"),
                         new SimpleTestPackageContext("D", "1.0.0")
                         {
                             Dependencies = [
                                 new SimpleTestPackageContext("E", "2.0.0"),
                             ]
                         },
                         new SimpleTestPackageContext("F", "1.0.0")
                         {
                             Dependencies = [
                                 new SimpleTestPackageContext("G", "2.0.0"),
                             ]
                         }
                     ]
                 },
                 new SimpleTestPackageContext("F", "2.0.0")
                 {
                     Dependencies = [
                         new SimpleTestPackageContext("G", "3.0.0"),
                     ]
                 },
                 new SimpleTestPackageContext("H", "1.0.0")
                 {
                     Dependencies = [
                         new SimpleTestPackageContext("B", "2.0.0"),
                     ]
                 });

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""F"": {
                            ""version"": ""[2.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""H"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""C"" : ""(,1.0.0]"",
                    ""D"" : ""(,1.0.0]"",
                    ""G"" : ""(,2.0.0]"",
                    ""B"" : ""(,1.5.0]""
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(5);

            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Dependencies[0].Id.Should().Be("F");

            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("2.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();

            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("F");
            result.LockFile.Targets[0].Libraries[2].Version.Should().Be(new NuGetVersion("2.0.0"));
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].Dependencies[0].Id.Should().Be("G");

            result.LockFile.Targets[0].Libraries[3].Name.Should().Be("G");
            result.LockFile.Targets[0].Libraries[3].Version.Should().Be(new NuGetVersion("3.0.0"));
            result.LockFile.Targets[0].Libraries[3].Dependencies.Should().BeEmpty();

            result.LockFile.Targets[0].Libraries[4].Name.Should().Be("H");
            result.LockFile.Targets[0].Libraries[4].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[4].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[4].Dependencies[0].Id.Should().Be("B");
        }

        // P -> A 1.0.0 -> B 1.0.0
        // Prune B 1.0.0
        [Fact]
        public async Task RestoreCommand_WithMultiTargetedPrunePackageReferences_PrunesTransitivesDependencies_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            },
            ""net48"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }";


            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.LockFile.Targets.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[1].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("packageB");
            result.LockFile.Targets[1].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(2);
        }

        // P1 -> A 1.0.0 -> B 1.0.0
        // P1 -> P2 (project)
        // Prune B 1.0.0
        [Theory]
        [InlineData("net10.0", "10.0.100", true, true)]
        [InlineData("net10.0", "9.0.100", true, false)]
        [InlineData("net10.0", "", false, true)]
        [InlineData("net472", "", false, false)]
        [InlineData("net472", "10.0.100", false, false)]
        public async Task RestoreCommand_WithDirectProjectReferenceSpecifiedForPruning_SkipsPruning(string framework, string sdkAnalysisLevel, bool usingMicrosoftNETSdk, bool shouldWarn)
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""FRAMEWORK"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"",
                    ""Project2"" : ""(,3.0.0]"" 
                }
            }
          }
        }".Replace("FRAMEWORK", framework);

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.SdkAnalysisLevel = !string.IsNullOrEmpty(sdkAnalysisLevel) ? NuGetVersion.Parse(sdkAnalysisLevel) : null;
            projectSpec.RestoreMetadata.UsingMicrosoftNETSdk = usingMicrosoftNETSdk;
            var projectSpec2 = ProjectTestHelpers.GetPackageSpec("Project2", framework);

            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec, projectSpec2);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
            if (shouldWarn)
            {
                result.LockFile.LogMessages.Should().HaveCount(1);
                result.LockFile.LogMessages[0].Code.Should().Be(NuGetLogCode.NU1511);
                result.LockFile.LogMessages[0].LibraryId.Should().Be("Project2");
                result.LockFile.LogMessages[0].TargetGraphs.Should().HaveCount(1);
                result.LockFile.LogMessages[0].TargetGraphs[0].Should().Be(framework);
            }
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);
        }

        // P1 -> A 1.0.0 -> B 1.0.0
        // P1 -> P2 (project) -> P3 (project)
        // Prune B 1.0.0
        [Fact]
        public async Task RestoreCommand_WithTransitiveProjectReferenceSpecifiedForPruning_SkipsPruningAndDoesNotWarn_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"",
                    ""Project3"" : ""(,3.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.SdkAnalysisLevel = NuGetVersion.Parse("10.0.100");
            projectSpec.RestoreMetadata.UsingMicrosoftNETSdk = true;
            var projectSpec2 = ProjectTestHelpers.GetPackageSpec("Project2", framework: "net472");
            var projectSpec3 = ProjectTestHelpers.GetPackageSpec("Project3", framework: "net472");

            projectSpec2 = projectSpec2.WithTestProjectReference(projectSpec3);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec, projectSpec2, projectSpec3);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("Project3");
            result.LockFile.Targets[0].Libraries[2].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().BeEmpty();
            result.LockFile.LogMessages.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);
        }

        // P -> A 1.0.0 -> B 1.0.0
        // Prune B 1.0.0
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_AndMissingVersion_PrunesTransitivesDependencies_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };
            var packageB200 = new SimpleTestPackageContext("packageB", "2.0.0");

            await SimpleTestPackageUtility.CreatePackagesWithoutDependenciesAsync(
                pathContext.PackageSource,
                packageA,
                packageB200);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            },
            ""net48"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.LockFile.Targets.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[1].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("packageB");
            result.LockFile.Targets[1].Libraries[1].Version.Should().Be(new NuGetVersion("2.0.0"));
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(2);
        }

        // P -> A 1.0.0 -> B (, 2.0.0]
        // Prune B 1.0.0
        // It prunes B 1.0.0, because the missing lower bound means min version. 
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_AndMissingLowerBoundVersion_PrunesTransitivesDependencies_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "(, 2.0.0]")]
            };
            var packageB = new SimpleTestPackageContext("packageB", "1.0.0");

            await SimpleTestPackageUtility.CreatePackagesWithoutDependenciesAsync(
                pathContext.PackageSource,
                packageA,
                packageB);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";


            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);
        }

        // P -> A 1.0.0 -> B 1.0.0
        //              -> (win) runtime.a 1.0.0
        //              -> (win) runtime.a.win 1.0.0
        // Prune runtime.A 1.0.0
        // Leaves B and runtime.a.win

        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_PrunesRuntimeDependencies_AndVerifiesEquivalency()
        {
            // Arrange
            using var pathContext = new SimpleTestPathContext();

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                new SimpleTestPackageContext("a", "1.0.0")
                {
                    Dependencies = [new SimpleTestPackageContext("b", "1.0.0")],
                    RuntimeJson = @"{
                  ""runtimes"": {
                    ""win"": {
                            ""a"": {
                                ""runtime.a"": ""1.0.0"",
                                ""runtime.a.win"": ""1.0.0""
                            }
                          }
                        }
                  }"
                },
                new SimpleTestPackageContext("runtime.a", "1.0.0"),
                new SimpleTestPackageContext("runtime.a.win", "1.0.0"));
            var rootProject = @"
        {
          ""runtimes"": {
                        ""win"": {}
                  },
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""a"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""runtime.A"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.Success.Should().BeTrue(because: string.Join(Environment.NewLine, result.LogMessages.Select(e => e.Message)));
            result.LockFile.Targets.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[1].Libraries.Should().HaveCount(3);

            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("a");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("a");
            result.LockFile.Targets[1].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("b");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("b");
            result.LockFile.Targets[1].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[1].Libraries[2].Name.Should().Be("runtime.a.win");
            result.LockFile.Targets[1].Libraries[2].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[2].Dependencies.Should().BeEmpty();

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        // P -> A 1.0.0 -> (win) runtime.a 1.0.0 -> runtime.a.core 1.0.0
        //                                       -> runtime.a.extensions 1.0.0
        // Prune runtime.a.core 1.0.0
        // Leaves A, runtime.a and runtime.a.extensions

        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_PrunesTransitiveRuntimeDependencies_AndVerifiesEquivalency()
        {
            // Arrange
            using var pathContext = new SimpleTestPathContext();

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                new SimpleTestPackageContext("a", "1.0.0")
                {
                    RuntimeJson = @"{
                  ""runtimes"": {
                    ""win"": {
                            ""a"": {
                                ""runtime.a"": ""1.0.0"",
                            }
                          }
                        }
                  }"
                },
                new SimpleTestPackageContext("runtime.a", "1.0.0")
                {
                    Dependencies = [new SimpleTestPackageContext("runtime.a.core", "1.0.0"), new SimpleTestPackageContext("runtime.a.extensions", "1.0.0")]
                }
                );
            var rootProject = @"
        {
          ""runtimes"": {
                        ""win"": {}
                  },
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""a"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""runtime.a.core"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.Success.Should().BeTrue(because: string.Join(Environment.NewLine, result.LogMessages.Select(e => e.Message)));
            result.LockFile.Targets.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries.Should().HaveCount(3);

            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("a");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();

            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("a");
            result.LockFile.Targets[1].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("runtime.a");
            result.LockFile.Targets[1].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[2].Name.Should().Be("runtime.a.extensions");
            result.LockFile.Targets[1].Libraries[2].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[2].Dependencies.Should().BeEmpty();

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        // P -> A 1.0.0 -> B 1.0.0
        //              -> (win) runtime.a 1.0.0
        //              -> (win) runtime.a.win 1.0.0
        // Prune runtime.A 1.0.0, B 1.0.0
        // Leaves only runtime.a.win
        [Fact]
        public async Task RestoreCommand_WithPrunePackageReferences_PrunesBothTypesOfDependencies_AndVerifiesEquivalency()
        {
            // Arrange
            using var pathContext = new SimpleTestPathContext();

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                new SimpleTestPackageContext("a", "1.0.0")
                {
                    Dependencies = [new SimpleTestPackageContext("b", "1.0.0")],
                    RuntimeJson = @"{
                  ""runtimes"": {
                    ""win"": {
                            ""a"": {
                                ""runtime.a"": ""1.0.0"",
                                ""runtime.a.win"": ""1.0.0""
                            }
                          }
                        }
                  }"
                },
                new SimpleTestPackageContext("runtime.a", "1.0.0"),
                new SimpleTestPackageContext("runtime.a.win", "1.0.0"));
            var rootProject = @"
        {
          ""runtimes"": {
                        ""win"": {}
                  },
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""a"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""runtime.A"" : ""(,1.0.0]"",
                    ""B"" : ""(,3.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);
            result.Success.Should().BeTrue(because: string.Join(Environment.NewLine, result.LogMessages.Select(e => e.Message)));
            result.LockFile.Targets.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries.Should().HaveCount(2);

            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("a");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("a");
            result.LockFile.Targets[1].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("runtime.a.win");
            result.LockFile.Targets[1].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().BeEmpty();

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(2);
        }

        // P1 -> A 1.0.0 -> ProjectAndPackage 1.0.0
        // P1 -> P2 (project) -> ProjectAndPackage (project)
        [Fact]
        public async Task RestoreCommand_WithTransitiveProjectReferenceSpecifiedForPruningCoalescingWithPackageReference_SkipsPruning_AndVerifiesEquivalency()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("ProjectAndPackage", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""ProjectAndPackage"" : ""(,3.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.SdkAnalysisLevel = !string.IsNullOrEmpty("10.0.100") ? NuGetVersion.Parse("10.0.100") : null;
            projectSpec.RestoreMetadata.UsingMicrosoftNETSdk = true;
            var projectSpec2 = ProjectTestHelpers.GetPackageSpec("Project2", framework: "net472");
            var projectSpec3 = ProjectTestHelpers.GetPackageSpec("ProjectAndPackage", framework: "net472");

            projectSpec2 = projectSpec2.WithTestProjectReference(projectSpec3);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec, projectSpec2, projectSpec3);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            result.LockFile.Targets[0].Libraries[1].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("ProjectAndPackage");
            result.LockFile.Targets[0].Libraries[2].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().BeEmpty();

            result.LockFile.LogMessages.Should().BeEmpty();

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);
        }

        // P -> A 1.0.0 -> B 1.0.0
        // P -> C 1.0.0
        // Prune C for one framework, but not the other
        [Theory]
        [InlineData("\"packagesToPrune\": { \"C\" : \"(,0.5.0]\" }")]
        [InlineData("")]
        public async Task RestoreCommand_WithMultiTargeting_AndPartialPruning_DoesNotWarn(string pruningString)
        {
            using var pathContext = new SimpleTestPathContext();
            await SetupCommonPackagesAsync(pathContext);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net10.0"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""C"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""C"" : ""(,1.0.0]"" 
                }
            },
            ""net472"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""C"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                PRUNING_STRING
            }
          }
        }".Replace("PRUNING_STRING", pruningString);

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);

            result.LockFile.Targets.Should().HaveCount(2);

            // verify net472
            result.LockFile.Targets[0].TargetFramework.GetShortFolderName().Should().Be("net472");
            result.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("C");
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(0);

            // verify net10.0
            result.LockFile.Targets[1].TargetFramework.GetShortFolderName().Should().Be("net10.0");
            result.LockFile.Targets[1].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[1].Libraries[2].Name.Should().Be("C");
            result.LockFile.Targets[1].Libraries[2].Dependencies.Should().HaveCount(0);

            result.LockFile.LogMessages.Should().BeEmpty();

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        [Theory]
        [InlineData("\"packagesToPrune\": { \"C\" : \"(,1.0.0]\" }", true)]
        [InlineData("", false)]
        public async Task RestoreCommand_WithPartialMultiTargeting_WhenPruning_Warns(string pruningString, bool warns)
        {
            using var pathContext = new SimpleTestPathContext();
            await SetupCommonPackagesAsync(pathContext);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net10.0"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""C"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                PRUNING_STRING
            },
            ""net472"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }".Replace("PRUNING_STRING", pruningString);

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);

            result.LockFile.Targets.Should().HaveCount(2);
            // verify net472
            result.LockFile.Targets[0].TargetFramework.GetShortFolderName().Should().Be("net472");
            result.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(0);

            // verify net10.0
            result.LockFile.Targets[1].TargetFramework.GetShortFolderName().Should().Be("net10.0");
            result.LockFile.Targets[1].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[1].Libraries[2].Name.Should().Be("C");
            result.LockFile.Targets[1].Libraries[2].Dependencies.Should().HaveCount(0);

            // Verify log messages
            if (warns)
            {
                result.LockFile.LogMessages.Should().HaveCount(1);
                result.LockFile.LogMessages[0].Code.Should().Be(NuGetLogCode.NU1510);
                result.LockFile.LogMessages[0].LibraryId.Should().Be("C");
                result.LockFile.LogMessages[0].TargetGraphs.Should().HaveCount(1);
                result.LockFile.LogMessages[0].TargetGraphs[0].Should().Be("net10.0");
            }
            else
            {
                result.LockFile.LogMessages.Should().BeEmpty();
            }

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        [Fact]
        public async Task RestoreCommand_WithMultiTargeting_WhenPruningInEveryFramework_Warns()
        {
            using var pathContext = new SimpleTestPathContext();
            await SetupCommonPackagesAsync(pathContext);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net10.0"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""C"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""C"" : ""(,1.0.0]"" 
                }
            },
            ""net472"": {
                ""dependencies"": {
                        ""A"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""C"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""C"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);

            // Act & Assert
            var result = await RunRestoreAsync(pathContext, projectSpec);

            result.LockFile.Targets.Should().HaveCount(2);
            // verify net472
            result.LockFile.Targets[0].TargetFramework.GetShortFolderName().Should().Be("net472");
            result.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[0].Libraries[2].Name.Should().Be("C");
            result.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(0);

            // verify net10.0
            result.LockFile.Targets[1].TargetFramework.GetShortFolderName().Should().Be("net10.0");
            result.LockFile.Targets[1].Libraries.Should().HaveCount(3);
            result.LockFile.Targets[1].Libraries[0].Name.Should().Be("A");
            result.LockFile.Targets[1].Libraries[0].Dependencies.Should().HaveCount(1);
            result.LockFile.Targets[1].Libraries[1].Name.Should().Be("B");
            result.LockFile.Targets[1].Libraries[1].Dependencies.Should().HaveCount(0);
            result.LockFile.Targets[1].Libraries[2].Name.Should().Be("C");
            result.LockFile.Targets[1].Libraries[2].Dependencies.Should().HaveCount(0);

            // Verify log messages
            result.LockFile.LogMessages.Should().HaveCount(1);
            result.LockFile.LogMessages[0].Code.Should().Be(NuGetLogCode.NU1510);
            result.LockFile.LogMessages[0].LibraryId.Should().Be("C");
            result.LockFile.LogMessages[0].TargetGraphs.Should().HaveCount(2);
            result.LockFile.LogMessages[0].TargetGraphs.Should().Contain("net10.0");
            result.LockFile.LogMessages[0].TargetGraphs.Should().Contain(".NETFramework,Version=v4.7.2");

            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(3);
        }

        [Theory]
        [InlineData("net472", false)]
        [InlineData("net9.0", false)]
        [InlineData("net10.0", true)]
        [InlineData("net10.0-windows", true)]
        [InlineData("net12.0", true)]
        public void AnalyzePruningResults_WithVariousFrameworks_WarnsForNET10OrNewerOnly(string framework, bool shouldWarn)
        {
            var rootProject = @"
                {
                  ""frameworks"": {
                    ""FRAMEWORK"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""a"" : ""(,1.0.0]"" 
                        }
                    }
                  }
                }".Replace("FRAMEWORK", framework);

            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", Path.GetTempPath(), rootProject);
            var testLogger = new TestLogger();
            var testEvent = new TelemetryEvent("dummyEvent");

            RestoreCommand.AnalyzePruningResults(projectSpec, testEvent, testLogger);

            if (shouldWarn)
            {
                testLogger.WarningMessages.Should().HaveCount(1);
                var restoreLogMessage = (RestoreLogMessage)testLogger.LogMessages.Single();
                restoreLogMessage.Code.Should().Be(NuGetLogCode.NU1510);
                restoreLogMessage.LibraryId.Should().Be("A");
            }
            else
            {
                testLogger.WarningMessages.Should().BeEmpty();
            }
            testEvent["Pruning.RemovablePackages.Count"].Should().Be(1);
            testEvent["Pruning.Pruned.Direct.Count"].Should().Be(1);
        }

        [Theory]
        [InlineData("9.0.100", false)]
        [InlineData("10.0.100", true)]
        public void AnalyzePruningResults_WithSDKAnalysisLevel_WarnsFor10OrNewerOnly(string sdkAnalysisLevel, bool shouldWarn)
        {
            var rootProject = @"
                {
                  ""frameworks"": {
                    ""net10.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""A"" : ""(,1.0.0]"" 
                        }
                    }
                  }
                }";

            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", Path.GetTempPath(), rootProject);
            projectSpec.RestoreMetadata.SdkAnalysisLevel = !string.IsNullOrEmpty(sdkAnalysisLevel) ? NuGetVersion.Parse(sdkAnalysisLevel) : null;
            projectSpec.RestoreMetadata.UsingMicrosoftNETSdk = true;
            var testLogger = new TestLogger();
            var testEvent = new TelemetryEvent("dummyEvent");

            RestoreCommand.AnalyzePruningResults(projectSpec, testEvent, testLogger);

            if (shouldWarn)
            {
                testLogger.WarningMessages.Should().HaveCount(1);
                var restoreLogMessage = (RestoreLogMessage)testLogger.LogMessages.Single();
                restoreLogMessage.Code.Should().Be(NuGetLogCode.NU1510);
                restoreLogMessage.LibraryId.Should().Be("A");
            }
            else
            {
                testLogger.WarningMessages.Should().BeEmpty();
            }
            testEvent["Pruning.RemovablePackages.Count"].Should().Be(1);
            testEvent["Pruning.Pruned.Direct.Count"].Should().Be(1);
        }

        [Theory]
        [InlineData("net9.0", false)]
        [InlineData("net10.0", true)]
        public void AnalyzePruningResults_WithMultiTargetingProjectAndVariousCasings_WarnsFor10OrNewerOnly(string framework, bool shouldWarn)
        {
            var rootProject = @"
                {
                  ""frameworks"": {
                    ""FRAMEWORK"": {
                        ""dependencies"": {
                                ""a"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                                ""B"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""A"" : ""(,2.0.0]"",
                            ""B"" : ""(,2.0.0]"" 
                        }
                    },
                    ""net8.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                                ""B"": {
                                    ""version"": ""[3.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""a"" : ""(,2.0.0]"",
                            ""B"" : ""(,2.0.0]"" 
                        }
                    }
                  }
                }".Replace("FRAMEWORK", framework);

            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", Path.GetTempPath(), rootProject);
            var testLogger = new TestLogger();
            var testEvent = new TelemetryEvent("dummyEvent");

            RestoreCommand.AnalyzePruningResults(projectSpec, testEvent, testLogger);

            if (shouldWarn)
            {
                testLogger.WarningMessages.Should().HaveCount(1);
                var restoreLogMessage = (RestoreLogMessage)testLogger.LogMessages.Single();
                restoreLogMessage.Code.Should().Be(NuGetLogCode.NU1510);
                restoreLogMessage.LibraryId.Should().Be("a");
            }
            else
            {
                testLogger.WarningMessages.Should().BeEmpty();
                testEvent.Count.Should().Be(2);
            }
            testEvent["Pruning.RemovablePackages.Count"].Should().Be(1);
            testEvent["Pruning.Pruned.Direct.Count"].Should().Be(2);
        }

        [Fact]
        public void AnalyzePruningResults_WithMultiTargetingProjectAndVariousVersions_WarnsForCompletelyPrunedPackagesOnly()
        {
            var rootProject = @"
                {
                  ""frameworks"": {
                    ""net10.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                                ""B"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""B"" : ""(,2.0.0]"" 
                        }
                    },
                    ""net8.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                                ""B"": {
                                    ""version"": ""[3.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""B"" : ""(,3.0.0]"" 
                        }
                    }
                  }
                }";

            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", Path.GetTempPath(), rootProject);
            var testLogger = new TestLogger();
            var testEvent = new TelemetryEvent("dummyEvent");

            RestoreCommand.AnalyzePruningResults(projectSpec, testEvent, testLogger);


            testLogger.WarningMessages.Should().HaveCount(1);
            var restoreLogMessage = (RestoreLogMessage)testLogger.LogMessages.Single();
            restoreLogMessage.Code.Should().Be(NuGetLogCode.NU1510);
            restoreLogMessage.LibraryId.Should().Be("B");
            testEvent["Pruning.RemovablePackages.Count"].Should().Be(1);
            testEvent["Pruning.Pruned.Direct.Count"].Should().Be(1);
        }

        // A 1.0.0 -> B 1.0.0
        // C 1.0.0
        private static async Task SetupCommonPackagesAsync(SimpleTestPathContext pathContext)
        {
            // Setup packages
            var packageA = new SimpleTestPackageContext("A", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("B", "1.0.0")]
            };
            var packageC = new SimpleTestPackageContext("C", "1.0.0");


            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA,
                packageC);
        }

        [Fact]
        public async Task RestoreCommand_WithLockFileAndPrunedPackages_GeneratesCompleteLockFile()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);

            // Act & Assert
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Version.Should().Be(new NuGetVersion("1.0.0"));
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            ISet<LibraryIdentity> installedPackages = result.GetAllInstalled();
            installedPackages.Should().HaveCount(1);

            result._newPackagesLockFile.Should().NotBeNull();
            result._newPackagesLockFile.Targets.Should().HaveCount(1);
            result._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(1);
            result._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            result._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().BeEmpty();
        }

        [Fact]
        public async Task RestoreCommand_WithLockFileAndPrunedPackagesFromPackageReference_WithLockedMode_Succeeds()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, projectSpec);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            setupResult._newPackagesLockFile.Should().NotBeNull();
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            // Set-up again with LockedMode
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec);
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            result._newPackagesLockFile.Should().BeNull();
        }

        [Fact]
        public async Task RestoreCommand_WithLockFileAndPrunedPackagesFromProjectReference_WithLockedMode_Succeeds()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
            }
          }
        }";

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";
            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);
            var projectSpec2 = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, projectSpec, projectSpec2);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            ValidateAssetsFile(setupResult);
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            // Set-up again with LockedMode
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec, projectSpec2);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            ValidateAssetsFile(result);

            static void ValidateAssetsFile(RestoreResult restoreResult)
            {
                restoreResult.LockFile.Targets.Should().HaveCount(1);
                restoreResult.LockFile.Targets[0].Libraries.Should().HaveCount(2);
                restoreResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
                restoreResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
                restoreResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
                restoreResult.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(1);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RestoreCommand_WithPrePruningLockFile_AndPrunedPackageFromPackageReference_WithLockedMode_Succeeds(bool lockedMode)
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var prePruningRootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }";

            // Setup project
            var prePruningSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, prePruningRootProject);
            prePruningSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, prePruningSpec);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);

            setupResult.LockFile.Targets.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            setupResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            setupResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries[0].Dependencies[0].Id.Should().Be("packageB");
            setupResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("packageB");

            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            setupResult._newPackagesLockFile.Should().NotBeNull();
            setupResult._newPackagesLockFile.Targets.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(2);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies[0].Id.Should().Be("packageB");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Id.Should().Be("packageB");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Dependencies.Should().BeEmpty();

            // Set-up again with LockedMode
            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, lockedMode);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            result.LockFile.Targets.Should().HaveCount(1);
            // packageB is part of the pre-pruning lock file, but after pruning is enabled, it gets pruned.
            // The lock file does not get updated.
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result._newPackagesLockFile.Should().BeNull();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RestoreCommand_WithPrePruningLockFile_AndPrunedPackageFromProjectReference_WithLockedMode_Succeeds(bool lockedMode)
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
            }
          }
        }";

            var prePruningRootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
            }
          }
        }";
            // Setup project
            var prePruningProjectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, prePruningRootProject);
            prePruningProjectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, lockedMode);
            var projectSpec2 = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            prePruningProjectSpec = prePruningProjectSpec.WithTestProjectReference(projectSpec2);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, prePruningProjectSpec, projectSpec2);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            setupResult.LockFile.Targets.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            setupResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            setupResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("packageB");
            setupResult.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
            setupResult.LockFile.Targets[0].Libraries[2].Name.Should().Be("Project2");
            setupResult.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(2);
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            setupResult._newPackagesLockFile.Should().NotBeNull();
            setupResult._newPackagesLockFile.Targets.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(3);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Id.Should().Be("packageB");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Dependencies.Should().BeEmpty();
            setupResult._newPackagesLockFile.Targets[0].Dependencies[2].Id.Should().Be("project2");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[2].Dependencies.Should().HaveCount(2);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec, projectSpec2);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            result.LockFile.Targets.Should().HaveCount(1);
            // packageB is part of the pre-pruning lock file, but after pruning is enabled, it gets pruned.
            // The lock file does not get updated.
            result.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(1);
            result._newPackagesLockFile.Should().BeNull();
        }

        [Fact]
        public async Task RestoreCommand_WithPrePruningLockFile_AndPrunedPackageFromPackageReference_WithForceReevaluate_UpdatesLockFile()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var prePruningRootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }";

            // Setup project
            var prePruningSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, prePruningRootProject);
            prePruningSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, prePruningSpec);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);

            setupResult.LockFile.Targets.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries.Should().HaveCount(2);

            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            setupResult._newPackagesLockFile.Should().NotBeNull();
            setupResult._newPackagesLockFile.Targets.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(2);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Run again with pruning enabled and locked mode.
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, false);
            var lockedModeResult = await RunRestoreAsync(pathContext, projectSpec);
            lockedModeResult.Success.Should().BeTrue();
            await lockedModeResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            // Run again without locked mode and force re-evaluate.
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, forceEvaluate: true, testLogger, projectSpec);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            result.LockFile.Targets.Should().HaveCount(1);
            // packageB is part of the pre-pruning lock file, but after pruning is enabled, it gets pruned.
            // The lock file does not get updated.
            result.LockFile.Targets[0].Libraries.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result._newPackagesLockFile.Targets.Should().HaveCount(1);
            result._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(1);
            result._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            result._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().BeEmpty();
        }

        [Fact]
        public async Task RestoreCommand_WithPrePruningLockFile_AndPrunedPackageFromProjectReference_WithForceReevaluate_UpdatesLockFile()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
            }
          }
        }";

            var prePruningRootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
            }
          }
        }";
            // Setup project
            var prePruningProjectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, prePruningRootProject);
            prePruningProjectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);
            var projectSpec2 = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            prePruningProjectSpec = prePruningProjectSpec.WithTestProjectReference(projectSpec2);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, prePruningProjectSpec, projectSpec2);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            setupResult.LockFile.Targets.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            setupResult._newPackagesLockFile.Should().NotBeNull();
            setupResult._newPackagesLockFile.Targets.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(3);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";
            // Set-up again with pruning enabled
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Run with locked mode. Should succeed.
            var lockedModeResult = await RunRestoreAsync(pathContext, projectSpec, projectSpec2);
            lockedModeResult.Success.Should().BeTrue();
            await lockedModeResult.CommitAsync(NullLogger.Instance, CancellationToken.None);

            // Run again without locked mode and force re-evaluate.
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, forceEvaluate: true, testLogger, projectSpec, projectSpec2);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            result.LockFile.Targets.Should().HaveCount(1);
            result.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            result.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            result.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            result.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            result.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(1);

            result._newPackagesLockFile.Should().NotBeNull();
            result._newPackagesLockFile.Targets.Should().HaveCount(1);
            result._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(2);
            result._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            result._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().BeEmpty();
            result._newPackagesLockFile.Targets[0].Dependencies[1].Id.Should().Be("project2");
            result._newPackagesLockFile.Targets[0].Dependencies[1].Dependencies.Should().HaveCount(1);
        }

        [Fact]
        public async Task RestoreCommand_WithLockFileAndPrunableIdPackageFromProjectReference_WithLockedMode_Succeeds()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA);

            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
            }
          }
        }";

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""packagesToPrune"": {
                    ""packageB"" : ""(,0.1.0]"" 
                }
            }
          }
        }";
            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);
            var projectSpec2 = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, projectSpec, projectSpec2);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            ValidateAssetsFile(setupResult);
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            // Set-up again with LockedMode
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec, projectSpec2);
            result.Success.Should().BeTrue(because: testLogger.ShowMessages());
            ValidateAssetsFile(result);

            static void ValidateAssetsFile(RestoreResult restoreResult)
            {
                restoreResult.LockFile.Targets.Should().HaveCount(1);
                restoreResult.LockFile.Targets[0].Libraries.Should().HaveCount(3);
                restoreResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
                restoreResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
                restoreResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("packageB");
                restoreResult.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
                restoreResult.LockFile.Targets[0].Libraries[2].Name.Should().Be("Project2");
                restoreResult.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(2);
            }
        }

        [Fact]
        public async Task RestoreCommand_WithPrePruningLockFile_AndPrunedPackageFromPackageReference_WithLockedMode_AndExtraPackage_FailsWithNU1004()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };
            var packageC = new SimpleTestPackageContext("packageC", "2.0.0");

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA,
                packageC);

            var prePruningRootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                }
            }
          }
        }";

            // Setup project
            var prePruningSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, prePruningRootProject);
            prePruningSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, prePruningSpec);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);

            setupResult.LockFile.Targets.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            setupResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            setupResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries[0].Dependencies[0].Id.Should().Be("packageB");
            setupResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("packageB");

            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            setupResult._newPackagesLockFile.Should().NotBeNull();
            setupResult._newPackagesLockFile.Targets.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(2);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies[0].Id.Should().Be("packageB");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Id.Should().Be("packageB");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Dependencies.Should().BeEmpty();

            // Set-up again with LockedMode and an extra package
            var rootProject = @"
            {
              ""frameworks"": {
                ""net472"": {
                    ""dependencies"": {
                            ""packageA"": {
                                ""version"": ""[1.0.0,)"",
                                ""target"": ""Package"",
                            },
                            ""packageC"": {
                                ""version"": ""[2.0.0,)"",
                                ""target"": ""Package"",
                            },
                    },
                    ""packagesToPrune"": {
                        ""packageB"" : ""(,1.0.0]"" 
                    }
                }
              }
            }";
            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec);
            result.Success.Should().BeFalse(because: testLogger.ShowMessages());
            result.LockFile.LogMessages.Should().HaveCount(1);
            result.LockFile.LogMessages[0].Code.Should().Be(NuGetLogCode.NU1004);
        }

        [Fact]
        public async Task RestoreCommand_WithPrePruningLockFile_AndPrunedPackageFromProjectReference_WithLockedMode_AndExtraPackage_FailsWithNU1004()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            var packageA = new SimpleTestPackageContext("packageA", "1.0.0")
            {
                Dependencies = [new SimpleTestPackageContext("packageB", "1.0.0")]
            };

            var packageC = new SimpleTestPackageContext("packageC", "1.0.0");

            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                packageA,
                packageC);

            var prePruningLeafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
            }
          }
        }";

            var prePruningRootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
            }
          }
        }";
            // Setup project
            var prePruningProjectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, prePruningRootProject);
            prePruningProjectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: false);
            var prePruningLeafProjectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, prePruningLeafProject);
            prePruningProjectSpec = prePruningProjectSpec.WithTestProjectReference(prePruningLeafProjectSpec);

            // Pre-Conditions
            var setupResult = await RunRestoreAsync(pathContext, prePruningProjectSpec, prePruningLeafProjectSpec);
            setupResult.Success.Should().BeTrue();
            await setupResult.CommitAsync(NullLogger.Instance, CancellationToken.None);
            setupResult.LockFile.Targets.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries.Should().HaveCount(3);
            setupResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            setupResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().HaveCount(1);
            setupResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("packageB");
            setupResult.LockFile.Targets[0].Libraries[1].Dependencies.Should().BeEmpty();
            setupResult.LockFile.Targets[0].Libraries[2].Name.Should().Be("Project2");
            setupResult.LockFile.Targets[0].Libraries[2].Dependencies.Should().HaveCount(2);
            File.Delete(setupResult.LockFilePath); // Delete the assets file to avoid assets file library caching.

            setupResult._newPackagesLockFile.Should().NotBeNull();
            setupResult._newPackagesLockFile.Targets.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies.Should().HaveCount(3);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Id.Should().Be("packageA");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[0].Dependencies.Should().HaveCount(1);
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Id.Should().Be("packageB");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[1].Dependencies.Should().BeEmpty();
            setupResult._newPackagesLockFile.Targets[0].Dependencies[2].Id.Should().Be("project2");
            setupResult._newPackagesLockFile.Targets[0].Dependencies[2].Dependencies.Should().HaveCount(2);

            var rootProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageC"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
            }
          }
        }";


            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", pathContext.SolutionRoot, rootProject);
            projectSpec.RestoreMetadata.RestoreLockProperties = new RestoreLockProperties(restorePackagesWithLockFile: "true", nuGetLockFilePath: null, restoreLockedMode: true);
            var leafProjectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            projectSpec = projectSpec.WithTestProjectReference(leafProjectSpec);

            // Run again
            var testLogger = new TestLogger();
            var result = await RunRestoreAsync(pathContext, testLogger, projectSpec, leafProjectSpec);
            result.Success.Should().BeFalse(because: testLogger.ShowMessages());
            result.LockFile.LogMessages.Should().HaveCount(1);
            result.LockFile.LogMessages[0].Code.Should().Be(NuGetLogCode.NU1004);
        }

        [Fact]
        public async Task RestoreCommand_PrunableDependenciesFromDirectPackageReference_DoNotFlowTransitively()
        {
            using var pathContext = new SimpleTestPathContext();

            // Setup packages
            await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                pathContext.PackageSource,
                PackageSaveMode.Defaultv3,
                new SimpleTestPackageContext("packageA", "1.0.0"),
                new SimpleTestPackageContext("packageB", "1.0.0"));

            var leafProject = @"
        {
          ""frameworks"": {
            ""net472"": {
                ""dependencies"": {
                        ""packageA"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                        ""packageB"": {
                            ""version"": ""[1.0.0,)"",
                            ""target"": ""Package"",
                        },
                },
                ""packagesToPrune"": {
                    ""packageB"" : ""(,1.0.0]"" 
                }
            }
          }
        }";

            // Setup project
            var projectSpec = ProjectTestHelpers.GetPackageSpec("Project1", pathContext.SolutionRoot, "net472");
            var projectSpec2 = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project2", pathContext.SolutionRoot, leafProject);
            projectSpec = projectSpec.WithTestProjectReference(projectSpec2);

            // Act & Assert
            var restoreResult = await RunRestoreAsync(pathContext, projectSpec, projectSpec2);
            restoreResult.Success.Should().BeTrue();
            restoreResult.LockFile.Targets.Should().HaveCount(1);
            restoreResult.LockFile.Targets[0].Libraries.Should().HaveCount(2);
            restoreResult.LockFile.Targets[0].Libraries[0].Name.Should().Be("packageA");
            restoreResult.LockFile.Targets[0].Libraries[0].Dependencies.Should().BeEmpty();
            restoreResult.LockFile.Targets[0].Libraries[1].Name.Should().Be("Project2");
            restoreResult.LockFile.Targets[0].Libraries[1].Dependencies.Should().HaveCount(1);
        }

        [Fact]
        public void PopulatePruningEnabledTelemetry_WithVariousFrameworks_PopulatesTelemetryCorrectly()
        {
            var rootProject = @"
                {
                  ""frameworks"": {
                    ""net10.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""a"" : ""(,1.0.0]"" 
                        }
                    },
                    ""net9.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""a"" : ""(,1.0.0]"" 
                        }
                    },
                    ""net8.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        }
                    },
                    ""net7.0"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        }
                    },
                    ""netstandard2.1"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""a"" : ""(,1.0.0]"" 
                        }
                    },
                    ""netstandard1.6"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        }
                    },
                    ""net472"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        }
                    },
                    ""net46"": {
                        ""dependencies"": {
                                ""A"": {
                                    ""version"": ""[1.0.0,)"",
                                    ""target"": ""Package"",
                                },
                        },
                        ""packagesToPrune"": {
                            ""a"" : ""(,1.0.0]"" 
                        }
                    },
                  }
                }";

            var projectSpec = ProjectTestHelpers.GetPackageSpecWithProjectNameAndSpec("Project1", Path.GetTempPath(), rootProject);
            var testLogger = new TestLogger();
            var testEvent = new TelemetryEvent("dummyEvent");

            RestoreCommand.PopulatePruningEnabledTelemetry(projectSpec, testEvent);
            testEvent["Pruning.FrameworksEnabled.Count"].Should().Be(4);
            testEvent["Pruning.FrameworksDisabled.Count"].Should().Be(1);
            testEvent["Pruning.FrameworksUnsupported.Count"].Should().Be(2);
            testEvent["Pruning.FrameworksDefaultDisabled.Count"].Should().Be(1);
        }

        // Add a test where a new package is introduced, but a different package gets pruned, bringing the counter to be the same.

        internal static Task<RestoreResult> RunRestoreAsync(SimpleTestPathContext pathContext, params PackageSpec[] projects)
        {
            return RunRestoreAsync(pathContext, new TestLogger(), projects);
        }

        internal static Task<RestoreResult> RunRestoreAsync(SimpleTestPathContext pathContext, TestLogger logger, params PackageSpec[] projects)
        {
            return RunRestoreAsync(pathContext, forceEvaluate: false, logger, projects);
        }

        internal static Task<RestoreResult> RunRestoreAsync(SimpleTestPathContext pathContext, bool forceEvaluate, TestLogger logger, params PackageSpec[] projects)
        {
            var request = ProjectTestHelpers.CreateRestoreRequest(pathContext, logger, projects);
            request.RestoreForceEvaluate = forceEvaluate;
            return new RestoreCommand(request).ExecuteAsync();
        }
    }
}
