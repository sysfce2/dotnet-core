// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Microsoft.NET.Build.Containers.IntegrationTests;

public sealed class LayerEndToEndTests : IDisposable
{
    private ITestOutputHelper _testOutput;

    public LayerEndToEndTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
        testSpecificArtifactRoot = new();
        priorArtifactRoot = ContentStore.ArtifactRoot;
        ContentStore.ArtifactRoot = testSpecificArtifactRoot.Path;
    }

    [Fact]
    public void SingleFileInFolder()
    {
        using TransientTestFolder folder = new();

        string testFilePath = Path.Join(folder.Path, "TestFile.txt");
        string testString = $"Test content for {nameof(SingleFileInFolder)}";

        File.WriteAllText(testFilePath, testString);

        Layer l = Layer.FromDirectory(directory: folder.Path, containerPath: "/app", false, SchemaTypes.DockerManifestV2);

        Console.WriteLine(l.Descriptor);

        //Assert.AreEqual("application/vnd.oci.image.layer.v1.tar", l.Descriptor.MediaType); // TODO: configurability
        Assert.True(l.Descriptor.Size is >= 135 and <= 500, $"'l.Descriptor.Size' should be between 135 and 500, but is {l.Descriptor.Size}"); // TODO: determinism!
        //Assert.AreEqual("sha256:26140bc75f2fcb3bf5da7d3b531d995c93d192837e37df0eb5ca46e2db953124", l.Descriptor.Digest); // TODO: determinism!

        VerifyDescriptorInfo(l);

        var allEntries = LoadAllTarEntries(l.BackingFile);
        Assert.True(allEntries.TryGetValue("app", out var appEntry) && appEntry.EntryType == TarEntryType.Directory, "Missing app directory entry");
        Assert.True(allEntries.TryGetValue("app/TestFile.txt", out var fileEntry) && fileEntry.EntryType == TarEntryType.RegularFile, "Missing TestFile.txt file entry");
    }

    [Fact]
    public void SingleFileInFolderWindows()
    {
        using TransientTestFolder folder = new();

        string testFilePath = Path.Join(folder.Path, "TestFile.txt");
        string testString = $"Test content for {nameof(SingleFileInFolder)}";

        File.WriteAllText(testFilePath, testString);

        Layer l = Layer.FromDirectory(directory: folder.Path, containerPath: "C:\\app", true, SchemaTypes.DockerManifestV2);

        var allEntries = LoadAllTarEntries(l.BackingFile);
        Assert.True(allEntries.TryGetValue("Files", out var filesEntry) && filesEntry.EntryType == TarEntryType.Directory, "Missing Files directory entry");
        Assert.True(allEntries.TryGetValue("Files/app", out var appEntry) && appEntry.EntryType == TarEntryType.Directory, "Missing Files/app directory entry");
        Assert.True(allEntries.TryGetValue("Files/app/TestFile.txt", out var fileEntry) && fileEntry.EntryType == TarEntryType.RegularFile, "Missing Files/app/TestFile.txt file entry");
        Assert.True(allEntries.TryGetValue("Hives", out var hivesEntry) && hivesEntry.EntryType == TarEntryType.Directory, "Missing Hives directory entry");

        // Enable after https://github.com/dotnet/runtime/issues/81699 is resolved
        // foreach (var entry in allEntries.Values)
        // {
        //     Assert.IsInstanceOfType(entry, typeof(PaxTarEntry));
        //     var pax = (PaxTarEntry)entry;
        //     Assert.IsTrue(pax.ExtendedAttributes.ContainsKey("MSWINDOWS.rawsd"),
        //         "Missing MSWINDOWS.rawsd definition for " + entry.Name);
        // }
    }

    [Fact] // https://github.com/dotnet/sdk/issues/40511
    public void SingleFileInHiddenFolder()
    {
        using TransientTestFolder folder = new();

        string childDirectory = Path.Join(folder.Path, "wwwroot");
        string grandchildDirectory = Path.Join(childDirectory, ".well-known");

        Directory.CreateDirectory(childDirectory);
        Directory.CreateDirectory(grandchildDirectory);

        string testFilePath = Path.Join(grandchildDirectory, "TestFile.txt");
        string testString = $"Test content for {nameof(SingleFileInHiddenFolder)}";

        File.WriteAllText(testFilePath, testString);

        Layer l = Layer.FromDirectory(directory: folder.Path, containerPath: "/app", false, SchemaTypes.DockerManifestV2);

        VerifyDescriptorInfo(l);

        var allEntries = LoadAllTarEntries(l.BackingFile);
        Assert.True(allEntries.TryGetValue("app", out var appEntry) && appEntry.EntryType == TarEntryType.Directory, "Missing app directory entry");
        Assert.True(allEntries.TryGetValue("app/wwwroot", out var wwwrootEntry) && wwwrootEntry.EntryType == TarEntryType.Directory, "Missing app/wwwroot directory entry");
        Assert.True(allEntries.TryGetValue("app/wwwroot/.well-known", out var wellKnownEntry) && wellKnownEntry.EntryType == TarEntryType.Directory, "Missing app/wwwroot/.well-known directory entry");
        Assert.True(allEntries.TryGetValue("app/wwwroot/.well-known/TestFile.txt", out var fileEntry) && fileEntry.EntryType == TarEntryType.RegularFile, "Missing app/wwwroot/.well-known/TestFile.txt file entry");
    }

    [Fact]
    public void UserIdIsAppliedToFiles()
    {
        using TransientTestFolder folder = new();

        string testFilePath = Path.Join(folder.Path, "TestFile.txt");
        string testString = $"Test content for {nameof(SingleFileInFolder)}";
        File.WriteAllText(testFilePath, testString);

        var userId = 1234;
        Layer l = Layer.FromDirectory(directory: folder.Path, containerPath: "/app", false, SchemaTypes.DockerManifestV2, userId: userId);
        var allEntries = LoadAllTarEntries(l.BackingFile);
        Assert.True(allEntries.TryGetValue("app", out var appEntry) && appEntry.EntryType == TarEntryType.Directory, "Missing app directory entry");
        Assert.True(allEntries.TryGetValue("app/TestFile.txt", out var fileEntry) && fileEntry.EntryType == TarEntryType.RegularFile, "Missing TestFile.txt file entry");
        Assert.All(allEntries.Values, entry =>
        {
            Assert.True(entry.Uid == userId, $"Expected UID {userId} for entry {entry.Name}, but got {entry.Uid}");
        });
    }

    private static void VerifyDescriptorInfo(Layer l)
    {
        Assert.Equal(l.Descriptor.Size, new FileInfo(l.BackingFile).Length);

        byte[] hashBytes;
        byte[] uncompressedHashBytes;

        using (FileStream fs = File.OpenRead(l.BackingFile))
        {
            hashBytes = SHA256.HashData(fs);

            fs.Position = 0;

            using (GZipStream decompressionStream = new(fs, CompressionMode.Decompress))
            {
                uncompressedHashBytes = SHA256.HashData(decompressionStream);
            }
        }

        Assert.Equal(Convert.ToHexStringLower(hashBytes), l.Descriptor.Digest.Substring("sha256:".Length));
        Assert.Equal(Convert.ToHexStringLower(uncompressedHashBytes), l.Descriptor.UncompressedDigest?.Substring("sha256:".Length));
    }

    TransientTestFolder? testSpecificArtifactRoot;
    string? priorArtifactRoot;

    public void Dispose()
    {
        testSpecificArtifactRoot?.Dispose();
        if (priorArtifactRoot is not null)
        {
            ContentStore.ArtifactRoot = priorArtifactRoot;
        }
    }


    private static Dictionary<string, TarEntry> LoadAllTarEntries(string file)
    {
        using var gzip = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        var entries = new Dictionary<string, TarEntry>();

        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) != null)
        {
            entries[entry.Name] = entry;
        }

        return entries;
    }
}
