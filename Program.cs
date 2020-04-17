// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;

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
        static OSPlatform OS;
        static Version Framework;
        static bool CopyExcluded = true;
        static bool TrimExcluded = true;

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
            Console.WriteLine("  --framework <tfm>  One of netcoreapp3.0|netcoreapp3.1|net5 (default 5.0)");
            Console.WriteLine("  --pdb              Embed PDB files.");
            Console.WriteLine("  --native           Embed native binaries.");
            Console.WriteLine("  --content          Embed content files.");
            Console.WriteLine("  --no-copy          Don't Copy excluded files to output directory.");
            Console.WriteLine("  --no-trim          Don't trim excluded files copied to output directory.");
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
            string framework = "net5";

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

                    case "--framework":
                        framework = NextArg(arg);
                        break;

                    case "--no-copy":
                        CopyExcluded = true;
                        break;

                    case "--no-trim":
                        TrimExcluded = false;
                        break;

                    case "--no-bundle":
                        CreateBundle = false;
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

                default:
                    throw new ArgumentException("Unknown Framework");
            }

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

            if (CreateHost)
            {
                HostWriter.CreateAppHost(Template, Path.Combine(SourceDir, Host), App);
            }

            if (!CreateBundle)
            {
                return;
            }

            Bundler bundler = new Bundler(Host, OutputDir, Options, OS, Framework, Diagnostics);

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
                    if (TrimExcluded &&
                        !spec.BundleRelativePath.Contains("coreclr") &&
                        !spec.BundleRelativePath.Contains("clrjit") &&
                        !spec.BundleRelativePath.Contains("clrcompression") &&
                        !spec.BundleRelativePath.Contains("hostfxr") &&
                        !spec.BundleRelativePath.Contains("hostpolicy"))
                    {
                        continue;
                    }

                    File.Copy(spec.SourcePath, Path.Combine(OutputDir, spec.BundleRelativePath), true);
                }
            }

        }
    }
}
