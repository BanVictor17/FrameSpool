# LittleCMS color engine

FrameSpool embeds LittleCMS **2.19.1** to convert embedded RGB ICC v2/v4 profiles into sRGB before preview, thumbnails, corrections, printing and export. Conversion uses relative colorimetric intent and preserves alpha. An sRGB destination profile is created by the engine; Windows ICM and installed system profiles are not consulted. Untagged images are interpreted as sRGB. Damaged or non-RGB profiles still report an error rather than silently changing the meaning of their colors.

- Upstream: https://github.com/mm2/Little-CMS/releases/tag/lcms2.19.1
- Source: unmodified upstream `lcms2-2.19.1.zip`, included here.
- Source SHA256: `06d59795cb87a67f73daaa5b6f86bf77b256710aad5822e3fee7f89546a5f93f`
- License: MIT, Copyright (c) 2023 Marti Maria Saguer; see [LICENSE](LICENSE).
- Bundled binary: `native/lcms/lcms2.dll`, Windows x64, compiled with Strawberry Perl's MinGW-w64 GCC 13.2.0 / UCRT (Brecht Sanders r8). No plugins or external runtime DLLs are required beyond Windows 10/11's UCRT. Existing MinGW/GCC runtime notices are included under `third_party/LibRaw`.

Rebuild from the repository root with `powershell -ExecutionPolicy Bypass -File build-color-engine.ps1 -Compiler C:\Strawberry\c\bin\gcc.exe`. The script checks the upstream archive hash and compiles its unchanged C sources with `-O2 -shared -static-libgcc -DCMS_DLL -DCMS_DLL_BUILD -Wl,--no-insert-timestamp`. Normal .NET builds embed the checked-in DLL; a C compiler is only needed to rebuild that dependency.

The executable embeds the DLL, license, this notice and source archive. The DLL is extracted to a versioned, SHA256-checked per-user runtime cache and loaded by absolute path. Color transforms use separate contexts for concurrent jobs. Generated ICC v2/v4.4 fixtures verify conversion, thumbnails/corrected output, alpha, concurrency, source preservation and rejection of truncated profiles. They do not verify any user's unavailable original photo.
