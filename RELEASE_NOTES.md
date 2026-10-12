FrameSpool 0.2.5 fixes browser continuity, window persistence and photo selection order.

- Switching to Print Settings and back preserves expanded folders, the selected folder, thumbnails, photo scroll and browser focus. Returning no longer reloads the folder or rebuilds the explorer.
- The folder explorer adds Back, Forward, Up, Refresh, a folder picker and a typeable folder address. Alt+Left/Right/Up navigate, Ctrl+L focuses the address and F5 refreshes folders/photos. Refresh detects added/removed folders and drives while keeping existing expanded branches. The tree menu offers open, refresh, expand/collapse and copy path.
- Normal window bounds and maximized state persist across restarts. Closing while minimized retains the last normal/maximized state. Saved windows are clamped to the available monitor area; damaged preferences fall back safely.
- Individual photo selections preserve click order. Select All follows the visible grid left-to-right, then top-to-bottom, honoring the current sort direction. Selected thumbnails, pagination and printing use the same order; each photo's copies remain adjacent.
- A changed selection starts at page one and clears old page-copy counts and manual placements. A fresh set of three photos no longer starts on the previous third page or inherits a zero-copy page.
- Explorer controls scale with DPI. The README includes a refreshed browser screenshot and navigation/order guidance.

Retains bundled ICC v2/v4 conversion, inline positioning, cached preview navigation, persistent driver settings and GitHub updates. Choose Help > Check for Updates to download/install and restart. Preferences and frames are retained; current photo selections close during updates.

Downloads include the single self-contained Windows 10/11 x64 portable EXE, per-user MSI, portable ZIP with documentation/notices/source, LibRaw source and SHA256 checksums. No separate .NET runtime is needed.

Verification covers browser reuse/expansion/scroll, folder history and refresh, click/bulk/pagination order, new-selection reset, normal/maximized/minimized restoration, monitor bounds, damaged preferences and unhandled UI events, alongside existing RAW/ICC/layout/driver/update fixtures. Physical L8050 output and complete reference-app equivalence remain unverified; see ROADMAP.md. New code is MIT; artwork and dependencies retain their separate notices. This is a regular release; binaries are unsigned.
