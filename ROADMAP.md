# FrameSpool roadmap

FrameSpool is usable for photo selection, layout, cropping, corrections, frames and Windows-driver printing. It is an early implementation; complete behavioral and output equivalence with the reference application has not been established.

## Required for complete functionality

1. **Printer validation.** Install the L8050 Windows driver and compare borderless and bordered output on real paper. Check each paper size, orientation, printable area, media type, quality, source, expansion, copies and saved driver settings. Current verification uses Microsoft Print to PDF; it does not establish physical printer compatibility.
2. **Correct driver controls.** Media, quality, borderless and expansion currently belong to the driver's Properties dialog. The generic controls preserve their displayed values but cannot write proprietary device settings. Connect those controls to verified driver capabilities or route them explicitly through Properties before treating them as fully functional.
3. **Output fidelity.** Implement and compare enhancement curves, ICC/color management, PRINT Image Matching metadata, noise reduction, red-eye detection, skin processing and portrait reshaping. Current correction algorithms are approximations. Compare reference output using the same image, layout, settings and driver; similar controls do not prove identical results.
4. **Remaining layout formats.** Decode the eight legacy USD layouts, EFF decorations and PIF frame import/export. Render legacy oval overlays, EXIF-info captions, rotated/vertical captions and original font metrics. All 135 FD2 records load, but some decorations and captions differ.
5. **RAW development.** Add a distributable RAW decoder and verify camera orientation, capture metadata, color and supported formats. JPEG, TIFF, PNG, BMP and GIF already work.
6. **Interface behavior.** Compare every screen and dialog with a working reference: geometry, keyboard navigation, selection, drag/drop, crop offsets and rotation direction. Complete splash/alerts/preferences behavior, shell folder browsing and Quick Print desktop shortcuts.
7. **Release verification.** Test a clean Windows account, high-DPI displays, long/non-ASCII paths, large images, disconnected printers, cancelled dialogs and saved/custom frames. Add representative output comparisons and driver tests for resolved differences. A signed installer and a newer bundled .NET runtime can follow if wider deployment requires them.

## Distribution requirements

- The new code uses the MIT license. Third-party data is described separately in `ASSET_NOTICES.md`; do not apply MIT to those assets.
- Keep the permission terms and any required third-party notices with releases. Preserve the artwork's actual provenance even though the application has independent branding.
- Publish releases as previews until outstanding behavior and printer checks are complete. A single executable does not make the algorithms or driver output equivalent to the reference application.

## Verified baseline

The initial suite passed 79 assertions on the development machine, including layout geometry, pagination, source-file preservation, independent crops for repeated photos, correction behavior, Windows Forms rendering and driver-setting round trips. Windows printing produced a 5 x 3.5 inch PDF with approximately 8pt captions. These are tests of FrameSpool's implementation, not a complete reference comparison.
