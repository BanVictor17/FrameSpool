# FrameSpool dependency notices

FrameSpool's newly written code is MIT licensed. Recovered artwork and layout data have separate permission terms in ASSET_NOTICES.md.

The portable executable includes the Microsoft .NET 10 Windows Desktop runtime, redistributed under its MIT license and third-party notices. Runtime source is available at https://github.com/dotnet/runtime and https://github.com/dotnet/wpf and https://github.com/dotnet/winforms. The exact runtime packages and their notices are recorded by the build; notices are embedded and available under Help > About > Licenses.

LibRaw 0.22.2 is redistributed under CDDL 1.0. Its unmodified source archive, COPYRIGHT, additional notices, CDDL terms and build instructions are included in third_party/LibRaw. Static GCC libraries use the GCC Runtime Library Exception; MinGW-w64/UCRT and winpthreads notices are included. This does not relicense LibRaw as MIT.

The executable embeds these license texts and the LibRaw source archive. Use Help > About > Licenses to read the notices or save the decoder's corresponding source. The repository and release source archive also provide them. Portable ZIP and MSI distributions include these notices as files.

No reference application's executables, DLLs or printer SDK are bundled. Build tools (.NET SDK and WiX) are downloaded for compilation and are not included in the app.
