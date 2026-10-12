FrameSpool 0.2.3 improves photo positioning and next/previous navigation.

- Positioning opens inside the app over the left sidebar, beside the preview, rather than beyond the right edge. Its location is kept within the monitor's working area.
- Trim, Fit and a 50–200% magnification slider are always available beneath the page arrows. They synchronize immediately with the selected photo and the Positioning palette when open. Empty slots disable the controls.
- Adjacent pages are pre-rendered and cached for immediate normal next/previous navigation. Cold pages render in the background without clearing the existing preview or flashing “Loading preview”. Crops, captions, corrections, layout changes and source-file changes invalidate old snapshots.
- Preview storage is bounded to eight entries and 64 MiB. Closed windows and stale renders release their images safely.

Includes the v0.2.2 fixes for accepted printer settings, native driver persistence, DPI scaling and GitHub updates. Existing v0.2.2 users can choose Help > Check for Updates, then download/install and restart. Current photo selections close during updates; preferences and frames are retained.

Downloads: a single self-contained Windows 10/11 x64 portable EXE, per-user MSI installer, portable ZIP with documentation/notices, decoder source and SHA256 checksums. No separate .NET runtime is needed.

Verification covers positioning synchronization, palette placement, immediate cached navigation, crop/source invalidation, cache bounds, DPI layouts, RAW/ICC/layout/driver/workflow fixtures, portable replacement and MSI upgrade/uninstall. Physical L8050 output and full original-app equivalence remain unverified; see ROADMAP.md. New code is MIT; artwork and dependencies retain their separate notices. This is a regular release; binaries are unsigned.
