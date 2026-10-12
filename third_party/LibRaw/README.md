# Bundled RAW decoder

FrameSpool launches two unmodified LibRaw 0.22.2 sample programs in hidden child processes: `dcraw_emu.exe` and `raw-identify.exe`. They are embedded in the portable app and extracted into the user's FrameSpool runtime cache when needed. The binaries are built from upstream source with static MinGW-w64 libraries; they require only the Windows 10/11 system UCRT, kernel and Winsock. No camera codec, printer vendor software, Visual C++ redistributable installation or network connection is required for decoding.

LibRaw is used under **CDDL 1.0**. FrameSpool's MIT license does not change LibRaw's license. `LICENSE.CDDL`, upstream `COPYRIGHT`, additional third-party copyright/license notices, the GCC Runtime Library Exception and the MinGW-w64 licenses are included here. The original LGPL alternative is retained for reference. No changes were made to LibRaw source code.

## Corresponding source and provenance

The complete, unmodified corresponding source archive is included as `third_party/LibRaw/LibRaw-0.22.2.zip` in the FrameSpool repository. Recipients can obtain it from the same repository/release source archive as FrameSpool, or directly from the official project:

- Release page: https://www.libraw.org/download
- Source archive: https://www.libraw.org/data/LibRaw-0.22.2.zip
- Source archive SHA-256: `F4448371523F9960D26A131ED9DC21BEE49F967ADBCC4FB6F56B9B47C2BA87C6`
- Compiler: installed Strawberry Perl MinGW-w64 x86_64-UCRT-posix-SEH GCC 13.2.0, built by Brecht Sanders r8; MinGW-w64 headers 11.0.1. GCC compilation uses no third-party optimizer or modified compiler intermediate representations.
- `native/libraw/dcraw_emu.exe` SHA-256: `8AFB63CF9000760AE427512EDDFD929B343C786EF1577FF516773FFCA8A0EF8A`
- `native/libraw/raw-identify.exe` SHA-256: `0FBB71B1AC7C4F8AD4249B524449DC9852F118490CBFFBEB3326250693A2D16D`

The official Windows binary package was examined but is **not shipped**: its MSVC runtime imports would introduce an extra machine prerequisite.

## Rebuilding

Extract the included source ZIP. With MinGW-w64 GCC, GNU Make, `ar`, `ranlib` and `rm` on PATH, run from `LibRaw-0.22.2`:

```text
mingw32-make -f Makefile.mingw -j4 library bin/dcraw_emu bin/raw-identify CFLAGS="-O2 -I. -w -DLIBRAW_NODLL -static -static-libgcc -static-libstdc++"
```

Copy `bin/dcraw_emu.exe` and `bin/raw-identify.exe` into `native/libraw`, then rebuild FrameSpool. The timestamped PE build will not necessarily reproduce the identical hashes. Preserve this notice, the licenses and complete corresponding source when redistributing modified builds.

## Supported behavior and limits

- LibRaw develops supported camera RAW data into 8-bit sRGB using AHD interpolation, camera color matrices, camera orientation and as-shot white balance.
- RAW corrections provide 13 exposure choices from -3 to +3 EV in half-stop increments and seven white balance choices. Camera-native presets are used when the file provides them. Missing presets use independent relative-temperature approximations; these do not reproduce an individual camera's preset calibration.
- RAW identification/processing happens in a separate process, with timeouts, a 1 GB input size limit and a 100-megapixel output limit. Source files are read/copied and never modified. Decoded TIFF cache files are limited to roughly 512 MB, except a single active large image. Unicode paths are handled by copying inputs to temporary ASCII filenames. Transient copies are deleted after each job.
- Supported extensions are candidates for decoding, not a promise that every camera compression mode is supported. Unsupported or corrupt files report an error. This build does not include Adobe's DNG SDK, GoPro's GPR SDK, RawSpeed, external JPEG/zlib or LittleCMS plugins: JPEG-XL/lossy or deflated-float DNG and GPR modes requiring those dependencies are not supported. LibRaw's built-in lossless JPEG RAW decoder is included.
- A generated Bayer DNG verifies full development, metadata, Unicode filenames, exposure, white balance, monochrome, rotation, corrupt-file handling and source immutability. Real camera coverage has not been exhaustively tested.

Embedded RGB ICC profiles in bitmap photos are independently converted to sRGB through bundled LittleCMS 2.19.1, independently of Windows system profiles before adjustments. Generated ICC v2/v4.4 profiles with swapped primaries verify actual color conversion, concurrency and alpha preservation. See ../LittleCMS/README.md for color-engine source, license and build details. Malformed and non-RGB profiles are rejected with a clear message; images without profiles are treated as sRGB. Printer output color management remains the selected Windows driver's responsibility.
