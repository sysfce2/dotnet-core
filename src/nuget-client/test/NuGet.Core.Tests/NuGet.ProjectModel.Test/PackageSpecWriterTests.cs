// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Internal.NuGet.Testing.SignedPackages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.RuntimeModel;
using NuGet.Versioning;
using Test.Utility;
using Xunit;

namespace NuGet.ProjectModel.Test
{
    public class PackageSpecWriterTests
    {
        private static readonly PackageSpec EmptyPackageSpec = JsonPackageSpecReader.GetPackageSpec("{}", null, null);

        [Fact]
        public void RoundTripAutoReferencedProperty()
        {
            // Arrange
            var json = @"{
                    ""dependencies"": {
                        ""b"": {
                            ""version"": ""[1.0.0, )"",
                            ""autoReferenced"": true
                        }
                    },
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""autoReferenced"": true
                            }
                        }
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RoundTripDownloadDependencies()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""autoReferenced"": true
                            }
                        },
                        ""downloadDependencies"": [
                            {""name"" : ""a"", ""version"" : ""[1.0.0, 1.0.0]""},
                            {""name"" : ""b"", ""version"" : ""[1.0.0, 1.0.0];[2.0.0, 2.0.0]""}
                        ]
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_ThrowsForNullPackageSpec()
        {
            using (var jsonWriter = new JTokenWriter())
            using (var writer = new JsonObjectWriter(jsonWriter))
            {
                Assert.Throws<ArgumentNullException>(() => PackageSpecWriter.Write(packageSpec: null, writer: writer));
            }
        }

        [Fact]
        public void Write_ThrowsForNullWriter()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => PackageSpecWriter.Write(EmptyPackageSpec, writer: null));
        }

        [Fact]
        public void Write_ReadWriteDependencies()
        {
            // Arrange
            var json = @"{
  ""version"": ""1.2.3"",
  ""dependencies"": {
    ""packageA"": {
      ""suppressParent"": ""All"",
      ""target"": ""Project""
    }
  },
  ""frameworks"": {
    ""net46"": {}
  }
}";
            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_ReadWriteWarningProperties()
        {
            // Arrange
            var json = @"{
                            ""restore"": {
    ""projectUniqueName"": ""projectUniqueName"",
    ""projectName"": ""projectName"",
    ""projectPath"": ""projectPath"",
    ""projectJsonPath"": ""projectJsonPath"",
    ""packagesPath"": ""packagesPath"",
    ""outputPath"": ""outputPath"",
    ""projectStyle"": ""PackageReference"",
    ""crossTargeting"": true,
    ""restoreUseLegacyDependencyResolver"": true,
    ""fallbackFolders"": [
      ""b"",
      ""a"",
      ""c""
    ],
    ""configFilePaths"": [
      ""b"",
      ""a"",
      ""c""
    ],
    ""originalTargetFrameworks"": [
      ""a"",
      ""b"",
      ""c""
    ],
    ""sources"": {
      ""source"": {}
    },
    ""frameworks"": {
      ""net45"": {
        ""projectReferences"": {}
      }
    },
    ""warningProperties"": {
      ""allWarningsAsErrors"": true,
      ""noWarn"": [
        ""NU1601"",
      ],
      ""warnAsError"": [
        ""NU1500"",
        ""NU1501""
      ],
      ""warnNotAsError"": [
        ""NU1801"",
        ""NU1802""
      ]
    }
  }
}";
            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void WriteToFile_ThrowsForNullPackageSpec()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => PackageSpecWriter.WriteToFile(packageSpec: null, filePath: @"C:\a.json"));
        }

        [Fact]
        public void WriteToFile_ThrowsForNullFilePath()
        {
            // Assert
            Assert.Throws<ArgumentException>(() => PackageSpecWriter.WriteToFile(EmptyPackageSpec, filePath: null));
        }

        [Fact]
        public void WriteToFile_ThrowsForEmptyFilePath()
        {
            // Assert
            Assert.Throws<ArgumentException>(() => PackageSpecWriter.WriteToFile(EmptyPackageSpec, filePath: null));
        }

        [Fact]
        public void Write_SerializesMembersAsJson()
        {
            // Arrange && Act
            var expectedJson = ResourceTestUtility.GetResource("NuGet.ProjectModel.Test.compiler.resources.PackageSpecWriter_Write_SerializesMembersAsJson.json", typeof(PackageSpecWriterTests));
            var packageSpec = CreatePackageSpec(withRestoreSettings: true);
            var actualJson = GetJsonString(packageSpec);

            // Assert
            Assert.Equal(expectedJson, actualJson);
        }

        [Fact]
        public void Write_SerializesMembersAsJsonWithoutRestoreSettings()
        {
            // Arrange && Act
            var expectedJson = ResourceTestUtility.GetResource("NuGet.ProjectModel.Test.compiler.resources.PackageSpecWriter_Write_SerializesMembersAsJson.json", typeof(PackageSpecWriterTests));
            var packageSpec = CreatePackageSpec(withRestoreSettings: false);
            var actualJson = GetJsonString(packageSpec);

            // Assert
            expectedJson.Should().Be(actualJson);
        }

        [Fact]
        public void Write_SerializesMembersAsJsonWithWarningProperties()
        {
            // Arrange && Act
            var expectedWarningPropertiesJson = @"{
  ""allWarningsAsErrors"": true,
  ""noWarn"": [
    ""NU1601"",
    ""NU1602""
  ],
  ""warnAsError"": [
    ""NU1500"",
    ""NU1501""
  ],
  ""warnNotAsError"": [
    ""NU1801"",
    ""NU1802""
  ]
}";
            var allWarningsAsErrors = true;
            var warningsAsErrors = new HashSet<NuGetLogCode> { NuGetLogCode.NU1500, NuGetLogCode.NU1501 };
            var noWarn = new HashSet<NuGetLogCode> { NuGetLogCode.NU1602, NuGetLogCode.NU1601 };
            var warningsNotAsErrors = new HashSet<NuGetLogCode> { NuGetLogCode.NU1801, NuGetLogCode.NU1802 };
            var warningProperties = new WarningProperties(warningsAsErrors, noWarn, allWarningsAsErrors, warningsNotAsErrors);
            var packageSpec = CreatePackageSpec(withRestoreSettings: true, warningProperties: warningProperties);
            var actualJson = packageSpec.ToJObject();
            var actualWarningPropertiesJson = actualJson["restore"]["warningProperties"].ToString();

            // Assert
            Assert.NotNull(actualWarningPropertiesJson);
            Assert.Equal(expectedWarningPropertiesJson, actualWarningPropertiesJson);
        }

        [Fact]
        public void Write_SerializesMembersAsJsonWithWarningPropertiesAndNoAllWarningsAsErrors()
        {
            // Arrange && Act
            var expectedWarningPropertiesJson = @"{
  ""noWarn"": [
    ""NU1601"",
    ""NU1602""
  ],
  ""warnAsError"": [
    ""NU1500"",
    ""NU1501""
  ],
  ""warnNotAsError"": [
    ""NU1802"",
    ""NU1803""
  ]
}";
            var allWarningsAsErrors = false;
            var warningsAsErrors = new HashSet<NuGetLogCode> { NuGetLogCode.NU1500, NuGetLogCode.NU1501 };
            var noWarn = new HashSet<NuGetLogCode> { NuGetLogCode.NU1602, NuGetLogCode.NU1601 };
            var warningsNotAsErrors = new HashSet<NuGetLogCode>() { NuGetLogCode.NU1802, NuGetLogCode.NU1803 };
            var warningProperties = new WarningProperties(warningsAsErrors, noWarn, allWarningsAsErrors, warningsNotAsErrors);
            var packageSpec = CreatePackageSpec(withRestoreSettings: true, warningProperties: warningProperties);
            var actualJson = packageSpec.ToJObject();
            var actualWarningPropertiesJson = actualJson["restore"]["warningProperties"].ToString();

            // Assert
            Assert.NotNull(actualWarningPropertiesJson);
            Assert.Equal(expectedWarningPropertiesJson, actualWarningPropertiesJson);
        }

        [Fact]
        public void Write_SerializesMembersAsJsonWithWarningPropertiesAndNo_WarnAsError()
        {
            // Arrange && Act
            var expectedWarningPropertiesJson = @"{
  ""allWarningsAsErrors"": true,
  ""noWarn"": [
    ""NU1601"",
    ""NU1602""
  ]
}";
            var allWarningsAsErrors = true;
            var warningsAsErrors = new HashSet<NuGetLogCode> { };
            var noWarn = new HashSet<NuGetLogCode> { NuGetLogCode.NU1602, NuGetLogCode.NU1601 };
            var warningsNotAsErrors = new HashSet<NuGetLogCode>() { };
            var warningProperties = new WarningProperties(warningsAsErrors, noWarn, allWarningsAsErrors, warningsNotAsErrors);
            var packageSpec = CreatePackageSpec(withRestoreSettings: true, warningProperties: warningProperties);
            var actualJson = packageSpec.ToJObject();
            var actualWarningPropertiesJson = actualJson["restore"]["warningProperties"].ToString();

            // Assert
            Assert.NotNull(actualWarningPropertiesJson);
            Assert.Equal(expectedWarningPropertiesJson, actualWarningPropertiesJson);
        }

        [Fact]
        public void Write_SerializesMembersAsJsonWithEmptyWarningProperties()
        {
            // Arrange && Act
            var allWarningsAsErrors = false;
            var warningsAsErrors = new HashSet<NuGetLogCode> { };
            var noWarn = new HashSet<NuGetLogCode> { };
            var warningsNotAsErrors = new HashSet<NuGetLogCode>() { };
            var warningProperties = new WarningProperties(warningsAsErrors, noWarn, allWarningsAsErrors, warningsNotAsErrors);
            var packageSpec = CreatePackageSpec(withRestoreSettings: true, warningProperties: warningProperties);
            var actualJson = packageSpec.ToJObject();
            var actualWarningPropertiesJson = actualJson["restore"]["warningProperties"];

            // Assert
            Assert.Null(actualWarningPropertiesJson);
        }

        [Fact]
        public void Write_SerializesMembersAsJsonWithWarningPropertiesAndNo_NoWarn()
        {
            // Arrange && Act
            var expectedWarningPropertiesJson = @"{
  ""allWarningsAsErrors"": true,
  ""warnAsError"": [
    ""NU1500"",
    ""NU1501""
  ]
}";
            var allWarningsAsErrors = true;
            var warningsAsErrors = new HashSet<NuGetLogCode> { NuGetLogCode.NU1500, NuGetLogCode.NU1501 };
            var noWarn = new HashSet<NuGetLogCode> { };
            var warningsNotAsErrors = new HashSet<NuGetLogCode>() { };
            var warningProperties = new WarningProperties(warningsAsErrors, noWarn, allWarningsAsErrors, warningsNotAsErrors);
            var packageSpec = CreatePackageSpec(withRestoreSettings: true, warningProperties: warningProperties);
            var actualJson = packageSpec.ToJObject();
            var actualWarningPropertiesJson = actualJson["restore"]["warningProperties"].ToString();

            // Assert
            Assert.NotNull(actualWarningPropertiesJson);
            Assert.Equal(expectedWarningPropertiesJson, actualWarningPropertiesJson);
        }

        [Fact]
        public void Write_ReadWriteDependenciesAreSorted()
        {
            // Arrange
            var json = @"{
                    ""dependencies"": {
                        ""b"": {
                                ""version"": ""[1.0.0, )"",
                        },
                        ""a"": {
                            ""version"": ""[1.0.0, )"",
                        }
                    },
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""b"": {
                                ""version"": ""[1.0.0, )"",
                            },
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                            }
                        },
                        ""downloadDependencies"": [
                            {""name"" : ""c"", ""version"" : ""[2.0.0]""},
                            {""name"" : ""d"", ""version"" : ""[2.0.0]""},
                       ],
                       ""frameworkReferences"" : {
                            ""b"": {
                                ""privateAssets"": ""none"",
                            },
                            ""a"": {
                                ""privateAssets"": ""none"",
                            }
                        }
                    }
                  }
                }";

            var expectedJson = @"{
                  ""dependencies"": {
                    ""a"": ""[1.0.0, )"",
                    ""b"": ""[1.0.0, )""
                  },
                  ""frameworks"": {
                    ""net46"": {
                      ""dependencies"": {
                        ""a"": ""[1.0.0, )"",
                        ""b"": ""[1.0.0, )""
                      },
                      ""downloadDependencies"": [
                            {""name"" : ""c"", ""version"" : ""[2.0.0, 2.0.0]""},
                            {""name"" : ""d"", ""version"" : ""[2.0.0, 2.0.0]""},
                       ],
                      ""frameworkReferences"" : {
                            ""a"": {
                                ""privateAssets"": ""none"",
                            },
                            ""b"": {
                                ""privateAssets"": ""none"",
                            }
                        }
                    }
                  }
                }";
            // Act & Assert
            VerifyPackageSpecWrite(json, expectedJson);
        }

        [Fact]
        public void Write_ReadWriteVersionsAreNormalized()
        {
            // Arrange
            var json = @"{
                    ""dependencies"": {
                        ""a"": {
                                ""version"": ""1.0.0"",
                        },
                    },
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""1.0.0"",
                            },
                        },
                        ""downloadDependencies"":  [
                            {""name"" : ""b"", ""version"" : ""[2.0.0]""},
                       ]
                    }
                  }
                }";

            var expectedJson = @"{
                  ""dependencies"": {
                    ""a"": ""[1.0.0, )""
                  },
                  ""frameworks"": {
                    ""net46"": {
                      ""dependencies"": {
                        ""a"": ""[1.0.0, )""
                      },
                      ""downloadDependencies"": [
                            {""name"" : ""b"", ""version"" : ""[2.0.0, 2.0.0]""},
                       ]
                    }
                  }
                }";
            // Act & Assert
            VerifyPackageSpecWrite(json, expectedJson);
        }

        [Fact]
        public void RoundTripFrameworkReferences()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""autoReferenced"": true
                            }
                        },
                        ""frameworkReferences"": {
                            ""Microsoft.WindowsDesktop.App|WinForms"" : {
                                ""privateAssets"" : ""none""
                            },
                            ""Microsoft.WindowsDesktop.App|WPF"" : {
                                ""privateAssets"" : ""none""
                            }
                        }
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RoundTripRuntimeIdentifierGraphPath()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""netcoreapp3.0"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""autoReferenced"": true
                            }
                        },
                        ""runtimeIdentifierGraphPath"": ""path\\to\\sdk\\3.0.100\\runtime.json""
                    },
                    ""netcoreapp3.1"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[2.0.0, )"",
                                ""autoReferenced"": true
                            }
                        },
                        ""runtimeIdentifierGraphPath"": ""path\\to\\sdk\\3.1.100\\runtime.json""
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RoundTripPackageReferenceVersionCentrallyManaged()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""versionCentrallyManaged"": true
                            }
                        }
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RoundTripPackageReferenceGeneratePathProperty()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""generatePathProperty"": true
                            }
                        }
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RoundTripPackageReferenceAliases()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net46"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""aliases"": ""yay""
                            }
                        }
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RoundTripTargetFrameworkAliases()
        {
            var json = @"{
                        ""restore"": {
                        ""projectUniqueName"": ""projectUniqueName"",
                        ""projectName"": ""projectName"",
                        ""projectPath"": ""projectPath"",
                        ""projectJsonPath"": ""projectJsonPath"",
                        ""packagesPath"": ""packagesPath"",
                        ""outputPath"": ""outputPath"",
                        ""projectStyle"": ""PackageReference"",
                        ""frameworks"": {
                          ""net45"": {
                            ""targetAlias"" : ""minNetVersion"",
                            ""projectReferences"": {}
                          }
                        }
                      },
                  ""frameworks"": {
                    ""net46"": {
                        ""targetAlias"" : ""minNetVersion"",
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""aliases"": ""yay""
                            }
                        }
                    }
                }
            }";
            // Arrange

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_WithAssetTargetFallbackAndDualCompatibilityFramework_RoundTrips()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net5.0"": {
                        ""dependencies"": {
                            ""a"": {
                                ""version"": ""[1.0.0, )"",
                                ""autoReferenced"": true
                            }
                        },
                        ""imports"": [
                           ""net472"",
                           ""net471""
                        ],
                        ""assetTargetFallback"" : true,
                        ""secondaryFramework"" : ""native""
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_RestoreAuditProperties_RoundTrips()
        {
            // Arrange
            var json = @"{
                  ""restore"": {
                    ""projectUniqueName"": ""projectUniqueName"",
                    ""restoreAuditProperties"": {
                        ""enableAudit"": ""true"",
                        ""auditLevel"": ""moderate"",
                        ""auditMode"": ""all""
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_RestoreSdkAnalysisLevel_RoundTrips()
        {
            // Arrange
            var json = @"{
                  ""restore"": {
                    ""projectUniqueName"": ""projectUniqueName"",
                    ""SdkAnalysisLevel"": ""9.0.100""
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_RestoreUsingMicrosoftNetSdk_RoundTrips()
        {
            // Arrange
            var json = @"{
                  ""restore"": {
                    ""projectUniqueName"": ""projectUniqueName"",
                    ""UsingMicrosoftNETSdk"": false
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void Write_RestoreAuditPropertiesWithSuppressions_RoundTrips()
        {
            // Arrange
            var json = @"{
                  ""restore"": {
                    ""projectUniqueName"": ""projectUniqueName"",
                    ""restoreAuditProperties"": {
                        ""enableAudit"": ""true"",
                        ""auditLevel"": ""moderate"",
                        ""auditMode"": ""all"",
                        ""suppressedAdvisories"": {
                            ""https://github.com/advisories/example-cve-1"": null,
                            ""https://github.com/advisories/example-cve-2"": null
                        },
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        [Fact]
        public void RestoreMetadataWithMacros_RoundTrips()
        {
            // Arrange
            var json = @"{
                            ""restore"": {
    ""projectUniqueName"": ""C:\\users\\me\\source\\code\\project.csproj"",
    ""projectName"": ""project"",
    ""projectPath"": ""C:\\users\\me\\source\\code\\project.csproj"",
    ""projectJsonPath"": ""C:\\users\\me\\source\\code\\project.json"",
    ""packagesPath"": ""$(User).nuget\\packages"",
    ""outputPath"": ""C:\\users\\me\\source\\code\\obj"",
    ""projectStyle"": ""PackageReference"",
    ""crossTargeting"": true,
    ""fallbackFolders"": [
        ""C:\\Program Files\\dotnet\\sdk\\NuGetFallbackFolder"",
        ""$(User)fallbackFolder""


    ],
    ""configFilePaths"": [
        ""$(User)source\\code\\NuGet.Config"",
        ""$(User)AppData\\Roaming\\NuGet\\NuGet.Config"",
        ""C:\\Program Files (x86)\\NuGet\\Config\\Microsoft.VisualStudio.FallbackLocation.config"",
        ""C:\\Program Files (x86)\\NuGet\\Config\\Microsoft.VisualStudio.Offline.config""
    ]
  }
}";
            var environmentReader = new TestEnvironmentVariableReader(new Dictionary<string, string>()
                {
                    { MacroStringsUtility.NUGET_ENABLE_EXPERIMENTAL_MACROS, "true" }
            });

            // Act
            var actual = PackageSpecTestUtility.RoundTripJson(json, environmentReader);

            // Assert
            var metadata = actual.RestoreMetadata;
            var userSettingsDirectory = NuGetEnvironment.GetFolderPath(NuGetFolderPath.UserSettingsDirectory);

            Assert.NotNull(metadata);
            metadata.PackagesPath.Should().Be(@$"{userSettingsDirectory}.nuget\packages");

            metadata.ConfigFilePaths.Should().Contain(@$"{userSettingsDirectory}source\code\NuGet.Config");
            metadata.ConfigFilePaths.Should().Contain(@"C:\Program Files (x86)\NuGet\Config\Microsoft.VisualStudio.FallbackLocation.config");
            metadata.ConfigFilePaths.Should().Contain(@"C:\Program Files (x86)\NuGet\Config\Microsoft.VisualStudio.Offline.config");
            metadata.ConfigFilePaths.Should().Contain(@$"{userSettingsDirectory}AppData\Roaming\NuGet\NuGet.Config");

            metadata.FallbackFolders.Should().Contain(@"C:\Program Files\dotnet\sdk\NuGetFallbackFolder");
            metadata.FallbackFolders.Should().Contain(@$"{userSettingsDirectory}fallbackFolder");
        }

        [Fact]
        public void RoundTripPackagesToPrune()
        {
            // Arrange
            var json = @"{
                  ""frameworks"": {
                    ""net46"": {
                        ""packagesToPrune"": {
                            ""a"": ""(, 2.1.3]""
                        }
                    }
                  }
                }";

            // Act & Assert
            VerifyJsonPackageSpecRoundTrip(json);
        }

        private static string GetJsonString(PackageSpec packageSpec)
        {
            JObject jObject = packageSpec.ToJObject();

            return jObject.ToString(Formatting.Indented);
        }

        private static PackageSpec CreatePackageSpec(bool withRestoreSettings, WarningProperties warningProperties = null)
        {
            var unsortedArray = new[] { "b", "a", "c" };
            var unsortedReadOnlyList = new List<string>(unsortedArray).AsReadOnly();
            var libraryRange = new LibraryRange("library", new VersionRange(new NuGetVersion("1.2.3")), LibraryDependencyTarget.Package);
            var libraryRangeWithNoWarn = new LibraryRange("libraryWithNoWarn", new VersionRange(new NuGetVersion("1.2.3")), LibraryDependencyTarget.Package);
            var libraryRangeWithNoWarnGlobal = new LibraryRange("libraryRangeWithNoWarnGlobal", new VersionRange(new NuGetVersion("1.2.3")), LibraryDependencyTarget.Package);
            var libraryDependency = new LibraryDependency()
            {
                IncludeType = LibraryIncludeFlags.Build,
                LibraryRange = libraryRange
            };

            var libraryDependencyWithNoWarn = new LibraryDependency()
            {
                IncludeType = LibraryIncludeFlags.Build,
                LibraryRange = libraryRangeWithNoWarn,
                NoWarn = [NuGetLogCode.NU1500, NuGetLogCode.NU1601]
            };

            var libraryDependencyWithNoWarnGlobal = new LibraryDependency()
            {
                IncludeType = LibraryIncludeFlags.Build,
                LibraryRange = libraryRangeWithNoWarnGlobal,
                NoWarn = [NuGetLogCode.NU1500, NuGetLogCode.NU1608]
            };

            var nugetFramework = new NuGetFramework("frameworkIdentifier", new Version("1.2.3"), "frameworkProfile");
            var nugetFrameworkWithNoWarn = new NuGetFramework("frameworkIdentifierWithNoWarn", new Version("1.2.5"), "frameworkProfileWithNoWarn");

            var packageSpec = new PackageSpec()
            {
                Dependencies = new List<LibraryDependency>() { libraryDependency, libraryDependencyWithNoWarnGlobal },
                Name = "name",
                FilePath = "filePath",
                RestoreMetadata = new ProjectRestoreMetadata()
                {
                    CrossTargeting = true,
                    FallbackFolders = unsortedReadOnlyList,
                    ConfigFilePaths = unsortedReadOnlyList,
                    LegacyPackagesDirectory = false,
                    OriginalTargetFrameworks = unsortedReadOnlyList,
                    OutputPath = "outputPath",
                    ProjectStyle = ProjectStyle.PackageReference,
                    PackagesPath = "packagesPath",
                    ProjectJsonPath = "projectJsonPath",
                    ProjectName = "projectName",
                    ProjectPath = "projectPath",
                    ProjectUniqueName = "projectUniqueName",
                    Sources = new List<PackageSource>()
                        {
                            new PackageSource("source", "name", isEnabled: true, isOfficial: false, isPersistable: true)
                        },
                    TargetFrameworks = new List<ProjectRestoreMetadataFrameworkInfo>()
                        {
                            new ProjectRestoreMetadataFrameworkInfo(nugetFramework)
                        }
                },
                Version = new NuGetVersion("1.2.3")
            };

            if (withRestoreSettings)
            {
                packageSpec.RestoreSettings = new ProjectRestoreSettings() { HideWarningsAndErrors = true };
            }

            if (warningProperties != null)
            {
                packageSpec.RestoreMetadata.ProjectWideWarningProperties = warningProperties;
            }

            var runtimeDependencySet = new RuntimeDependencySet("id", new[]
            {
                new RuntimePackageDependency("id", new VersionRange(new NuGetVersion("1.2.3")))
            });
            var runtimes = new List<RuntimeDescription>()
            {
                new RuntimeDescription("runtimeIdentifier", unsortedArray, new [] { runtimeDependencySet })
            };
            var compatibilityProfiles = new List<CompatibilityProfile>()
            {
                new CompatibilityProfile("name", new[] { new FrameworkRuntimePair(nugetFramework, "runtimeIdentifier")})
            };

            packageSpec.RuntimeGraph = new RuntimeGraph(runtimes, compatibilityProfiles);

            packageSpec.TargetFrameworks.Add(new TargetFrameworkInformation()
            {
                Dependencies = [],
                FrameworkName = nugetFramework,
                Imports = [nugetFramework],
            });

            packageSpec.TargetFrameworks.Add(new TargetFrameworkInformation()
            {
                Dependencies = [libraryDependencyWithNoWarn],
                FrameworkName = nugetFrameworkWithNoWarn,
                Imports = [nugetFrameworkWithNoWarn],
                Warn = true
            });

            return packageSpec;
        }

        private static void VerifyJsonPackageSpecRoundTrip(string json)
        {
            // Arrange & Act
            var spec = JsonPackageSpecReader.GetPackageSpec(json, "testName", @"C:\fake\path");
            string actualResult = GetJsonString(spec);
            string expected = JObject.Parse(json).ToString();

            // Assert
            Assert.Equal(expected, actualResult);
        }

        private static void VerifyPackageSpecWrite(string json, string expectedJson)
        {
            // Arrange & Act
            PackageSpec spec = JsonPackageSpecReader.GetPackageSpec(json, "testName", @"C:\fake\path");
            string actualResult = GetJsonString(spec);
            string expected = JObject.Parse(expectedJson).ToString();

            // Assert
            Assert.Equal(expected, actualResult);
        }
    }
}
