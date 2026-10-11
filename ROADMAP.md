# FrameSpool roadmap

Version 0.2.1 adds background previews, safer resizing and screen transitions, validated drag-and-drop, reusable popup menus and valid print-dialog page ranges. It includes bundled RAW development and .NET runtime, ICC-to-sRGB conversion, 143 layouts including roll paper, oval masks, crop marks, captions, legacy template import, portable frame packages, Quick Print workflows, driver controls and an MSI. Complete behavioral/output equivalence with the reference application remains unverified.

## Remaining work

1. **Physical printer validation.** Install the L8050 Windows driver and test dimensions/orientation, borders, media, quality, resolution, source, expansion, copies, saved private settings and color on actual paper. Only Windows virtual printing is verified here. Roll lengths also need a capable physical driver/printer.
2. **Proprietary processing parity.** Independently implemented enhancement curves, noise/red-eye/skin/portrait processing and missing-camera white-balance fallbacks differ from the reference. PRINT Image Matching is unavailable. Compare identical inputs/settings/driver against a functioning reference, including printer-side color management.
3. **Additional legacy formats.** PIF 2.x, multipage/non-360dpi PIF, arbitrary colored/custom EFF decoding and writing legacy PIF remain unsupported. EPPIF 3.1 import and new `.fspframe` export are available. Original caption token formatting/font mapping still need runtime comparisons.
4. **Broader deployment testing.** Test fresh Windows accounts, high-DPI displays, real camera files, large/long-path files, printer errors/disconnections and every dialog against a working reference. Expand output comparisons. Obtain a signing certificate for signed releases if needed.

## Verification

Synthetic photographs, Bayer DNG and ICC profiles test source preservation, RAW development/color/orientation, fixed-point arithmetic, layout/render geometry, crops, masks/captions/frames, workflow cancellation/preferences/shortcuts and actual Windows driver ticket/DEVMODE transport. The EXE is tested alone in an empty directory. MSI verification covers installation, upgrade and uninstall.

Tests validate this implementation; they do not establish a complete 1:1 comparison. Version 0.2.1 is a regular release; the hardware and reference-output checks above remain outstanding.

New source uses MIT. Asset ownership/redistribution permission and dependency terms are separate in ASSET_NOTICES.md and THIRD_PARTY_NOTICES.md.
