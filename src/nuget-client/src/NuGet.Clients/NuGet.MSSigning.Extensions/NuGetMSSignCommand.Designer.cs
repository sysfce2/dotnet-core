﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace NuGet.MSSigning.Extensions {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class NuGetMSSignCommand {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal NuGetMSSignCommand() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("NuGet.MSSigning.Extensions.NuGetMSSignCommand", typeof(NuGetMSSignCommand).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to File path to the p7b file to be used while signing the package..
        /// </summary>
        internal static string MSSignCommandCertificateFileDescription {
            get {
                return ResourceManager.GetString("MSSignCommandCertificateFileDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to SHA-256, SHA-384 or SHA-512 fingerprint of the certificate used to search a p7b file for the certificate..
        /// </summary>
        internal static string MSSignCommandCertificateFingerprintDescription {
            get {
                return ResourceManager.GetString("MSSignCommandCertificateFingerprintDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Name of the cryptographic service provider which contains the private key container..
        /// </summary>
        internal static string MSSignCommandCSPNameDescription {
            get {
                return ResourceManager.GetString("MSSignCommandCSPNameDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Signs a NuGet package with the specified p7b file..
        /// </summary>
        internal static string MSSignCommandDescription {
            get {
                return ResourceManager.GetString("MSSignCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Hash algorithm to be used while generating the package manifest file. Defaults to SHA256..
        /// </summary>
        internal static string MSSignCommandHashAlgorithmDescription {
            get {
                return ResourceManager.GetString("MSSignCommandHashAlgorithmDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Invalid value provided for &apos;{0}&apos;. For a list of accepted values, please visit https://learn.microsoft.com/nuget/reference/cli-reference/cli-ref-sign.
        /// </summary>
        internal static string MSSignCommandInvalidArgumentException {
            get {
                return ResourceManager.GetString("MSSignCommandInvalidArgumentException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to {0}: Invalid value for &apos;CertificateFingerprint&apos; option. The value must be a SHA-256, SHA-384, or SHA-512 certificate fingerprint (in hexadecimal)..
        /// </summary>
        internal static string MSSignCommandInvalidCertificateFingerprint {
            get {
                return ResourceManager.GetString("MSSignCommandInvalidCertificateFingerprint", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Cng Key is not an RSA key..
        /// </summary>
        internal static string MSSignCommandInvalidCngKeyException {
            get {
                return ResourceManager.GetString("MSSignCommandInvalidCngKeyException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Name of the key container which has the private key..
        /// </summary>
        internal static string MSSignCommandKeyContainerDescription {
            get {
                return ResourceManager.GetString("MSSignCommandKeyContainerDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Can&apos;t find specified certificate..
        /// </summary>
        internal static string MSSignCommandNoCertException {
            get {
                return ResourceManager.GetString("MSSignCommandNoCertException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Can&apos;t find cng key..
        /// </summary>
        internal static string MSSignCommandNoCngKeyException {
            get {
                return ResourceManager.GetString("MSSignCommandNoCngKeyException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to No package was provided. For a list of accepted ways to provide a package, please visit https://docs.nuget.org/docs/reference/command-line-reference.
        /// </summary>
        internal static string MSSignCommandNoPackageException {
            get {
                return ResourceManager.GetString("MSSignCommandNoPackageException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to No &apos;-Timestamper&apos; option was provided the signed package will not be timestamped. To learn more about this option, please visit https://docs.nuget.org/docs/reference/command-line-reference.
        /// </summary>
        internal static string MSSignCommandNoTimestamperWarning {
            get {
                return ResourceManager.GetString("MSSignCommandNoTimestamperWarning", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to No CertificateFile provided or provided file is not a p7b file..
        /// </summary>
        internal static string MSSignCommandNoValidCertificateFileException {
            get {
                return ResourceManager.GetString("MSSignCommandNoValidCertificateFileException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Directory where the signed package should be saved. By default the original package is overwritten by the signed package..
        /// </summary>
        internal static string MSSignCommandOutputDirectoryDescription {
            get {
                return ResourceManager.GetString("MSSignCommandOutputDirectoryDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Switch to indicate if the current signature should be overwritten. By default the command will fail if the package already has a signature..
        /// </summary>
        internal static string MSSignCommandOverwriteDescription {
            get {
                return ResourceManager.GetString("MSSignCommandOverwriteDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to URL to an RFC 3161 timestamping server..
        /// </summary>
        internal static string MSSignCommandTimestamperDescription {
            get {
                return ResourceManager.GetString("MSSignCommandTimestamperDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Hash algorithm to be used by the RFC 3161 timestamp server. Defaults to SHA256..
        /// </summary>
        internal static string MSSignCommandTimestampHashAlgorithmDescription {
            get {
                return ResourceManager.GetString("MSSignCommandTimestampHashAlgorithmDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Signs a NuGet package..
        /// </summary>
        internal static string MSSignCommandUsageDescription {
            get {
                return ResourceManager.GetString("MSSignCommandUsageDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to nuget mssign MyPackage.nupkg -Timestamper https://foo.bar -CertificateFile foo.p7b -CSPName &quot;Cryptographic Service Provider&quot;  -KeyContainer &quot;4003d786-cc37-4004-bfdf-c4f3e8ef9b3a&quot; -CertificateFingerprint &quot;4003d786cc374004bfdfc4f3e8ef9b3a&quot;  
        ///
        ///nuget mssign MyPackage.nupkg -Timestamper https://foo.bar -CertificateFile foo.p7b -CSPName &quot;Cryptographic Service Provider&quot;  -KeyContainer &quot;4003d786-cc37-4004-bfdf-c4f3e8ef9b3a&quot; -CertificateFingerprint &quot;4003d786cc374004bfdfc4f3e8ef9b3a&quot; -OutputDirectory .\..\Signed.
        /// </summary>
        internal static string MSSignCommandUsageExamples {
            get {
                return ResourceManager.GetString("MSSignCommandUsageExamples", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &lt;package_path&gt; -Timestamper &lt;timestamp_server_url&gt; -CertificateFile &lt;p7b_file_path&gt; -CSPName &lt;cryptographic_service _provider_name&gt;  -KeyContainer &lt;key_container_guid&gt;  -CertificateFingerprint &lt;certificate_fingerprint&gt;.
        /// </summary>
        internal static string MSSignCommandUsageSummary {
            get {
                return ResourceManager.GetString("MSSignCommandUsageSummary", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Sign a NuGet package by adding repository signature with specified p7b file..
        /// </summary>
        internal static string RepoSignCommandDescription {
            get {
                return ResourceManager.GetString("RepoSignCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Semicolon-separated list of package owners..
        /// </summary>
        internal static string RepoSignCommandPackageOwnersDescription {
            get {
                return ResourceManager.GetString("RepoSignCommandPackageOwnersDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Sign a NuGet package with repository signature..
        /// </summary>
        internal static string RepoSignCommandUsageDescription {
            get {
                return ResourceManager.GetString("RepoSignCommandUsageDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to nuget reposign MyPackage.nupkg -Timestamper http://timestamper.test -CertificateFile certificates.p7b -CSPName &quot;Cryptographic Service Provider&quot; -KeyContainer 4003d786-cc37-4004-bfdf-c4f3e8ef9b3a -CertificateFingerprint 8599ADD1C62EE42E315EA887FF60908B7A7C6E9B -V3ServiceIndexUrl https://v3.index.test
        ///
        ///nuget reposign MyPackage.nupkg -Timestamper http://timestamper.test -CertificateFile certificates.p7b -CSPName &quot;Cryptographic Service Provider&quot; -KeyContainer 4003d786-cc37-4004-bfdf-c4f3e8ef9b3a -CertificateF [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string RepoSignCommandUsageExamples {
            get {
                return ResourceManager.GetString("RepoSignCommandUsageExamples", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &lt;package_path&gt; -Timestamper &lt;timestamp_server_url&gt; -CertificateFile &lt;p7b_file_path&gt; -CSPName &lt;cryptographic_service_provider_name&gt; -KeyContainer &lt;key_container_guid&gt; -CertificateFingerprint &lt;certificate_fingerprint&gt; -PackageOwners &lt;package_owners&gt; -V3ServiceIndexUrl &lt;v3_service_index_url&gt;.
        /// </summary>
        internal static string RepoSignCommandUsageSummary {
            get {
                return ResourceManager.GetString("RepoSignCommandUsageSummary", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Repository V3 service index URL..
        /// </summary>
        internal static string RepoSignCommandV3ServiceIndexUrlDescription {
            get {
                return ResourceManager.GetString("RepoSignCommandV3ServiceIndexUrlDescription", resourceCulture);
            }
        }
    }
}
