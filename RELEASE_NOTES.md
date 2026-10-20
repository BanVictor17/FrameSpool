FrameSpool 0.2.6 fixes live folder refresh and adds direct photo positioning in the main preview.

- Folders update automatically when directories are created, renamed or deleted, including Desktop while another folder is selected. New, renamed and removed photo files update the open folder while preserving selection, focus and scroll. Larger refreshes run in the background without opening a progress window.
- View > Refresh, the explorer refresh button and F5 refresh every loaded branch instead of only the selected folder. Expanded branches stay open. Deleting the open folder returns to an available parent; changes made during printing appear when returning to the browser.
- In Automatic flow, drag a photo directly inside its frame in the main page preview. Release to keep its position or press Escape during the drag to cancel. Fit/Trim and magnification are preserved, and each placed copy keeps independent offsets. Movement follows display scaling and rotated custom openings and uses the same page coordinates as printing/export.
- Manual flow retains dragging between photo slots.

Choose Help > Check for Updates to install this release from a portable or installed copy. Preferences and frames are retained; the current photo selection closes during updates.

Downloads include the self-contained Windows 10/11 x64 portable EXE, per-user MSI, portable ZIP with documentation/notices/source, LibRaw source and SHA256 checksums. No separate .NET runtime is needed.

Windows verification covers 272 assertions, including live folder creation/rename/deletion, background photo batches, deleted-folder recovery, changes during printing, crop dragging, Escape cancellation, independent copies, DPI changes and rotated openings. Separate checks cover portable updates and MSI installation, upgrade and uninstall. Physical printer output remains unverified. New code is MIT; artwork and dependencies retain their separate notices. Binaries are unsigned.
