# uwPDF

Extreme lite PDF viewer and annotator (UWP) focused on fast launch and pen annotations, aiming to reproduce the old Edge browser's PDF experience, styled after the macOS Finder Quick Look PDF preview.

## Features

- Opens local PDF files with the built-in `Windows.Data.Pdf` renderer. A native DirectX 11 / `IPdfRendererNative` component renders into XAML surfaces; devices rejecting interop use a system-rendered bitmap fallback.
- DirectX device initialization runs through an asynchronous worker factory in both native bridges. Unsupported device/interop setup is not retried for every page; a page-specific draw failure keeps the device for other pages. Device recovery allows another setup attempt, and temporary allocation pressure does not permanently disable native rendering. Resetting the backend cancels stale native/bitmap results and releases partially allocated pixels.
- Selects PDF text with the default text tool: drag to select, double-click a word, then right-click for Copy, Highlight, Select all text on this page, or Deselect. `Ctrl+C`, `Ctrl+A` (current page), and `Esc` work too. Touch continues to scroll.
- Text highlights use the highlighter's selected color, stay aligned through zoom and resizing, and support Undo, Clear page annotations, and right-click deletion. Use Save / `Ctrl+S` to save ink and text highlights locally; reopening restores them. The PDF itself remains unchanged. There are no text editing, cut, or paste commands.
- PDF text is extracted offline and cached near the viewport. Image-only pages remain viewable and support ink, but have no selectable text; no OCR is performed.
- `Ctrl+F` opens page-by-page text search with next/previous navigation and wraparound. Query changes cancel waiting requests, unreadable pages are skipped with a partial-result notice, and search does not retain text for the entire document. Matching normalizes whitespace and supports overlapping occurrences.
- Export creates a separate PDF containing standard Highlight and Ink annotations with appearance streams. Windows Ink generates the exported filled stroke outlines, preserving curves, pressure and pen tip shapes; the standard InkList is retained for compatibility. Annotations are appended one at a time, avoiding a whole document's expanded outline JSON in the managed heap.
- Quick Look-style UI: slim title bar with the centered file name, a light canvas with hairline page borders, and a floating dark translucent HUD toolbar at the bottom center.
- HUD buttons are custom line-icon ghost buttons (hover/pressed/checked overlays, no chrome), with page navigation, zoom out/fit/zoom in with a live percent label, and pen/highlighter/eraser tools.
- The title bar's annotation menu provides text selection, touch writing, pen, highlighter, eraser, undo, and clear. Color and stroke size controls appear while an ink tool is active.
- The initial window follows the first page's cropped, rotated PDF point dimensions. It adds 28 DIP of horizontal padding, 16 DIP of vertical padding, a 52-DIP unified title bar, and a 110-DIP right thumbnail rail for multi-page documents. It scales down to leave 32 DIP around the monitor work area, with an initial scale capped at 100% and an 800 × 600 fallback.
- First-page rendering and the visible-page worker start without waiting for monitor work-area probing or automatic window sizing. Rapid file switches share one pending work-area probe; completed results are read again for the current monitor. A late sizing result respects a manual resize and the current file, and sizing failures do not reject a readable PDF.
- A manual window resize is retained when opening another file in the same session. Closing and reopening restores content-based sizing. Windows manages window position and can override sizing in snapped, maximized, or tablet layouts; the app requests a 500 × 320 minimum to keep commands usable.
- "100%" uses one PDF point per logical DIP. Fit adapts the entire current page to the viewport without enlarging it; manual zoom can go above 100%. Zooming re-renders visible pages at higher resolution automatically. A background scheduler renders nearby pages and thumbnails first.
- Vertical scrollbars keep a 3-DIP thumb in idle, scrolling, hover, and drag states, with a transparent 12-DIP hit area. Horizontal scrolling remains available for zoomed pages, with its scrollbar hidden. The native ScrollViewer template and input behavior are retained.
- Page controls and thumbnails are created only near their respective viewports, on absolute canvases that retain the full document scroll extent. Saved offscreen ink/highlight data is released and restored on demand; unsaved annotations survive virtualization. Annotation disk reads run independently of the page render queue.
- Visible pages keep rendering during scrolling and pinch zoom, using a smaller first bitmap when needed. All missing visible pages are filled before upgrading any existing visible surface. Once they are filled during scrolling, one adjacent missing page is prefetched in the movement direction. Further prefetching, text extraction, and resolution upgrades wait for a 120-ms pause (or the final event). Existing bitmaps stay visible during upgrades; a budgeted cache survives control recycling and immediately restores revisited pages. Obsolete results are discarded, and idle rendering waits for an event instead of polling.
- Horizontal panning, vertical scrolling, wheel input and active direct-manipulation gestures all defer background work. Queued refinements/detail work recheck input state before drawing and committing. Thumbnail selection uses the same scroll/zoom conditions as its execution guard, preventing a synchronous rejection loop during pinch or slider zoom.
- Raster replacements attach the new image without clearing the current source first. Replaced visible page/detail/thumbnail rasters are retained for two distinct XAML rendering callbacks, subject to a 32-MiB/four-entry transition queue. Transition bytes participate in page-cache trimming; hiding, suspending, resetting or unloading flushes the queue. Rendering callbacks are subscribed only while transitions are pending. This grace period is not a GPU fence or proof that scrolling white-page regressions are resolved on hardware.
- System surface-content loss and application resume reset the native backend, invalidate lost page/thumbnail surfaces and wake visible-first rendering, including when no pending draw exists to report a device error.
- Page bitmaps share an adaptive cache budget, normally up to 64 MiB, that accounts for retained CPU pixels and XAML surfaces, with at most four million pixels per bitmap. Visible pages receive most of the budget; prefetching and zoomed-out pages use lower resolutions. This is a raster-cache budget, not a cap on total process memory: PDF parsing, text data, annotations, native decoding, and transient surfaces also consume memory.
- Detects the current page with a binary search over the page stack instead of walking transforms on every scroll event.
- Mixed page dimensions are confirmed before first raster display, ink restoration and search-result positioning. Background geometry corrections preserve the current page fraction after layout rather than returning to its top; active gestures are not programmatically re-anchored. Fit uses the last explicitly fitted page as its reference, so scrolling onto another page size does not automatically change zoom; window refitting retains the reading position.
- Ink export reads each annotated page's actual rotated CropBox size from the export document, including pages never visited in the current session. It does not normalize saved strokes against an assumed first-page aspect ratio.
- Annotation saves write only dirty pages; page turns never wait on disk I/O.
- Supports ink annotations with pen, drawing tablet, and mouse through `InkCanvas` and `InkPresenter` (`CoreInputDeviceTypes.Pen | Mouse`), saved per document and page in app local storage without modifying the original PDF. Touch still scrolls the document.
- Keyboard: `Space`/`Page Down`/`→`/`↓` next page, `Back`/`Page Up`/`←`/`↑` previous page, `Home`/`End` first/last page, `+`/`-` zoom, `0` fit, `Ctrl+O` open.
- Supports PDF file activation and the `litepdfviewerpreview://sample` protocol through the app manifest.

## Build

Run `scripts/Prepare-NativeDependencies.ps1` to restore the pinned native auxiliary engine and generate font/source inputs. The C# UWP/XAML application retains SDK 10.0.14393.0 and its existing .NET Native build tools. Its default native component is C++/WinRT, requiring modern MSVC and SDK 10.0.26100.0; ARM32 uses the pinned local tools described below. Open `LitePdfViewer.sln` with the UWP workload installed. Debug and Release/.NET Native configurations for x86, x64 and ARM32 are retained. ARM64 uses the separate `LitePdfViewer.ARM64.sln` and build script below.

The development package certificate is generated without a password.

The DirectX device and PDF draw code now live in a shared standard C++ core.
The default C++/WinRT component uses that same core for x86/x64/ARM32/ARM64.
Ink outline export, display work-area queries, text extraction and annotation
writing also have C++/WinRT implementations. Modern packages contain one native
component and one auxiliary document engine, with no C++/CX bridge DLL.
The legacy C++/CX bridge remains available for x86/x64/ARM32 through
`/p:PdfRendererBridge=Legacy`, using the existing v140 tools. The default
application has separate `*-CppWinRT` output/intermediate folders and an
`AppPackages/CppWinRT` package directory.

`/p:PdfRendererBridge=CppWinRT` explicitly selects the default path.
This path requires modern MSVC tools and
Windows SDK 10.0.26100.0; `scripts/Build-CppWinRT.ps1` generates the metadata and
projection from `PdfNative/WinRT/PdfNative.Rendering.idl`. The native component
uses the static OneCore CRT, including its pinned MuPDF parser/writer. The build
validates source, engine, tool and output hashes before reusing an existing DLL.
No new WinUI, browser or runtime NuGet dependency is added.
ARM32 can use either bridge. Its C++/WinRT build uses the local toolchain below.
The renderer cross-compiles for ARM64 in
Debug and Release and is used by the separate ARM64 application. Neither bridge migration nor packaging proves that
the application runs on every older Windows version.

For ARM32, run `Prepare-CppWinRTArm32.ps1` first. It prepares pinned MSVC 14.44
compiler/resources, matching headers and static OneCore CRT from the installed
Visual Studio catalog, with payload hashes checked. SDK 26100 lacks ARM32
libraries and its bundled C++/WinRT library no longer supports ARM32, so this
path links 14393 ARM32 UCRT/UM libraries and uses 14393 Win32/PDF declarations.
Current UCRT/WRL compilation helpers and MIDL/metadata come from SDK 26100;
the unmodified Microsoft.Windows.CppWinRT 2.0.230706.1 generator emits both base
and Windows projections. The NuGet archive is pinned by SHA-256, its Microsoft
author/NuGet repository signatures were verified, and its MIT notice is packaged.
This preparation changes no system registration. Build records distinguish
native SDK, helper/projection SDK and generator version. App target/minimum
remain 14393; this does not establish ARM32/Windows 1607 device runtime.

```powershell
.\scripts\Prepare-CppWinRTArm32.ps1
.\scripts\Build-CppWinRT.ps1 -Platform ARM -Configuration Release
.\scripts\Test-CppWinRTBinary.ps1 -Platform ARM -Configuration Release
.\scripts\Test-CppWinRT.ps1 -Platform ARM -Configuration Release -SkipBuild -CompileOnly
& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' .\LitePdfViewer.sln /p:Configuration=Release /p:Platform=ARM /p:PdfRendererBridge=CppWinRT
```

ARM32 smoke executables are cross-compiled without executing on an x64 host.
Actual activation, drawing and application input still require an ARM32 device.

The build script searches all installed MSVC instances for the requested
architecture. If ARM64 tools cannot be installed system-wide, run
`scripts/Prepare-CppWinRTArm64.ps1` to prepare a local overlay under
`artifacts/arm64-tools`. It downloads ARM64 compiler/resources and static OneCore
libraries from the installed Visual Studio catalog, verifies their SHA-256,
and copies headers from the matching installed compiler. It does not register
or modify Visual Studio. Install SDK 10.0.26100.0 before building the component.

```powershell
.\scripts\Prepare-CppWinRTArm64.ps1
.\scripts\Build-CppWinRT.ps1 -Platform ARM64 -Configuration Release
.\scripts\Test-CppWinRTBinary.ps1 -Platform ARM64 -Configuration Release
.\scripts\Test-CppWinRT.ps1 -Platform ARM64 -Configuration Release -SkipBuild -CompileOnly
```

The binary check covers PE architecture, AppContainer flags, WinRT exports,
static CRT dependencies and build hashes. `-CompileOnly` generates an ARM64
smoke executable without running it; activation/rendering must be checked on
an ARM64 device. These component checks do not establish ARM64 app runtime.

The complete ARM64 app uses UWP 6.2.14 and .NET Native 2.2, with SDK 26100 and
minimum Windows 10 1809 (17763). Its project, manifest, intermediate outputs and
packages are separate from the existing 14393 builds. Both app projects import
`LitePdfViewer.Shared.projitems` so the UI, PDF logic and packaged assets share
one source list. The ARM64 solution now builds the C# application and the complete
C++/WinRT component without the legacy C++/CX projects.

```powershell
.\scripts\Prepare-CppWinRTArm64.ps1
.\scripts\Prepare-UwpArm64Targets.ps1
.\scripts\Build-UwpArm64.ps1 -Configuration Release
.\scripts\Build-UwpArm64.ps1 -Configuration Debug
```

The targets preparation script copies installed VS 2026 native targets locally
and adds hash-verified official ARM64/UWP rules. `Build-UwpArm64.ps1` invokes the
separate ARM64 solution with those targets and the local compiler. It requires
VS 2026 UWP XAML tools and SDK 26100. This is a local build dependency setup,
not a Visual Studio registration change. `Test-UwpArm64Package.ps1` checks the
generated package architecture, all six activation registrations, the current
component DLL, absence of the legacy engine, OS baseline and .NET Native dependencies. Installing and
running the app on an ARM64 device remains a separate acceptance step.

The component can be tested without opening the application:

```powershell
.\scripts\Test-CppWinRT.ps1 -Platform x64
.\scripts\Test-CppWinRT.ps1 -Platform x86
.\scripts\Test-CppWinRTInk.ps1 -Platform x64 -Configuration Release -SkipBuild -RepeatCount 100
.\scripts\Test-PreviewDisplay.ps1
.\scripts\Test-NativeInk.ps1 -RepeatCount 100
```

These checks load the real DLL activation factory, exercise asynchronous device creation,
check ABI argument errors, failed-coroutine lifetime and unloading, and inspect
pixels drawn by the shared production core. They exclude XAML presentation,
touch/pen input and frame latency. `scripts/Test-CppWinRTPackage.ps1` also checks
all three packaged activation registrations and the component DLL hash.

Ink export clones each stroke before leaving the caller thread. Its background
outline capture shares the expensive DirectX device/context, but each stroke
uses a temporary InkD2DRenderer that is released on the drawing thread while
the snapshot is still alive. This bounds native stroke-cache lifetime and avoids
the heap corruption reproduced during concurrent export/close testing. The ink
smoke checks pressure, constant-width and transformed rectangular pen tips,
snapshot isolation, concurrent close, async lifetime and unload. Its output is
compared with the saved legacy outlines; `tests/VerifyNativeInk.py --outlines`
can then verify those outlines in standard exported PDF Ink appearances.
The legacy bridge queues an explicit shared-core lease so pending work does
not access native members after C++/CX Close. Its headless ink check also
exercises concurrent close, and releases all WinRT objects before apartment
uninitialization.

Command-line build on this machine:

```powershell
.\.tools\nuget-3.5.0.exe restore .\LitePdfViewer.sln -ConfigFile .\NuGet.Config
.\scripts\Prepare-NativeDependencies.ps1
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

The system renderer does not expose text. The auxiliary MuPDF document layer reads a cloned, seekable WinRT stream with a 64-KiB buffer on a worker. It returns native character quadrilaterals, including rotation and CropBox handling, rather than estimating character widths with a browser font. Image pixels are not decoded for text extraction; image-wrapped ActualText retains placement metadata, including on JPEG2000 pages. MuPDF is also used to write standard annotations into a separate PDF; its page renderer is not used by the viewer. PDF JavaScript and non-PDF document handlers are disabled. See [THIRD-PARTY.md](THIRD-PARTY.md) for pinned versions, build options and licenses. The old PDF.js text bridge is no longer packaged.

Window sizing queries the native display-region API when the current view has one visible region (Windows 10 1903 or later). Older systems and ambiguous display-region results retain the short-lived WebView work-area probe to avoid guessing the monitor or taskbar dimensions. This remaining fallback is separate from text extraction.

Native text/export checks can run without opening the application:

```powershell
.\scripts\Test-NativeText.ps1
python .\tests\VerifyNativeDocument.py
.\scripts\Test-TextSearch.ps1
.\scripts\Test-RenderScheduler.ps1
.\scripts\Test-RenderLoop.ps1
.\scripts\Test-ScrollActivity.ps1
.\scripts\Test-RasterBackend.ps1
.\scripts\Test-RasterRetirement.ps1
.\scripts\Test-MemoryRecovery.ps1
.\scripts\Test-LegacyRenderer.ps1
.\scripts\Test-PageGeometry.ps1
.\scripts\Test-PreviewStartup.ps1
.\scripts\Test-NativeInk.ps1
python .\tests\VerifyNativeInk.py
```

These checks exercise the production stream adapter, queued-read lifetime, known character geometry, CJK, rotated/cropped pages, search matching, system-generated ink outlines, batch annotation export and preservation of page content/boxes. Ink tests also compare exported PDF pixels with the normalized system outline, including the rotated CropBox page. They do not establish scrolling, touch or pen latency. Runtime acceptance still requires current-build interaction, memory and device tests; see [VERIFICATION.md](VERIFICATION.md).

The render-loop test compiles the current production `RunRenderLoopAsync` method
with platform/work doubles and records its source hash. It verifies yielding
during zoom/scroll, continued missing-page rendering, resumed background work
and hidden-window waiting. Backend tests exercise the production C# policy with
platform doubles; retirement tests exercise its actual frame/byte/entry policy.
They do not run XAML presentation. The legacy renderer smoke test separately
exercises its real asynchronous native factory.

The raster cache responds to `MemoryManager` usage/limit events. Its normal
maximum is 64 MiB, capped at one eighth of the reported application limit;
Medium pressure caps it at 32 MiB, High at 8 MiB, and OverLimit at 4 MiB.
A 1-MiB foreground floor keeps a small readable surface when the system limit
is already below usage. These are raster budgets, not total-process limits.
High pressure suspends offscreen prefetch, detail patches and thumbnails,
releases offscreen controls/clean annotations, and preserves visible base
images until a smaller replacement arrives. Dirty strokes/highlights survive
eviction. Low pressure with sufficient headroom resumes background work;
failed page/thumbnail/text work gets one new attempt on that transition.
There is no per-frame memory query or forced collection.

`Test-MemoryRecovery.ps1` compiles the production event controller and policy,
and extracts actual cache trimming, clean-annotation eviction, scheduler and
surface-recovery methods. With platform/work doubles, it checks coalesced
notifications including the early `NewLimit`, unloaded/reloaded subscriptions,
foreground/dirty preservation, pressure hysteresis, hidden-window cleanup,
prefetch suspension/recovery and uncached first-draw failure recovery. It does
not simulate the actual Windows memory manager or XAML/GPU presentation.

`scripts/Test-VS2015Sdk.ps1` checks 14393 discovery using MSBuild 14 in a fresh
32-bit .NET Framework process, its API contracts and x86/x64/ARM libraries.
It writes `artifacts/diagnostics/sdk2015-check.json` without changing SDK files
or project targets. A passing check does not test the running IDE's project cache.

`scripts/Test-VS2015Diagnostics.ps1` checks the collector executable, service and
COM activation and writes `artifacts/diagnostics/collector-check.json`. Successful
activation does not verify a Visual Studio profiling session or graph collection.
`scripts/Repair-VS2015Diagnostics.ps1` repairs collector registration using the
VS 2015 installer, with registration backups, when infrastructure checks fail.

`Test-PreviewStartup.ps1` compiles the actual document loading, sizing and
work-area probe methods with platform/work doubles and a single-thread
synchronization context. It checks first-page/worker independence from sizing,
stale file and resize continuations, manual-size preservation, one pending probe,
fresh monitor queries, cleanup failures and nonfatal sizing errors. Four negative
controls deliberately restore each regression and must fail. These tests do not
measure app startup, browser memory, actual monitor selection or frame latency.

`scripts/Test-NativeCleanup.ps1` verifies that export and abandoned text-open
cleanup allow a UI heartbeat, finish before input disposal, and retain
success/error/cancellation behavior. It exercises production C# methods with
native/platform doubles; it does not measure real PDF teardown or UI latency.

`scripts/Test-TextWorkers.ps1` checks background glyph expansion and page
matching with synchronous native doubles, including cached search, cancellation,
UTF-16/quad mapping, stale query/file rejection and continuation from the selected
match when the reading-page indicator differs. Four negative controls
restore the scheduling/publication regressions and must fail. This verifies
production control flow rather than actual input or frame latency.

For a reproducible image-heavy performance fixture and an engine-only baseline:

```powershell
python .\tests\CreatePerformanceFixture.py
.\scripts\Measure-NativeRaster.ps1
```

The fixture has 600 pages and 40 unique JPEG images. The benchmark samples forward, backward and repeated jump sequences at 768/1600-pixel widths. It now calls the shared production render core rather than creating a separate renderer implementation. It records PDF load/GetPage times, native DirectX render submission and GPU completion times, process memory, and the input SHA-256 in `artifacts/native-tests/raster-benchmark-*.json`. It reuses one output surface, so its memory figures exclude the app's page cache, XAML and auxiliary engine. GPU completion uses an event; these are engine measurements, not presentation/frame or pen latency.

The supported OS baseline is Windows 10 1607 (14393), as requested. System UWP
XAML, InkCanvas and InkPresenter remain the input path; the custom lightweight
toolbar retains the pen, highlighter, eraser, color and size workflow.

Application, legacy native projects and package manifest:

- Target: `10.0.14393.0`
- Minimum: `10.0.14393.0`

These settings apply to x86, x64 and ARM32. The separate ARM64 project targets
`10.0.26100.0` with minimum `10.0.17763.0`; it has its own manifest and packages.

All eight default C++/WinRT packages passed current-source checks: Debug/Release
for x86/x64/ARM32/ARM64, all six activation classes, exact native DLL/engine
hashes, no legacy bridge and exact licenses. The first six retain target/minimum
14393; the separate ARM64 packages retain 17763/26100. Evidence is recorded in
`artifacts/text-winrt/package-checks.json`. The earlier fourteen-package matrix
at `artifacts/arm64-tools/package-checks.json` predates the text/writer migration
and default-path change and is historical evidence. See
[VERIFICATION.md](VERIFICATION.md) for the current verification scope.
Build/package checks do not establish runtime compatibility on a 14393 device.
