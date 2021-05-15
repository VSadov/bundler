// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.IO.Compression;

namespace Bundle
{
    /// <summary>
    ///  The main driver for Bundle and Extract operations.
    /// </summary>
    public static class Program
    {
        enum RunMode
        {
            Help,
            Bundle,
            CreateHost
        };

        // unbundle option
        static string UnbundleFile = null;

        // Modes
        static bool NeedHelp = false;
        static bool CreateHost = false;
        static bool CreateBundle = true;

        // Common Options:
        static bool Diagnostics = false;
        static string OutputDir;

        // Bundle options:
        static string SourceDir;
        static string Host;
        static string Template;
        static string App;
        static BundleOptions Options = BundleOptions.None;
        static Architecture Architecture = Architecture.X64;
        static OSPlatform OS;
        static Version Framework;
        static bool CopyExcluded = true;

        static void Usage()
        {
            Console.WriteLine($".NET Core Bundler");
            Console.WriteLine("Usage: bundle <options>");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  --source <PATH>    Directory containing files to bundle (required).");
            Console.WriteLine("  --host <NAME>      Application host within source directory (required).");
            Console.WriteLine("  --app  <NAME>      Managed app within source directory (default <host-name>.dll).");
            Console.WriteLine("  --template <path>  Template Application host for this app.");
            Console.WriteLine("  --os <os>          One of Win|Linux|Osx (default Win)");
            Console.WriteLine("  --framework <tfm>  One of netcoreapp3.0|netcoreapp3.1|net5|net6 (default net6)");
            Console.WriteLine("  --pdb              Embed PDB files.");
            Console.WriteLine("  --native           Embed native binaries.");
            Console.WriteLine("  --content          Embed content files.");
            Console.WriteLine("  -a|--arch <arch>   One of x86|x64|arm|arm64 (default x64).");
            Console.WriteLine("  --compress         Enable compression.");
            Console.WriteLine("  --skip-excluded    Don't Copy excluded files to output directory.");
            Console.WriteLine("  --no-bundle        Skip bundling, rewrite-host only");
            Console.WriteLine("  -o|--output <PATH> Output directory (default: current).");
            Console.WriteLine("  -d|--diagnostics   Enable diagnostic output.");
            Console.WriteLine("  -?|-h|--help       Display usage information.");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("bundle --source <publish-dir> --host <host-exe> -o <output-dir>");
            Console.WriteLine("bundle --source <publish-dir> --template <template-host-exe> --host <new-exe> --app <app.dll> -o <output-dir>");
        }

        static void Fail(string type, string message)
        {
            Console.Error.WriteLine($"{type}: {message}");
        }

        static void ParseArgs(string[] args)
        {
            int i = 0;
            Func<string, string> NextArg = (string option) =>
            {
                if (++i >= args.Length)
                {
                    throw new ArgumentException("Argument missing for" + option);
                }
                return args[i];
            };

            string os = "win";
            string arch = "x64";
            string framework = "net6";

            for (; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg.ToLower())
                {
                    case "-?":
                    case "-h":
                    case "--help":
                        NeedHelp = true;
                        break;

                    case "-d":
                    case "--diagnostics":
                        Diagnostics = true;
                        break;

                    case "--pdb":
                        Options |= BundleOptions.BundleSymbolFiles;
                        break;

                    case "--native":
                        Options |= BundleOptions.BundleNativeBinaries;
                        break;

                    case "--content":
                        Options |= BundleOptions.BundleAllContent;
                        break;

                    case "--compress":
                        Options |= BundleOptions.EnableCompression;
                        break;

                    case "--host":
                        Host = NextArg(arg);
                        break;

                    case "--app":
                        App = NextArg(arg);
                        break;

                    case "--template":
                        CreateHost = true;
                        Template = NextArg(arg);
                        break;

                    case "--source":
                        SourceDir = NextArg(arg);
                        break;

                    case "-o":
                    case "--output":
                        OutputDir = NextArg(arg);
                        break;

                    case "--os":
                        os = NextArg(arg).ToLower();
                        break;

                    case "-a":
                    case "-arch":
                        arch = NextArg(arg).ToLower();
                        break;

                    case "--framework":
                        framework = NextArg(arg);
                        break;

                    case "--skip-excluded":
                        CopyExcluded = false;
                        break;

                    case "--no-bundle":
                        CreateBundle = false;
                        break;

                    case "--unbundle":
                        UnbundleFile = NextArg(arg);
                        break;

                    default:
                        throw new ArgumentException("Invalid option: " + arg);
                }
            }

            switch (os)
            {
                case "win":
                    OS = OSPlatform.Windows;
                    break;

                case "linux":
                    OS = OSPlatform.Linux;
                    break;

                case "osx":
                    OS = OSPlatform.OSX;
                    break;

                default:
                    throw new ArgumentException("Unknown OS");
            }

            switch (arch)
            {
                case "x86":
                    Architecture = Architecture.X86;
                    break;

                case "x64":
                    Architecture = Architecture.X64;
                    break;

                case "arm":
                    Architecture = Architecture.Arm;
                    break;

                case "arm64":
                    Architecture = Architecture.Arm64;
                    break;

                default:
                    throw new ArgumentException("Unknown OS");
            }

            switch (framework)
            {
                case "netcoreapp3.0":
                    Framework = new Version(3, 0);
                    break;

                case "netcoreapp3.1":
                    Framework = new Version(3, 1);
                    break;

                case "net5":
                    Framework = new Version(5, 0);
                    break;

                case "net6":
                    Framework = new Version(6, 0);
                    break;

                default:
                    throw new ArgumentException("Unknown Framework");
            }

            if (UnbundleFile == null)
            {
                if (SourceDir == null)
                {
                    throw new ArgumentException("Missing argument: --source");
                }

                if (Host == null)
                {
                    throw new ArgumentException("Missing argument: --host");
                }

                if (CreateHost)
                {
                    if (Template == null)
                    {
                        throw new ArgumentException("Missing argument: --template");
                    }

                    if (App == null)
                    {
                        App = Path.GetFileNameWithoutExtension(Host) + ".dll";
                    }
                }
            }

            if (OutputDir == null)
            {
                OutputDir = Environment.CurrentDirectory;
            }
        }

        public static void Main(string[] args)
        {
            try
            {
                ParseArgs(args);
            }
            catch (ArgumentException e)
            {
                Fail("ERROR", e.Message);
                Usage();
                return;
            }

            if (NeedHelp)
            {
                Usage();
                return;
            }

            if (UnbundleFile != null)
            {
                Unbundle(UnbundleFile, OutputDir);
                return;
            }

            if (CreateHost)
            {
                HostWriter.CreateAppHost(Template, Path.Combine(SourceDir, Host), App);
            }

            if (!CreateBundle)
            {
                return;
            }

            Bundler bundler = new Bundler(Host, OutputDir, Options, OS, Architecture, Framework, Diagnostics);

            // Get all files in the source directory and all sub-directories.
            string[] sources = Directory.GetFiles(SourceDir, searchPattern: "*", searchOption: SearchOption.AllDirectories);

            // Sort the file names to keep the bundle construction deterministic.
            Array.Sort(sources, StringComparer.Ordinal);

            List<FileSpec> fileSpecs = new List<FileSpec>(sources.Length);
            foreach (var file in sources)
            {
                fileSpecs.Add(new FileSpec(file, Path.GetRelativePath(SourceDir, file)));
            }

            bundler.GenerateBundle(fileSpecs);

            if (!CopyExcluded)
            {
                return;
            }

            foreach (var spec in fileSpecs)
            {
                if (spec.Excluded)
                {
                    var outputPath = Path.Combine(OutputDir, spec.BundleRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                    File.Copy(spec.SourcePath, outputPath, true);
                }
            }
        }

        private static void Unbundle(string inputFile, string outputDir)
        {
            Microsoft.NET.HostModel.RetryUtil.RetryOnIOError(() =>
            {
                byte[] bundleMarker = {
                    // 32 bytes represent the bundle signature: SHA-256 for ".net core bundle"
                    0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
                    0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
                    0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
                    0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
                };

                // TODO: this helper is internal
                // int markerLocation = BinaryUtils.SearchInFile(memoryMappedViewAccessor, pattern);
                int markerLocation = BinaryUtils.SearchInFile(inputFile, bundleMarker);

                using var input = new FileStream(inputFile, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(input);

                input.Seek(markerLocation - sizeof(long), SeekOrigin.Begin);
                long manifestOffset = reader.ReadInt64();

                // parse manifest
                input.Seek(manifestOffset, SeekOrigin.Begin);
                var manifest = Manifest.FromReader(reader);

                // extract contained files
                byte[] buffer = new byte[4096];
                long bundleStart = manifestOffset;
                foreach (var file in manifest.Files)
                {
                    var filePath = Path.Combine(outputDir, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write);

                    if (file.Offset < bundleStart)
                    {
                        bundleStart = file.Offset;
                    }

                    input.Seek(file.Offset, SeekOrigin.Begin);
                    if (file.CompressedSize == 0)
                    {
                        // copy
                        CopyBytes(input, output, file.Size, buffer);
                    }
                    else
                    {
                        // decompress
                        using var decompress = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
                        int read;
                        while ((read = decompress.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                        }
                    }
                }

                // copy host file up to the bundle location
                var hostPath = Path.Combine(outputDir, Path.GetFileName(inputFile));
                input.Seek(0, SeekOrigin.Begin);
                using (var host = new FileStream(hostPath, FileMode.Create, FileAccess.Write))
                {
                    CopyBytes(input, host, bundleStart, buffer);
                    host.Seek(markerLocation - sizeof(long), SeekOrigin.Begin);
                    // erase manifest offset
                    host.Write(stackalloc byte[sizeof(long)]);
                }

                // fix up host if it is a Mach-O
                // TODO: this helper is internal
                // Microsoft.NET.HostModel.AppHost.MachOUtils.AdjustHeadersForBundle(hostPath);
                typeof(Bundler).Assembly.GetType("Microsoft.NET.HostModel.AppHost.MachOUtils").InvokeMember(
                    "AdjustHeadersForBundle", 
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.InvokeMethod, 
                    null, 
                    null, 
                    new object[]{hostPath});

            });
        }

        private static void CopyBytes(FileStream input, FileStream output, long toCopy, byte[] buffer)
        {
            while (toCopy > 0)
            {
                var chunk = (int)Math.Min(toCopy, buffer.Length);
                input.Read(buffer, 0, chunk);
                output.Write(buffer, 0, chunk);
                toCopy -= chunk;
            }
        }

        public class Manifest : Microsoft.NET.HostModel.Bundle.Manifest
        {
            private Manifest(uint bundleMajorVersion, bool netcoreapp3CompatMode = false) : 
                base(bundleMajorVersion, netcoreapp3CompatMode) 
            {}

            public static Manifest FromReader(BinaryReader reader)
            {
                var majorVersion = reader.ReadUInt32();
                var minorVersion = reader.ReadUInt32();
                var fileCount    = reader.ReadInt32();
                var bundleID     = reader.ReadString();

                bool netcoreapp3CompatMode = false;
                if (majorVersion >= 2)
                {
                    var depsJsonOffset = reader.ReadInt64();
                    var depsJsonSize = reader.ReadInt64();

                    var runtimeconfigJsonOffset = reader.ReadInt64();
                    var runtimeconfigJsonSize = reader.ReadInt64();

                    var flags = reader.ReadUInt64();
                    netcoreapp3CompatMode = flags != 0;
                }

                var manifest = new Manifest(majorVersion, netcoreapp3CompatMode);

                for (int i = 0; i < fileCount; i++)
                {
                    var offset = reader.ReadInt64();
                    var size = reader.ReadInt64();

                    // compression is used only in version 6.0+
                    var compressedSize = majorVersion >= 6 ? reader.ReadInt64() : 0;
                    var fileType = (FileType)reader.ReadByte();
                    var relativePath = reader.ReadString();

                    manifest.AddEntry(fileType, relativePath, offset, size, compressedSize, majorVersion);
                }

                return manifest;
            }
        }
    }
}
