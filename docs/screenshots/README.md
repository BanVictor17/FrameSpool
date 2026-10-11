# Screenshot credits and reproduction

These are captures of FrameSpool's actual Windows Forms, using demo data. They are generated in-process; no Epson application's windows are used.

The Blue Marble Earth montage is NASA image **PIA18033**, credited by NASA's image metadata to **NASA**. Source: https://images.nasa.gov/details/PIA18033. The medium JPEG used for the screenshots is https://images-assets.nasa.gov/image/PIA18033/PIA18033~medium.jpg. It appears solely as a demonstration photograph in informational application screenshots, under NASA's [images and media guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/). No NASA endorsement is implied. The source photograph is not bundled as application artwork.

The classic UI artwork visible in the captures has the separate provenance/permission described in ../../ASSET_NOTICES.md. Newly created application code is MIT licensed.

To reproduce after building:

```powershell
FrameSpool.exe --capture-screenshots <output-directory> <sample-image-path>
```

The capture uses demo copies, an eight-photo layout, the correction window, frame editor and actual capabilities of the selected/default Windows printer. Driver options may differ between machines. Private source paths in correction captures are replaced with the label `Demo Photos` before capture.
