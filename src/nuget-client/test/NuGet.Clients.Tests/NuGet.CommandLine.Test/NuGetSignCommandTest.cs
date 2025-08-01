// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Moq;
using NuGet.Commands;
using NuGet.Common;
using Xunit;

namespace NuGet.CommandLine.Test
{
    public class NuGetSignCommandTest
    {
        private const string InvalidArgException = "Invalid value provided for '{0}'. For a list of accepted values, please visit https://docs.nuget.org/docs/reference/command-line-reference";
        private const string NoPackageException = "No package was provided. For a list of accepted ways to provide a package, please visit https://docs.nuget.org/docs/reference/command-line-reference";
        private const string MultipleCertificateException = "Multiple options were used to specify a certificate. For a list of accepted ways to provide a certificate, please visit https://docs.nuget.org/docs/reference/command-line-reference";
        private const string InvalidCertificateFingerprintException = "Invalid value for 'CertificateFingerprint' option. The value must be a SHA-256, SHA-384, or SHA-512 certificate fingerprint (in hexadecimal).";
        private const string Sha256Hash = "A591A6D40BF420404A011733CFB7B190D62C65BF0BCDA32B56C92B409B0F9DCA";

        [Fact]
        public void SignCommandArgParsing_NoPackagePath()
        {
            // Arrange
            var mockSignCommandRunner = new Mock<ISignCommandRunner>();
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
            };

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.Equal(NoPackageException, ex.Message);
        }

        [Theory]
        [InlineData(@"\\path\file.pfx", "test_cert_subject", "")]
        [InlineData("\\path\file.cert", "", "test_cert_fingerprint")]
        [InlineData("\\path\file.cert", "test_cert_subject", "test_cert_fingerprint")]
        [InlineData("", "test_cert_subject", "test_cert_fingerprint")]
        public void SignCommandArgParsing_MultipleCertificateOptions(
            string certificatePath,
            string certificateSubjectName,
            string certificateFingerprint)
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var mockSignCommandRunner = new Mock<ISignCommandRunner>();
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificatePath = certificatePath,
                CertificateSubjectName = certificateSubjectName,
                CertificateFingerprint = certificateFingerprint
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.Equal(MultipleCertificateException, ex.Message);
        }

        [Theory]
        [InlineData("AddressBook")]
        [InlineData("AuthRoot")]
        [InlineData("CertificateAuthority")]
        [InlineData("Disallowed")]
        [InlineData("My")]
        [InlineData("Root")]
        [InlineData("TrustedPeople")]
        [InlineData("TrustedPublisher")]
        [InlineData("AddreSSBook")]
        [InlineData("addressbook")]
        public void SignCommandArgParsing_ValidCertificateStoreName(string storeName)
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificateFingerprint = Sha256Hash;
            var parsable = Enum.TryParse(storeName, ignoreCase: true, result: out StoreName parsedStoreName);
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateFingerprint = certificateFingerprint,
                CertificateStoreName = storeName
            };
            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            Assert.True(parsable);
            var signArgs = signCommand.GetSignArgs();
            Assert.Equal(parsedStoreName, signArgs.CertificateStoreName);
            Assert.Equal(StoreLocation.CurrentUser, signArgs.CertificateStoreLocation);
        }

        [Fact]
        public void SignCommandArgParsing_InvalidCertificateStoreName()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificateFingerprint = Sha256Hash;
            var storeName = "random_store";
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateFingerprint = certificateFingerprint,
                CertificateStoreName = storeName
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.Equal(string.Format(InvalidArgException, nameof(signCommand.CertificateStoreName)), ex.Message);
        }

        [Theory]
        [InlineData("CurrentUser")]
        [InlineData("LocalMachine")]
        [InlineData("currentuser")]
        [InlineData("cURRentuser")]
        public void SignCommandArgParsing_ValidCertificateStoreLocation(string storeLocation)
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificateFingerprint = Sha256Hash;
            var parsable = Enum.TryParse(storeLocation, ignoreCase: true, result: out StoreLocation parsedStoreLocation);
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateFingerprint = certificateFingerprint,
                CertificateStoreLocation = storeLocation
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            Assert.True(parsable);
            var signArgs = signCommand.GetSignArgs();
            Assert.Equal(StoreName.My, signArgs.CertificateStoreName);
            Assert.Equal(parsedStoreLocation, signArgs.CertificateStoreLocation);
        }

        [Fact]
        public void SignCommandArgParsing_InvalidCertificateStoreLocation()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificateFingerprint = Sha256Hash;
            var storeLocation = "random_location";
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateFingerprint = certificateFingerprint,
                CertificateStoreLocation = storeLocation
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.Equal(string.Format(InvalidArgException, nameof(signCommand.CertificateStoreLocation)), ex.Message);
        }

        [Theory]
        [InlineData("sha256")]
        [InlineData("sha384")]
        [InlineData("sha512")]
        [InlineData("ShA256")]
        [InlineData("SHA256")]
        [InlineData("SHA384")]
        [InlineData("SHA512")]
        public void SignCommandArgParsing_ValidHashAlgorithm(string hashAlgorithm)
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificatePath = @"\\path\file.pfx";
            var parsable = Enum.TryParse(hashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedHashAlgorithm);
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificatePath = certificatePath,
                HashAlgorithm = hashAlgorithm
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            Assert.True(parsable);
            var signArgs = signCommand.GetSignArgs();
            Assert.Equal(parsedHashAlgorithm, signArgs.SignatureHashAlgorithm);
            Assert.Equal(HashAlgorithmName.SHA256, signArgs.TimestampHashAlgorithm);
        }

        [Fact]
        public void SignCommandArgParsing_InvalidHashAlgorithm()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificatePath = @"\\path\file.pfx";
            var hashAlgorithm = "MD5";
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificatePath = certificatePath,
                HashAlgorithm = hashAlgorithm
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.Equal(string.Format(InvalidArgException, nameof(signCommand.HashAlgorithm)), ex.Message);
        }

        [Theory]
        [InlineData("sha256")]
        [InlineData("sha384")]
        [InlineData("sha512")]
        [InlineData("ShA256")]
        [InlineData("SHA256")]
        [InlineData("SHA384")]
        [InlineData("SHA512")]
        public void SignCommandArgParsing_ValidTimestampHashAlgorithm(string timestampHashAlgorithm)
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificatePath = @"\\path\file.pfx";
            var parsable = Enum.TryParse(timestampHashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedTimestampHashAlgorithm);
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificatePath = certificatePath,
                TimestampHashAlgorithm = timestampHashAlgorithm
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            Assert.True(parsable);
            var signArgs = signCommand.GetSignArgs();
            Assert.Equal(HashAlgorithmName.SHA256, signArgs.SignatureHashAlgorithm);
            Assert.Equal(parsedTimestampHashAlgorithm, signArgs.TimestampHashAlgorithm);
        }

        [Fact]
        public void SignCommandArgParsing_InvalidTimestampHashAlgorithm()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificatePath = @"\\path\file.pfx";
            var timestampHashAlgorithm = "MD5";
            var mockConsole = new Mock<IConsole>();
            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificatePath = certificatePath,
                TimestampHashAlgorithm = timestampHashAlgorithm
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.Equal(string.Format(InvalidArgException, nameof(signCommand.TimestampHashAlgorithm)), ex.Message);
        }

        [Fact]
        public void SignCommandArgParsing_ValidArgs_CertFingerprintAsync()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificateFingerprint = Sha256Hash;
            var hashAlgorithm = "sha256";
            Enum.TryParse(hashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedHashAlgorithm);
            var timestampHashAlgorithm = "sha512";
            Enum.TryParse(timestampHashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedTimestampHashAlgorithm);
            var storeName = "CertificateAuthority";
            Enum.TryParse(storeName, ignoreCase: true, result: out StoreName parsedStoreName);
            var storeLocation = "LocalMachine";
            Enum.TryParse(storeLocation, ignoreCase: true, result: out StoreLocation parsedStoreLocation);
            var nonInteractive = true;
            var overwrite = true;
            var outputDir = @".\test\output\path";
            var mockConsole = new Mock<IConsole>();
            mockConsole.Setup(c => c.Verbosity).Returns(Verbosity.Detailed);

            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateFingerprint = certificateFingerprint,
                CertificateStoreName = storeName,
                CertificateStoreLocation = storeLocation,
                HashAlgorithm = hashAlgorithm,
                TimestampHashAlgorithm = timestampHashAlgorithm,
                OutputDirectory = outputDir,
                NonInteractive = nonInteractive,
                Overwrite = overwrite,
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var signArgs = signCommand.GetSignArgs();

            Assert.Null(signArgs.CertificatePath);
            Assert.Equal(certificateFingerprint, signArgs.CertificateFingerprint, StringComparer.Ordinal);
            Assert.Null(signArgs.CertificateSubjectName);
            Assert.Equal(parsedStoreLocation, signArgs.CertificateStoreLocation);
            Assert.Equal(parsedStoreName, signArgs.CertificateStoreName);
            Assert.Equal(mockConsole.Object, signArgs.Logger);
            Assert.Equal(nonInteractive, signArgs.NonInteractive);
            Assert.Equal(overwrite, signArgs.Overwrite);
            Assert.Equal(packagePath, signArgs.PackagePaths[0], StringComparer.Ordinal);
            Assert.Equal(timestamper, signArgs.Timestamper, StringComparer.Ordinal);
            Assert.Equal(outputDir, signArgs.OutputDirectory, StringComparer.Ordinal);
        }

        [Fact]
        public void SignCommandArgParsing_ValidArgs_CertSubjectNameAsync()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificateSubjectName = new Guid().ToString();
            var hashAlgorithm = "sha256";
            Enum.TryParse(hashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedHashAlgorithm);
            var timestampHashAlgorithm = "sha512";
            Enum.TryParse(timestampHashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedTimestampHashAlgorithm);
            var storeName = "CertificateAuthority";
            Enum.TryParse(storeName, ignoreCase: true, result: out StoreName parsedStoreName);
            var storeLocation = "LocalMachine";
            Enum.TryParse(storeLocation, ignoreCase: true, result: out StoreLocation parsedStoreLocation);
            var nonInteractive = true;
            var overwrite = true;
            var outputDir = @".\test\output\path";
            var mockConsole = new Mock<IConsole>();
            mockConsole.Setup(c => c.Verbosity).Returns(Verbosity.Detailed);

            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateSubjectName = certificateSubjectName,
                CertificateStoreName = storeName,
                CertificateStoreLocation = storeLocation,
                HashAlgorithm = hashAlgorithm,
                TimestampHashAlgorithm = timestampHashAlgorithm,
                OutputDirectory = outputDir,
                NonInteractive = nonInteractive,
                Overwrite = overwrite,
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var signArgs = signCommand.GetSignArgs();

            Assert.Null(signArgs.CertificatePath);
            Assert.Null(signArgs.CertificateFingerprint);
            Assert.Equal(certificateSubjectName, signArgs.CertificateSubjectName, StringComparer.Ordinal);
            Assert.Equal(parsedStoreLocation, signArgs.CertificateStoreLocation);
            Assert.Equal(parsedStoreName, signArgs.CertificateStoreName);
            Assert.Equal(mockConsole.Object, signArgs.Logger);
            Assert.Equal(nonInteractive, signArgs.NonInteractive);
            Assert.Equal(overwrite, signArgs.Overwrite);
            Assert.Equal(packagePath, signArgs.PackagePaths[0], StringComparer.Ordinal);
            Assert.Equal(timestamper, signArgs.Timestamper, StringComparer.Ordinal);
            Assert.Equal(outputDir, signArgs.OutputDirectory, StringComparer.Ordinal);
        }

        [Fact]
        public void SignCommandArgParsing_ValidArgs_CertPathAsync()
        {
            // Arrange
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var certificatePath = @"\\path\file.pfx";
            var hashAlgorithm = "sha256";
            Enum.TryParse(hashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedHashAlgorithm);
            var timestampHashAlgorithm = "sha512";
            Enum.TryParse(timestampHashAlgorithm, ignoreCase: true, result: out HashAlgorithmName parsedTimestampHashAlgorithm);
            var storeName = "My";
            Enum.TryParse(storeName, ignoreCase: true, result: out StoreName parsedStoreName);
            var storeLocation = "CurrentUser";
            Enum.TryParse(storeLocation, ignoreCase: true, result: out StoreLocation parsedStoreLocation);
            var nonInteractive = true;
            var overwrite = true;
            var outputDir = @".\test\output\path";
            var mockConsole = new Mock<IConsole>();
            mockConsole.Setup(c => c.Verbosity).Returns(Verbosity.Detailed);

            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificatePath = certificatePath,
                HashAlgorithm = hashAlgorithm,
                TimestampHashAlgorithm = timestampHashAlgorithm,
                OutputDirectory = outputDir,
                NonInteractive = nonInteractive,
                Overwrite = overwrite,
            };

            signCommand.Arguments.Add(packagePath);

            // Act & Assert
            var signArgs = signCommand.GetSignArgs();

            Assert.Equal(certificatePath, signArgs.CertificatePath, StringComparer.Ordinal);
            Assert.Null(signArgs.CertificateFingerprint);
            Assert.Null(signArgs.CertificateSubjectName);
            Assert.Equal(parsedStoreLocation, signArgs.CertificateStoreLocation);
            Assert.Equal(parsedStoreName, signArgs.CertificateStoreName);
            Assert.Equal(mockConsole.Object, signArgs.Logger);
            Assert.Equal(nonInteractive, signArgs.NonInteractive);
            Assert.Equal(overwrite, signArgs.Overwrite);
            Assert.Equal(packagePath, signArgs.PackagePaths[0], StringComparer.Ordinal);
            Assert.Equal(timestamper, signArgs.Timestamper, StringComparer.Ordinal);
            Assert.Equal(outputDir, signArgs.OutputDirectory, StringComparer.Ordinal);
        }

        [Theory]
        [InlineData("sign")]
        [InlineData("siGn a b")]
        [InlineData("sign a -ConfigFile b x")]
        [InlineData("sign a -Timestamper b x")]
        public void SignCommand_Failure_InvalidArguments(string cmd)
        {
            Util.TestCommandInvalidArguments(cmd);
        }

        [Theory]
        [InlineData("89967D1DD995010B6C66AE24FF8E66885E6E03A8")] // 40 characters long SHA-1
        [InlineData("89967D1DD995010B6C66AE24FF8E66885E6E03")] // 38 characters long not SHA-1
        [InlineData("invalid-certificate-fingerprint")]
        public void SignCommandArgParsing_ThrowsAnExceptionWarningForInsecureCertificateFingerprint(string certificateFingerprint)
        {
            var signCommand = ArrangeSignCommand(certificateFingerprint);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => signCommand.GetSignArgs());
            Assert.True(ex.Message.Contains(InvalidCertificateFingerprintException));
        }

        private static SignCommand ArrangeSignCommand(string certificateFingerprint, StringBuilder logMessages = null)
        {
            var packagePath = @"\\path\package.nupkg";
            var timestamper = "https://timestamper.test";
            var hashAlgorithm = "sha256";
            var timestampHashAlgorithm = "sha512";
            var outputDir = @".\test\output\path";
            var mockConsole = new Mock<IConsole>();
            mockConsole.Setup(c => c.Verbosity).Returns(Verbosity.Detailed);
            mockConsole.Setup(c => c.Log(It.IsAny<ILogMessage>())).Callback<ILogMessage>((message) =>
            {
                logMessages?.AppendLine($"{message.Message}");
            });

            var signCommand = new SignCommand
            {
                Console = mockConsole.Object,
                Timestamper = timestamper,
                CertificateFingerprint = certificateFingerprint,
                HashAlgorithm = hashAlgorithm,
                TimestampHashAlgorithm = timestampHashAlgorithm,
                OutputDirectory = outputDir,
                NonInteractive = true,
                Overwrite = true,
            };

            signCommand.Arguments.Add(packagePath);

            return signCommand;
        }
    }
}
