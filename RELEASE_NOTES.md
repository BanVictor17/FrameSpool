FrameSpool 0.2.1 improves responsiveness and fixes reported UI and printing failures.

- Background thumbnail, page and correction rendering keeps resizing, zooming and sliders responsive. Rapid changes discard stale results and release images safely when a window closes.
- Buffered redraws and atomic screen changes remove remnants of the previous screen. Footer spacing and resize bounds are corrected.
- The app opens directly into photo selection, without the additional startup window.
- Valid initial page ranges fix the Microsoft Print to PDF "FromPage is out of range" error. Driver setup and dialog failures are handled inside the print workflow.
- Reusable popup menus fix the disposed ContextMenuStrip crash.
- Drag-and-drop validates source pages, slots and targets. Invalid/cancelled drops keep the photo in place; use right-click > Clear Settings to remove it. Frame dragging ends safely on capture loss or resize.

Downloads:
- Portable EXE: one self-contained Windows 10/11 x64 executable, including .NET, artwork, layouts, RAW decoder, licenses and decoder source.
- MSI: per-user install, Start menu shortcut, upgrade and uninstall support. No separate runtime prerequisite.
- Portable ZIP: the same app with documentation and notices. SHA256SUMS.txt verifies downloads.

Includes camera RAW development, exposure/white balance, RGB histogram, embedded RGB ICC conversion to sRGB, 143 layouts including eight roll formats, oval masks, crop marks, EXIF/rotated captions, EPPIF 3.1 template import and portable .fspframe packages.

Browsing includes shell folder icons, keyboard shortcuts, saved preferences, cancellable batches and exports. Quick Print A/B supports desktop shortcuts and dropped photos/folders. Zero page copies skips output. Media/quality/resolution/borderless controls use the installed Windows driver's capabilities; Properties retains additional device-specific settings.

The portable app and MSI are verified automatically with synthetic RAW/ICC/layout/workflow fixtures and Windows driver round trips. MSI verification exercises install, major upgrade and uninstall.

Physical L8050 output and complete reference equivalence remain untested. PRINT Image Matching, exact proprietary correction algorithms, old/multipage PIF and arbitrary colored EFF remain unavailable. See ROADMAP.md.

New code is MIT licensed. Third-party artwork, .NET and LibRaw have separate terms and notices, embedded and included with distributions. This is a regular release; binaries are unsigned.
