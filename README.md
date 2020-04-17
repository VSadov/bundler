# .net Bundler

This tool is a wrapper script to invoke the .net single-file bundler utilities provided by Microsoft.NET.HostModel library.
It is intended as a testing tool for the developement of single-file bundler. 
It facilitates testing of changes to Microsoft.NET.HostModel library and .net application host components before changes are committed and availalbe in the .net SDK.


# Usage

```
.NET Core Bundler
Usage: bundle <options>

Options:
  --source <PATH>    Directory containing files to bundle (required).
  --host <NAME>      Application host within source directory (required).
  --app  <NAME>      Managed app within source directory (default <host-name>.dll).
  --template <path>  Template Application host for this app.
  --os <os>          One of Win|Linux|Osx (default Win)
  --framework <tfm>  One of netcoreapp3.0|netcoreapp3.1|net5 (default 5.0)
  --pdb              Embed PDB files.
  --native           Embed native binaries.
  --content          Embed content files.
  --no-copy          Don't Copy excluded files to output directory.
  --no-trim          Don't trim excluded files copied to output directory.
  --no-bundle        Skip bundling, rewrite-host only
  -o|--output <PATH> Output directory (default: current).
  -d|--diagnostics   Enable diagnostic output.
  -?|-h|--help       Display usage information.
```

# Examples

To create a single-file bundle from a multi-file publish directory:
```
bundle --source <publish-dir> --host <host-exe> -o <output-dir>
```

To use a newly built apphost executable:
(This option simply embeds the app.dll path in the provided apphost-exe, so that it is runnable) 
```
bundle --no-bundle --source <publish-dir> --template <apphost-exe> --host <new-exe> --app <app.dll> -o <output-dir>
```

To achieve the effect of dotnet publish /p:PublishSingleFile using new host components:
```
bundle --source <publish-dir> --template <apphost-exe> --host <new-exe> --app <app.dll> -o <output-dir>
```
