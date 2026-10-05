// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.CoreSetup.Test;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.DotNet.CoreSetup.Packaging.Tests
{
    public class NuGetArtifactTester : IDisposable
    {
        public static NuGetArtifactTester Open(
            RepoDirectoriesProvider dirs,
            string project,
            string id = null)
        {
            var tester = OpenOrNull(dirs, project, id);
            Assert.NotNull(tester);
            return tester;
        }

        public static NuGetArtifactTester OpenOrNull(
            RepoDirectoriesProvider dirs,
            string project,
            string id = null)
        {
            id = id ?? project;

            string nupkgPath = Path.Combine(
                dirs.BaseArtifactsFolder,
                "packages",
                HostTestContext.Configuration,
                "Shipping",
                $"{id}.{HostTestContext.MicrosoftNETCoreAppVersion}.nupkg");

            // If the nuspec exists, the nupkg should exist.
            Assert.True(File.Exists(nupkgPath));

            return new NuGetArtifactTester(nupkgPath);
        }

        public PackageIdentity Identity { get; }
        public NuGetVersion PackageVersion { get; }

        private readonly PackageArchiveReader _reader;

        public NuGetArtifactTester(string file)
        {
            _reader = new PackageArchiveReader(ZipFile.Open(file, ZipArchiveMode.Read));
            Identity = _reader.NuspecReader.GetIdentity();
            PackageVersion = _reader.NuspecReader.GetVersion();
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        public void IsTargetingPack()
        {
            IsFrameworkPack();

            Assert.NotEmpty(_reader.GetFiles("ref"));
            Assert.Empty(_reader.GetFiles("runtimes"));
            Assert.Empty(_reader.GetFiles("lib"));

            ContainsFrameworkList("FrameworkList.xml");
        }

        public void IsTargetingPackForPlatform()
        {
            IsFrameworkPack();

            HasGoodPlatformManifest();
        }

        public void IsAppHostPack()
        {
            IsRuntimeSpecificPack();
        }

        public void IsRuntimePack()
        {
            IsRuntimeSpecificPack();

            HasOnlyTheseDataFiles(
                "data/RuntimeList.xml");

            ContainsFrameworkList("RuntimeList.xml");
        }

        public void Has16KiBAlignedNativeLibraries()
        {
            const ulong PageSize = 16384;
            const uint LoadSegment = 1;
            const uint RelroSegment = 0x6474e552;
            const int ElfHeaderSize = 64;
            const int ProgramHeaderSize = 56;

            string[] libraries = _reader.GetFiles("runtimes")
                .Where(path => path.EndsWith(".so", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(libraries);

            foreach (string library in libraries)
            {
                using Stream input = _reader.GetEntry(library).Open();
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                byte[] image = buffer.ToArray();
                Assert.True(image.Length >= ElfHeaderSize, library);
                Assert.Equal(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1 }, image[..6]);

                // ELF64 program header layout: https://refspecs.linuxfoundation.org/elf/gabi4+/ch4.eheader.html
                ulong programHeadersOffset = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(32));
                ushort entrySize = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(54));
                ushort count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(56));
                Assert.Equal(ProgramHeaderSize, entrySize);
                Assert.True(count > 0, library);
                Assert.True(checked(programHeadersOffset + (ulong)entrySize * count) <= (ulong)image.Length, library);
                int loadSegments = 0;
                for (int index = 0; index < count; index++)
                {
                    ReadOnlySpan<byte> header = image.AsSpan(checked((int)(programHeadersOffset + (ulong)index * entrySize)), entrySize);
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(header);
                    ulong virtualAddress = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
                    if (type == LoadSegment)
                    {
                        ulong fileOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
                        ulong alignment = BinaryPrimitives.ReadUInt64LittleEndian(header[48..]);
                        Assert.True(alignment >= PageSize && (alignment & (alignment - 1)) == 0,
                            $"{library}: PT_LOAD alignment is {alignment:x}.");
                        Assert.True(fileOffset % PageSize == virtualAddress % PageSize,
                            $"{library}: PT_LOAD offset and virtual address are not congruent for 16 KiB pages.");
                        loadSegments++;
                    }
                    else if (type == RelroSegment)
                    {
                        ulong memorySize = BinaryPrimitives.ReadUInt64LittleEndian(header[40..]);
                        Assert.True(checked(virtualAddress + memorySize) % PageSize == 0,
                            $"{library}: PT_GNU_RELRO end is not 16 KiB aligned.");
                    }
                }
                Assert.True(loadSegments > 0, library);
            }
        }

        public void HasOnlyTheseDataFiles(params string[] expectedDataFiles)
        {
            HashSet<string> dataFileSet = _reader.GetFiles("data").ToHashSet();

            Assert.True(
                dataFileSet.SetEquals(expectedDataFiles),
                "Invalid set of data files: " +
                    $"expected '{string.Join(", ", expectedDataFiles)}', " +
                    $"actual '{string.Join(", ", dataFileSet)}'");
        }

        public void HasGoodPlatformManifest()
        {
            string platformManifestContent = ReadEntryContent(
                _reader.GetEntry("data/PlatformManifest.txt"));

            // Sanity: check if the manifest has some content.
            Assert.Contains(".dll", platformManifestContent);

            // Check that the lines contain the package ID where they're supposed to.
            foreach (var parts in platformManifestContent
                .Split('\r', '\n')
                .Select(line => line.Split("|"))
                .Where(parts => parts.Length > 1))
            {
                Assert.True(
                    parts[1] == Identity.Id,
                    $"Platform manifest package id column '{parts[1]}' doesn't match " +
                        $"actual package id '{Identity.Id}'");
            }
        }

        public string ReadEntryContent(string entry)
        {
            return ReadEntryContent(_reader.GetEntry(entry));
        }

        public XDocument ReadEntryXDocument(string entry)
        {
            return ReadEntryXDocument(_reader.GetEntry(entry));
        }

        private void IsFrameworkPack()
        {
            Assert.Empty(_reader.GetPackageDependencies());

            var expectedTypes = new[] { new PackageType("DotnetPlatform", new Version(0, 0)) };
            var types = _reader.GetPackageTypes().ToArray();
            Assert.Equal(expectedTypes, types);
        }

        private void IsRuntimeSpecificPack()
        {
            IsFrameworkPack();

            Assert.Empty(_reader.GetFiles("ref"));
            Assert.NotEmpty(_reader.GetFiles("runtimes"));
            Assert.Empty(_reader.GetFiles("lib"));
        }

        private void ContainsFrameworkList(string name)
        {
            XDocument frameworkList = ReadEntryXDocument(
                _reader.GetEntry($"data/{name}"));

            XElement[] frameworkListFiles = frameworkList
                .Element("FileList")
                .Elements("File")
                .ToArray();

            // Sanity: check if the list has some content.
            Assert.NotEmpty(frameworkListFiles);
        }

        private static string ReadEntryContent(ZipArchiveEntry entry)
        {
            using (var reader = new StreamReader(entry.Open()))
            {
                return reader.ReadToEnd();
            }
        }

        private static XDocument ReadEntryXDocument(ZipArchiveEntry entry)
        {
            return XDocument.Parse(ReadEntryContent(entry));
        }
    }
}
