FrameSpool 0.2.2 fixes printer configuration and laptop scaling, and adds GitHub updates.

- Fixes the false “printer rejected this combination” error: successfully resolved driver settings are accepted. Properties stays accessible when saved choices are obsolete.
- Saves accepted media, quality, resolution, source and private driver settings immediately when settings or the print dialog are accepted. Future jobs restore the native configuration without stale overrides, including after a failed job.
- Scales fonts, controls, artwork, thumbnails and layout lists at Windows 125%, 150% and 200% scaling, and adapts when moving between monitors. Smaller displays can scroll the workspace.
- Adds Help > Check for Updates and an About button. Stable GitHub downloads are verified against their SHA256 digests before updating and restarting. Portable copies are replaced atomically with rollback; installed copies upgrade through MSI, retaining their install directory.

Download the portable EXE for a single self-contained Windows 10/11 x64 app, or the MSI for a per-user installation with Start menu shortcut and uninstall support. The ZIP includes documentation and notices. Existing v0.2.1 users must download v0.2.2 manually to get the updater.

Preferences and frames survive updates. Restarting closes the current photo selection. Updates are requested manually and require internet access and a writable app folder. The app runs offline otherwise.

Verification covers synthetic RAW/ICC/layout/workflow fixtures, Windows printer setting round trips, four DPI scales, download cancellation/corruption and rollback, actual portable helper replacement, MSI install/helper upgrade/uninstall, and Microsoft Print to PDF output.

Physical L8050 output still needs testing with the updated app. Complete original-app output equivalence, PRINT Image Matching, exact proprietary correction algorithms and some old template formats remain unverified or unavailable; see ROADMAP.md. New code is MIT; redistributed artwork and dependencies have separate notices. This is a regular release; binaries are unsigned.
