# FrameSpool

A portable Windows application for selecting photos, arranging print layouts, cropping, applying corrections and printing through installed Windows drivers.

FrameSpool runs from one executable. Its built-in artwork, icon and 135 layout definitions are embedded; no companion assets folder or reference application installation is required. It opens without a connected printer. This is version 0.1.0, with incomplete features and output comparisons listed in [ROADMAP.md](ROADMAP.md).

## Run

Download a preview ZIP from [Releases](https://github.com/BanVictor17/FrameSpool/releases), build locally, or download a successful Windows workflow's `FrameSpool-Windows` artifact from [GitHub Actions](https://github.com/BanVictor17/FrameSpool/actions). Extract it and open `FrameSpool.exe`. Keep the license and asset notices with redistributed copies.

Requirements: Windows with .NET Framework 4.8 or later. Physical printing requires the printer manufacturer's Windows driver. The .NET runtime is supplied by Windows/the installed framework; this executable does not bundle a new runtime. The app has no dependency on REA, Node, Ghidra or a vendor photo-printing SDK.

Select a photo folder, click photos to select them, and set copies with plus/minus. Choose **Next Step** to select paper and a layout. **Positioning** adjusts each placed copy's cropping, scale and rotation. **Image Correct** changes printed/exported colors; the main layout preview keeps the original colors.

**Manual** opens a palette for placing, moving and removing photos. Repeated copies have independent cropping and shared source corrections. **Frame Menu > Create/Edit Frame** creates frames with rectangular/oval openings, artwork, text and lines. Editable frames use a new `.eppframe.json` format.

Preferences, Quick Print presets, custom frames and error logs live under `%LOCALAPPDATA%\FrameSpool`. The previous development app's settings remain in their old folder and are not migrated automatically. Source photographs are not rewritten.

## Printing

Select a queue under **Printer Settings**, then use **Properties...** for the driver's media, quality and borderless options. Those driver settings are preserved with presets. Generic media/expansion controls are not substitutes for driver-specific settings.

The L8050 path uses its normal Windows driver, with no old compatibility whitelist. Physical L8050 output has not yet been tested. See the printer validation tasks in the roadmap.

Use **Microsoft Print to PDF** without hardware, or **File > Export pages** for 300dpi PNG pages. **File > Manual Print** sends the original colors to the driver without app correction filters.

## Build and verify

From PowerShell in the repository folder:

```powershell
.\build.ps1
.\verify.ps1
```

The build uses the Windows .NET Framework compiler and writes `dist\FrameSpool\FrameSpool.exe`. `FrameSpool.csproj` supports MSBuild/Visual Studio with the .NET Framework 4.8 targeting pack. `pack-assets.ps1` creates the embedded data archive during either build route.

The verification script copies only the executable into a temporary folder and runs its deterministic tests using generated photographs. It checks layout geometry, source preservation, pagination, independent placement, correction behavior, form rendering and available driver settings. It does not send a physical print job. GitHub Actions also builds and runs this check.

The separate `--print-test <verification-output-folder>` command prints the generated fixture to Microsoft Print to PDF. It is an optional virtual-printer integration check, not a physical printer test.

## License

The new code and build scripts use [MIT](LICENSE). Built-in third-party data has separate ownership and permission status described in [ASSET_NOTICES.md](ASSET_NOTICES.md). MIT does not apply to those assets.
