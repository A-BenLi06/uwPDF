# uwPDF

Extreme lite PDF viewer and annotator (UWP) focused on fast launch and pen annotations, aiming to reproduce the old Edge browser's PDF experience, styled after the macOS Finder Quick Look PDF preview.

## Features

- Opens local PDF files with the built-in `Windows.Data.Pdf` renderer.
- Quick Look-style UI: slim title bar with the centered file name, a light canvas with hairline page borders, and a floating dark translucent HUD toolbar at the bottom center.
- HUD buttons are custom line-icon ghost buttons (hover/pressed/checked overlays, no chrome), with page navigation, zoom out/fit/zoom in with a live percent label, and pen/highlighter/eraser tools.
- The color swatches, stroke size slider, undo, and clear appear in the HUD only while an ink tool is active, keeping the default toolbar minimal.
- Pages are laid out edge to edge at full viewport width with no outer padding, so the window is the page at 100% zoom; a background scheduler fills in remaining pages nearest the current viewport first and renders thumbnails in the gaps.
- Pages are laid out at fit width, so "fit" is zoom 100%; zooming re-renders visible pages at higher resolution automatically.
- Pages beyond a small window around the current page are evicted from memory, so long documents no longer accumulate bitmaps.
- Detects the current page with a binary search over the page stack instead of walking transforms on every scroll event.
- Annotation saves are dirty-tracked and debounced in the background; page turns never wait on disk I/O.
- Supports ink annotations with pen, drawing tablet, and mouse through `InkCanvas` and `InkPresenter` (`CoreInputDeviceTypes.Pen | Mouse`), saved per document and page in app local storage without modifying the original PDF. Touch still scrolls the document.
- Keyboard: `Space`/`Page Down`/`→`/`↓` next page, `Back`/`Page Up`/`←`/`↑` previous page, `Home`/`End` first/last page, `+`/`-` zoom, `0` fit, `Ctrl+O` open.
- Supports PDF file activation and the `litepdfviewerpreview://sample` protocol through the app manifest.

## Build

Open `LitePdfViewer.sln` in Visual Studio with the UWP workload installed, then build `Debug|x86` or `Debug|x64`.

The development package certificate is generated without a password.

Command-line build on this machine:

```powershell
.\.tools\nuget-3.5.0.exe restore .\LitePdfViewer.sln -ConfigFile .\NuGet.Config
& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' .\LitePdfViewer.sln /p:Configuration=Debug /p:Platform=x64
```

For local `litepdfviewerpreview://sample` launches, copy the test asset into the loose layout before registering it:

```powershell
Copy-Item .\LitePdfViewer\TestAssets\SkimSample.pdf .\LitePdfViewer\bin\x64\Debug\TestAssets\
Add-AppxPackage -Register .\LitePdfViewer\bin\x64\Debug\AppxManifest.xml
```

For the fastest local package, build Release:

```powershell
& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' .\LitePdfViewer.sln /p:Configuration=Release /p:Platform=x86
```

## Notes

This project targets the Windows 10 10586 UWP SDK available with Visual Studio 2015. That SDK includes `InkCanvas` and `InkPresenter`, but not the later built-in `InkToolbar` control. The app therefore uses official UWP controls and `InkPresenter` modes to provide the same basic pen, highlighter, eraser, color, and size workflow.

This repository targets the Windows 10 UWP SDK versions available on this machine:

- Target: `10.0.10586.0`
- Minimum: `10.0.10240.0`
