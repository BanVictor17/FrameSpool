# Third-party asset notices

FrameSpool's C# code and build scripts were written independently. The MIT license in `LICENSE` applies to that new code and project documentation; it does not relicense the contents of `assets/`.

## Built-in data

The interface artwork in `assets/EPQImage/` and `assets/Image/`, the application icon, and the 143 template definitions (135 FD2 and eight USD roll layouts) in `assets/layouts.json` were recovered from Epson Easy Photo Print. TGA images were converted to PNG for decoding. The four white EFF oval masks in `assets/LegacyDecorations` were decoded into PNG alpha overlays; crop marks and caption geometry remain derived template data. The artwork and template data retain their original copyright ownership; FrameSpool does not claim to have created them.

The repository maintainer confirmed permission to redistribute these assets on 2026-10-11. The original permission document and its specific terms were not supplied to the development agent. Include any conditions or notices required by that permission with distributed copies. This confirmation is not a new open-source license for the assets and does not establish unrestricted downstream relicensing rights.

No original application executable, DLL, SDK or runtime is distributed or loaded by FrameSpool. The original package is not required to build or run the app. The executable contains the same data files supplied in this repository.

The isolated fixed-point color conversions were independently implemented from observed arithmetic. They do not include the original proprietary enhancement engine.
