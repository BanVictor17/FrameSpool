FrameSpool 0.2.4 fixes photo previews blocked by Windows ICC color-profile conversion errors, including error 2011.

- Embedded RGB ICC v2/v4 profiles now convert to sRGB with bundled LittleCMS 2.19.1. Conversion no longer relies on the host's Windows color engine or installed sRGB profile.
- The same conversion applies to thumbnails, main previews, image corrections, printing and export. Transparency is preserved and original photo files are never modified.
- Concurrent photo loading uses independent color contexts. Damaged and non-RGB profiles still report an explicit error rather than silently discarding color information.
- LittleCMS's MIT license, unmodified upstream source archive and build instructions are included. Help > About > Licenses can save its source, including from the one-file portable EXE.

Retains v0.2.3's inline Trim/Fit/magnification controls, synchronized Positioning palette beside the preview and cached adjacent-page navigation, plus persistent driver settings, DPI scaling and GitHub updates. Choose Help > Check for Updates to download/install and restart; preferences and frames are retained. Current photo selections close during updates.

Downloads: a single self-contained Windows 10/11 x64 portable EXE, per-user MSI installer, portable ZIP with documentation/notices/source, LibRaw source and SHA256 checksums. No separate .NET runtime or color-engine installation is needed.

Verification covers generated ICC v2/v4.4 color conversion, thumbnails/corrected output, alpha values, concurrency, truncated profiles and source preservation, alongside the existing RAW/layout/UI/driver/update fixtures. The reported photo was on another computer and could not be tested here. Physical L8050 output and full original-app equivalence remain unverified; see ROADMAP.md. New code is MIT; artwork and dependencies retain their separate notices. This is a regular release; binaries are unsigned.
