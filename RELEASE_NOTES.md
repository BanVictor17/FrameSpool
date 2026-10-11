FrameSpool 0.2.0 expands the standalone photo-printing preview.

Downloads:
- Portable EXE: one self-contained Windows 10/11 x64 executable, including .NET, artwork, layouts, RAW decoder, licenses and decoder source.
- MSI: per-user install, Start menu shortcut, upgrade and uninstall support. No separate runtime prerequisite.
- Portable ZIP: the same app with documentation and notices. SHA256SUMS.txt verifies downloads.

Added camera RAW development, exposure/white balance, RGB histogram, embedded RGB ICC conversion to sRGB, 143 layouts including eight roll formats, original oval masks, crop marks, EXIF/rotated captions, EPPIF 3.1 template import and portable .fspframe packages.

Browsing includes shell folder icons, keyboard shortcuts, saved preferences, cancellable batches and exports. Quick Print A/B supports desktop shortcuts and dropped photos/folders. Zero page copies skips output. Media/quality/resolution/borderless controls use the installed Windows driver's capabilities; Properties retains additional device-specific settings.

The portable app and MSI are verified automatically with synthetic RAW/ICC/layout/workflow fixtures and Windows driver round trips. MSI verification exercises install, major upgrade and uninstall.

Physical L8050 output and complete reference equivalence remain untested. PRINT Image Matching, exact proprietary correction algorithms, old/multipage PIF and arbitrary colored EFF remain unavailable. See ROADMAP.md.

New code is MIT licensed. Third-party artwork, .NET and LibRaw have separate terms and notices, embedded and included with distributions. Releases are unsigned previews.
