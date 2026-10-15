# uwPDF acceptance status

## Local x64 test package 1.0.10.0 — 2026-10-14

- Both source manifests now use 1.0.10.0 so the local Release test package can
  upgrade the prior 1.0.9.0 and the next F5 build uses the same version.
- x64 Release/.NET Native with `UseStoreIdentity=false` completed and signed
  with the existing CN=Ben development certificate. Native package and 14393
  baseline checks pass. The AppX CMS signature verifies cryptographically;
  local certificate trust is handled by the included developer installer.
- Archive: `artifacts/releases/uwPDF-1.0.10.0-x64-test-20261014.zip`, approximately
  20.82 MiB, with AppX, public certificate, PowerShell installer, dependencies
  and TESTING.txt. Required ZIP entries were verified. The adjacent JSON records
  hashes and that the current uncommitted working tree is included. No install
  or Store upload was performed; the previous 1.0.9.0 archive remains preserved.

## Floating annotation layer and scrollbar proximity — 2026-10-14

- The annotation card is reparented from the command row into the document
  overlay. The header remains 44 DIP at every width; no second row is measured.
  A native rounded rectangle backs the tool card. Search and annotations share
  the overlay, with annotations placed below search when both are visible.
- Document and thumbnail ScrollViewer/ScrollBar templates and input/range logic
  remain native. A root-level mouse/pen-hover proximity observer selects native
  expanded/compact display states at 24/36 DIP, with hysteresis and native thumb
  drag retention. It never captures a pointer, handles an input event or maps a
  scrollbar value to a document offset. Touch continues to pan the document.
- Native automatic mouse/hide state changes are reconciled through one coalesced
  dispatcher action after the native transition commits. Calling GoToState
  recursively from CurrentStateChanged was observed to lose to the native commit;
  the final implementation avoids that re-entry and does not poll.
- A temporary own-app XAML harness used a real PDF at 500x320, 799x400,
  800x600 and 1000x650 DIP. Opening pen tools, the settings flyout and search,
  and closing the tools preserved exact document actual/viewport width/height,
  zoom, horizontal/vertical offsets and origin. Header/card/HUD bounds passed.
- Both native bars passed compact, near expanded, hysteresis and far compact
  checks; native body-mouse and hide transitions did not defeat the policy.
  Compact indicators remained after 3.6 seconds idle, with unchanged PDF layout.
  Own-app bitmaps show the full rail versus thin indicator. No computer-use
  tools or OS input injection was used; physical mouse drag/proximity and touch
  still require device acceptance. Artifacts: `artifacts/overlay-proximity-qa/`.
- Temporary harness code is removed before the final Debug build/layout sync
  and Store Release build. Localization, text-worker, zoom and startup checks pass.
  This revision supersedes the earlier wrapped annotation row and fully automatic
  scrollbar visibility descriptions below.

## Supplied glossy PDF logo — 2026-10-14

- The supplied PNG is preserved byte-for-byte in `Branding/LogoSource.png`;
  SHA-256 matches the original Downloads file. Its background is transparent.
  Assets use an alpha-bounded crop with a 13-pixel source bleed margin, centered
  and resized without distorting the drawing. Native source/small/tile PNGs
  were visually inspected.
- Generated 67 transparent assets: app/tile/Store/splash at 100/125/150/200/400%
  and 14 exact app-icon sizes from 16 to 256 pixels, each with default, unplated
  and light-unplated forms. Both architectures use the shared qualified assets;
  manifest virtual base references are retained. The empty-page logo is updated.
- The large tile PNGs initially exceeded SDK-14393's 200-KiB limit. Lossless
  refiltering/compression reduces them below the limit. Decoded RGBA pixel hashes
  match before/after compression; there is no color quantization. Splash uses
  the same lossless optimization. The generator retains this compression step.
- x64 Debug package build passes; new assets are synchronized to the original
  F5 layout and the obsolete five unqualified preview assets are removed there.
  Store Release is rebuilt with the new artwork. No Explorer cache reset,
  computer-use tools or new app registration is used.

## Store identity and four language resources — 2026-10-14

- Both canonical manifests use `BenLi06.uwPDF-LitePDFViewer`, publisher
  `CN=FC1FF585-22D6-4DFB-97E0-9D22B6842CC6`, and `BenLi06` display name.
  `StoreIdentity.json` records the supplied PFN, SID and Store ID without the
  pasted trailing whitespace/entities. Windows SID derivation matches the PFN.
- Release uses Store identity and no local signature. Debug generates its
  manifest under `obj/`, preserving the original development identity and signing
  certificate for F5. This avoids a certificate-subject mismatch and a new local
  app registration/data container. `/p:UseStoreIdentity` explicitly selects either.
- Simplified Chinese (`zh-Hans`), Traditional Chinese (`zh-Hant`), English
  (`en-US`, fallback), and Japanese (`ja-JP`) each have 165 matching RESW entries.
  XAML uses native x:Uid, including tooltip/accessibility attached properties;
  code uses ResourceLoader. Search status, menus, save/unsaved/error feedback,
  accessibility names and package display/description/file-type strings are
  localized. There is no production language override or manual regional mapping.
- A temporary own-app harness launched a fresh process per locale with a
  nonpersistent MRT language qualifier. Native XAML, tooltip, accessibility,
  code and pen flyout checks passed for zh-CN, zh-TW, zh-HK, en-US, en-GB and
  ja-JP; fr-FR fell back to English. Search read the real 17-match PDF and showed
  the localized fourth-result count. At 500x320 DIP, search/control bounds and
  the pen/HUD layout remained usable. Own-app bitmaps were inspected. Artifacts:
  `artifacts/localization-qa/`. No computer-use tools or OS input injection used.
- x64 Release completed .NET Native compilation and produced an unsigned Store
  AppX under `artifacts/store-packages/x64/`. Its manifest declares all four
  languages and the Store identity; the compiled PRI map uses the Store name and
  contains all language candidates. Baseline 14393 and native component package
  checks pass. No Store upload, Store installation or ARM64 rebuild was performed.
- Resource coverage/placeholders, search workers, search animation, startup,
  zoom and keyboard regressions pass. The temporary harness is removed before
  the final Debug build and original F5 layout synchronization.

## Native scrollbars, HUD anchoring and search refinement — 2026-10-13

- This working-tree revision supersedes the custom touch scrollbar described
  below and shipped in the earlier 1.0.9.0 / 6306c23 test archive. The user chose
  native scrollbars and direct finger panning on the document. Both scrollers
  retain the system templates and indicator/input behavior; custom pointer
  capture, drag math and templates are removed. No Slider replacement is used.
- References inspected: [Toolkit UWP sample styles](https://github.com/CommunityToolkit/WindowsCommunityToolkit/blob/main/Microsoft.Toolkit.Uwp.SampleApp/Styles/Generic.xaml),
  [Toolkit smooth scrolling](https://github.com/CommunityToolkit/WindowsCommunityToolkit/blob/rel/7.1.0/Microsoft.Toolkit.Uwp.UI/Extensions/ListViewBase/ListViewExtensions.SmoothScrollIntoView.cs),
  and [Microsoft's UWP scrollbar controller](https://github.com/microsoft/microsoft-ui-xaml/blob/v2.8.6/dev/ScrollView/ScrollBarController.cpp).
  These inform use of native controls and animated ChangeView completion;
  they do not add a WinUI dependency or change the SDK-14393 baseline.
- MainChrome has an Auto command row and a remaining-space document row.
  The HUD and its reveal surface span the window root, independently of the
  annotation row. Search uses a 360-DIP bounded panel, 12-DIP padding,
  aligned 32-DIP native input/actions, 8-DIP group/status gaps and a wrapping
  status row. The count is also announced as a polite accessibility live region.
- Search navigation submits a single animated native ChangeView to the match,
  preserving zoom. The old preliminary page-top jump/refit is removed. Navigation
  waits for the final ViewChanged; equal/clamped targets, cancellation, rejected
  requests and a missing-event timeout do not retain the temporary handler.
- A temporary own-app XAML harness used a real 17-match PDF. At 500x320,
  approximately 799x400, 800x600 and 1000x650 DIP, toolbar wrapping changed
  between 88 and 44 DIP while HUD bounds remained inside the root, with a
  12-DIP bottom margin and unchanged Y across annotation opening/closing.
  Test root sizes were constrained to the actual window; window bounds also
  matched these requests within pixel rounding. Search action/input alignment,
  padding and panel bounds passed. Both scrollbars measured the native 12 DIP
  and had no authored Style. Own-app control bitmaps were inspected.
- Animated navigation to 第4/17个 produced 24 intermediate ViewChanged events
  and a final event. Next/previous, both wraps, rotated glyph mask alignment,
  unchanged 80% zoom, no annotation dirtying and search cleanup passed.
  Reports/bitmaps: `artifacts/refined-ui-qa/`. No computer-use tools, OS input
  injection or physical touch acceptance were performed.
- Search-scroll, text-worker, zoom, startup and keyboard regressions pass.
  Temporary harness code is removed before the production Debug build and sync
  to the original F5 layout. The earlier Release archive is a separate snapshot.

## Release test package 1.0.9.0 — 2026-10-13

- The x64 Release app completed .NET Native compilation and signed AppX
  packaging. Output: `LitePdfViewer/AppPackages/CppWinRT/LitePdfViewer_1.0.9.0_x64_Test/`.
  Both source manifests use version 1.0.9.0; ARM64 was not rebuilt in this run.
- Package checks pass for the Windows 10 14393 baseline, all six modern WinRT
  registrations, the exact freshly built component DLL, static CRT, and absence
  of the legacy bridge. The developer installer includes certificate/dependencies.
- Release native core and component tests pass, including injected stream failures,
  cleanup, factories and concurrent close. Native document verification also passes
  repeated saves/erase without accumulating owned annotations, preservation of
  original/foreign annotations, text/quad geometry, CJK, rotation/crop and JPEG/JPX.
- The current managed cleanup, text search/worker, touch-drag mapping and zoom
  regressions pass. This package is supplied for user testing; no Release install
  or physical touchscreen acceptance was performed here.

## Touch scrollbar and search focus — 2026-10-13

- Scrollbar input surfaces are now a fixed 32 DIP with a minimum 44-DIP thumb
  height; visuals remain 3 DIP normally and 8 DIP on hover/touch dragging. The
  touch path cancels ancestor direct manipulation, captures one pointer, handles
  routed events even when a native part has handled them, and applies proportional
  range values to the correct ScrollViewer. Mouse events retain native routing.
- Search builds a cancellable document-wide range index in the background,
  presents the first useful result while counting, and retains geometry only for
  the active match. Next/previous use the match index independently of the reading
  page indicator. Counts use `第4/17个`; unreadable pages are reported separately.
- A hit-test-transparent reading overlay dims context and cuts out the active
  glyph quads, with an amber fill/outline. It follows scroll/zoom/layout transforms,
  supports rotated text, excludes the native scrollbar, and clears on query
  change, close, empty query or file switch. It does not alter PDF annotations.
- A temporary in-process XAML harness read a real two-page, 17-match PDF. It
  verified 第4/17个, both wrap directions, the rotated last match, alignment at
  64/80% zoom and after resizing, input transparency, cleanup, and unchanged
  annotation dirty state. Native control bitmaps were inspected; the 800-DIP
  offscreen layout bitmaps are clipped by the actual window on their right edge.
  Artifacts: `artifacts/search-touch-qa/`.
- Both native scrollbars measured 32 DIP, resolved to their own ScrollViewer,
  had manipulation-disabled template roots and installed handled-event hooks.
  Applying the production drag mapping moved each owner to the expected offset;
  TouchIdle/TouchDragging/TouchIdle restored 3/8/3 visuals. This verifies controls,
  range geometry and value routing; physical finger capture/drag has not been
  exercised. No computer-use tools or pointer injection were used.
- Text matching/worker, scrollbar drag, zoom, startup and keyboard regression
  scripts pass, and the x64 Debug SDK-14393 app build passes. Temporary harness
  code is removed before the final production rebuild and original layout sync.
  The earlier 12-DIP hit-area verification below describes the previous version.

## Zoom animation and scrollbar hover — 2026-10-11

- Removed the `viewport / zoom` content-width feedback and extra immediate
  `ChangeView` from centering. The frame retains the natural PDF width; native
  `HorizontalContentAlignment="Center"` handles pages smaller than the viewport.
  Explicit zoom/fit requests notify the render scheduler before animation, and
  automatic refits wait until zoom settles. Native programmatic zoom also raises
  DirectManipulationStarted; it no longer incorrectly cancels fit-page mode.
- A temporary in-process XAML harness loaded the packaged sample and invoked
  production zoom handlers at 25%, 51.2%, 64%, 80%, 100% and 125%, with 800- and
  500-DIP layouts. Native ViewChanged traces showed no zoom reversals, final
  targets within 0.5%, and a constant 614-DIP content width; final page centering
  was within 2 DIP. Animated fit, rapid zoom retargeting, and immediate slider
  requests also settled without obsolete refits. This checks native state/geometry,
  not presented frame times.
- The native scrollbar Normal/PointerOver/Normal states were exercised in both
  reading and thumbnail scrollers: thumb visuals changed 3 -> 8 -> 3 DIP while
  the input surface stayed 12 DIP and the viewport width remained unchanged.
  This exercises visual states without simulated mouse/pointer input.
- `scripts/Test-Zoom.ps1` covers the production methods with intermediate-frame
  doubles, including one ChangeView request, stable extent, the reading anchor,
  native Auto-width initialization, zoom bounds, and slider animation mode.
  Programmatic animation preserves fit mode; a pointer zoom leaves fit mode.
- No computer-use tooling or additional package registrations were used.
  Native traces are retained in `artifacts/zoom-hover-qa/result.txt`; temporary
  harness code is removed before the final Debug rebuild and layout sync.

## Compact native toolbar — 2026-10-11

- Segoe MDL2 Assets replaces the earlier Segoe UI Symbol map at the user's request. All 25 FontIcons use explicit font families and canonical codepoints checked against the installed font. Eraser now uses Microsoft's `E75C` EraseTool glyph; the former `E294` scanner glyph was a semantic error in the earlier version. Open uses `ED25` OpenFolderHorizontal; thumbnails use `E90D` DockRight.
- Native Button and ToggleButton templates replace oversized AppBar controls. Buttons measure 36 × 36 DIP in a 44-DIP toolbar. At widths of at least 800 DIP, the open annotation group stays centered in the same row; at smaller widths, it wraps together into a second 44-DIP row. Filename text remains left-aligned. Touch writing and clearing the current page are in a native More flyout.
- A temporary in-process XAML harness exercised the production layout and tool handlers, without computer-use tooling or simulated pointer/keyboard input. Native measurements passed at 1200, 800, 799 and 500 DIP; filenames retained at least 112 DIP in the tested inline layouts. Closing annotations returned to a 44-DIP reading row. `RenderTargetBitmap` outputs of the real toolbar were inspected; artifacts are `artifacts/compact-toolbar-qa/`.
- Pen/highlighter repeat selection opened their native settings flyouts; the 216-DIP palette grid fit within the 220-DIP panel. Tool selection remained mutually exclusive, and clicking the active eraser returned to text selection. Duplicate size preset rows were removed; sliders and live previews remain.
- Temporary harness code was removed from App and MainPage before the final build. No extra verification packages were registered. x64 Debug/AppX compilation, production preview-startup checks and existing keyboard checks passed. These checks do not establish physical stylus/touch behavior or validation on Windows 10 1607 hardware.

## UI refinement — 2026-10-10

- x64 Debug C++/WinRT build and AppX packaging passed on the existing SDK 14393 application target. All 26 authored FontIcons explicitly specify Segoe UI Symbol; each code point was checked against the installed font cmap and the relevant glyphs were visually inspected. MainPage has no implicit SymbolIcon/AppBar Icon shorthand remaining.
- Windows UI checks used a separate `Ben.UwPdfUiVerification` package and the repository's SkimSample PDF. The user's running preview and its unsaved annotations were left open. The first staging attempt used old AppX files; subsequent checks used the freshly extracted package, and the registered location was checked before updating it.
- Observed: light native title bar; title-bar dragging changed the window origin; commands and annotation toolbar fit a 502-DIP-wide outer window; only the current ink tool remains selected; pen settings open and light-dismiss without collapsing the toolbar; the 6-unit preset updates the preview and mouse ink is collected; Ctrl+F places search at the top right and highlights a matching word.
- Observed on the final HUD implementation: idle fade; clicking the hidden zoom control position in pen mode reveals the HUD and opens zoom input without adding a stroke or marking the file dirty; zoom input keeps the HUD visible beyond its idle interval. A transparent input region protects the floating toolbar area while hidden.
- Production-method preview startup and keyboard regression scripts passed. Keyboard coverage now includes already-handled navigation/shortcut keys, so native control input does not also invoke document commands.
- Physical stylus pressure, touch/palm behavior, other DPI scales, minimum-height flyout scrolling, and runtime checks on ARM32/ARM64 were not exercised. These checks do not establish latency or device-performance claims.

The product goal is native UWP preview and simple annotations, with old EdgeHTML
scroll/pinch/pen behavior and Quick Look-style UI/window sizing. PDF body editing
is outside scope; OCR is a later extension. Architecture choices are not evidence
that latency, memory or compatibility targets have been achieved.

The target combines old EdgeHTML reading/input behavior with Quick Look-style
presentation. Windows owns scrolling, pinch zoom and live ink input; the PDF
layer supplies page content. This is a behavioral target, not a claim to reproduce
Edge's unpublished implementation. The UI can remain C#; C++/WinRT is the target
native bridge, with ARM32 compatibility preserved throughout migration.

Acceptance priority is scrolling/white-page regressions, pinch and pen behavior,
then launch/memory measurements and remaining architecture/device coverage.
Compiler modernization alone does not establish a performance improvement.
The user-approved baseline is Windows 10 1607 (14393). The x86/x64/ARM32 application,
native configurations and package manifest target/minimum are 10.0.14393.0.
The separate ARM64 build uses SDK 26100, minimum 17763 and UWP/.NET Native 2.2;
it does not retarget the older architectures.
ARM32 remains a required architecture. The default C++/WinRT component uses a newer build
SDK; that does not by itself establish old-device runtime compatibility. System UWP XAML remains
the baseline; WebView2, Electron, WinUI 3 and Chromium PDF rendering are outside
the primary viewer architecture. The existing work-area-only WebView fallback
is separate from PDF content and remains a compatibility decision to resolve.

| Requirement | Target implementation | Current evidence | Remaining acceptance |
| --- | --- | --- | --- |
| Native UWP/system XAML | Native UWP with Windows.UI.Xaml; C# UI may remain | Viewer project and ScrollViewer/InkCanvas sources | Current package launch and input validation |
| System PDF + DirectX surfaces | Windows.Data.Pdf + D3D11/IPdfRendererNative; system bitmap fallback | Shared standard C++ device/render core; legacy and C++/WinRT component builds; headless production-core pixels verified | Current-build XAML rendering, device loss, suspend/resume and fallback validation |
| Native inertia and pinch zoom | ScrollViewer owns inertia and ZoomMode; retain existing surfaces while refining | ScrollViewer handles input; rendering is asynchronous | Touch hardware: fling, reverse direction, pinch while scrolling, no visual jumping |
| Mixed page sizes and reading position | Actual rotated CropBox geometry; stable reading anchor and explicit fit reference | Page geometry confirmed before raster/ink/search positioning; production anchor tests cover page fractions, borders/gaps, zoom, tiny pages and cumulative differences across 600 pages. Auto-fit reference stays on the last explicitly fitted page | Current UI: mixed portrait/landscape, rotated/cropped pages, distant jumps, background prefix corrections, resize during reading; verify no top-of-page jump or automatic zoom change during scrolling |
| No scrolling stalls or repeated white pages | Visible-first asynchronous rendering, directional prefetch, surface reuse and virtualized page controls | Visible-first scheduling; annotation reads do not block raster scheduling. Device setup uses real async native factories. Current production-loop tests verify yielding during scroll/zoom rather than repeatedly selecting rejected thumbnails. Horizontal/wheel/direct-manipulation state and backend retry/generation cleanup tests pass. Visible replacements avoid a null image source and use a bounded frame retirement queue | Current Release build: 600-page image fixture, cold/warm rapid scroll; record frame times and page latency; verify actual XAML replacement timing |
| Thin scrollbar with usable hit target | Vertical Auto, horizontal Hidden; fixed 3-DIP thumb inside a 12-DIP hit area | XAML template uses 3-DIP visual / 12-DIP hit area; actual mouse drag inside and 6 DIP outside the visible thumb moved the 600-page document | Touch interaction; quantify visual width across DPI and pointer states |
| Low-latency pressure-sensitive pen | InkCanvas/InkPresenter owns live pressure ink and erasing; page-local coordinates | Native InkPresenter and pressure-enabled attributes | Surface Pen hardware: pressure, eraser, palm rejection, zoomed-page alignment and latency |
| Text selection/copy/highlight | Auxiliary native text geometry; selectable text and simple highlights without body editing | Production native geometry tests: ordinary text, CJK, angled text, rotated CropBox; JPEG/JPX XObjects and image ActualText. Actual x64 Debug UI: CJK drag, right-click copy, clipboard paste into search, highlight/save/reopen passed | Double click, shortcuts, zoom/resize and rotated-page interaction |
| Search | Cancelable page-wise native text search with bounded text retention | Production matching tests: forward/backward anchors, overlap, whitespace, CJK, ligatures and surrogate pairs. Worker/stale-result tests pass. Actual x64 Debug UI: forward/backward and both wraps passed after selected-page cursor correction | Query changes during search, cancellation and malformed pages in current UI |
| Local annotation persistence | Per-document/page sidecars; dirty-only saves; unsaved annotations survive recycling | Sidecar load/save code; clean offscreen cache eviction; actual CJK highlight save, file switch and reopen passed | Ink save/reopen; unsaved strokes survive virtualization; undo remains correct |
| Standard annotation export | Separate PDF with standard Highlight/Ink and appearance streams | Production batch export reopens; native text/geometry, page boxes/rotation preserved; Highlight/Ink with AP verified; JPEG/JPX compressed image bytes preserved. Writer page sizes match Windows.Data.Pdf rotated CropBox sizes; queued size reads survive close. Windows Ink pressure/constant-width/rectangular outlines, PointTransform and rotated PenTipTransform export as filled APs, with standard InkList; crop-aware pixel-mask IoU .976/.969 | Picker flow; current UI strokes, undo/save/reopen/export without visiting saved mixed-size pages, and other PDF viewers |
| Quick Look sizing | First-page point size, scale <= 1 and work-area bounds; retain manual size within one session | First-page sizing calculation and native work-area bridge compile | A4/Letter/landscape, DPI, multiple monitors, manual-resize session behavior; native region API runtime |
| Bounded memory and fast launch | Budgeted raster/text/annotation caches; measure total Release process memory and startup | First-page/visible-worker startup is independent of window sizing; one in-flight legacy work-area probe. Production startup, file-switch/resize race and cleanup checks pass. Event-driven adaptive raster budget, native streamed text, clean annotation eviction; production memory controller/policy and foreground/dirty preservation checks pass; no packaged PDF.js | Current Release: startup, peak/private memory, real Windows memory-pressure cleanup, repeated scroll/file switches; no monotonic cache growth |
| x86/x64/ARM32/ARM64 | Native build/package and runtime validation for each architecture; preserve older-device path | Debug and Release/.NET Native x86/x64/ARM32 solution builds; separate ARM64 Debug/Release .NET Native packages pass native image, registration and dependency checks | Architecture-specific installation/runtime tests |
| C++/WinRT native layer | Shared standard C++ cores with all six renderer/ink/display/text/writer/DTO classes; default app bridge, preserving ARM32 | All eight current default packages pass baseline/registration/exact DLL/license checks with no legacy bridge. x86/x64 Debug/Release actual document DLL activation, UTF-16 geometry, stream cloning, queued close, error recovery and unload pass. Shared core short I/O, concurrent reads and 14 injected stream failures pass; standard exports and rendered ink alignment pass | XAML/display runtime, ARM32/ARM64 and old-Windows device verification |
| Visual Studio diagnostics | Stable Diagnostic Tools session with usable CPU/memory traces for comparable runs | Executable/service/COM checks pass. Computer-use observed actual memory/CPU curves in the resumed VS 2015 session and two fresh F5 sessions; latest session ran over 8 minutes without the failure banner | Save comparable controlled performance traces; graphs alone do not establish latency or Release memory |

2026-10-09 complete C++/WinRT document bridge migration:

- Text extraction and annotation writing now use `PdfDocumentCore` and
  `PdfAnnotationJson`, shared by the optional legacy and default modern bridges.
  Standard C++ stream callbacks return HRESULT; native ownership is transferred
  explicitly across the MuPDF exception boundary. Binding conversion consumes
  the native error, preventing duplicate unhandled-error reports on destruction.
- All six modern runtime classes are packaged in `PdfNative.Rendering.dll`.
  Modern apps no longer reference/package `PdfNative.dll`; the separate ARM64
  solution no longer builds C++/CX projects. x86/x64/ARM32 retain the explicit
  `PdfRendererBridge=Legacy` option. The primary application baseline remains
  target/minimum 14393; ARM64 remains separate at 17763/26100.
- `artifacts/text-winrt/package-checks.json` records eight current packages,
  source snapshots, package hashes/sizes, six registrations and exact licenses.
  `scripts/Test-AppPackageMatrix.ps1` verifies these artifacts and each
  component's source/archive hashes. Modern component incremental reuse checks
  the pinned engine inputs, tools, metadata and built output hashes.
- `scripts/Test-CppWinRTDocument.ps1` passed actual x86/x64 Debug/Release DLL
  checks. Input/output callers can close before queued work finishes; queued
  read/size operations survive owner close/release. Weak references and DLL
  unload checks verify completed work releases owners. Invalid streams, page
  indexes, JSON, null arguments and closed objects are exercised. ARM32/ARM64
  Debug/Release test executables cross-compile; device execution remains unproven.
- `scripts/Test-PdfDocumentCore.ps1` passed x64 Debug/Release checks with short
  reads/writes, 16 concurrent reads, 14 injected stream failures, successful
  recovery and weak stream lifetime checks. This test links the same pinned
  modern parser/writer archive and current production core.
- `tests/VerifyNativeDocument.py` and `tests/VerifyNativeInk.py` accept
  `--exe`, `--component` and `--output-root` for the actual modern DLL. Current
  x64 Release exports preserve CJK/ordinary/angled/rotated CropBox geometry,
  text and JPEG/JPX compressed image data. Standard Highlight/Ink APs and
  system pressure/constant-width/rectangular outlines pass; rendered ink mask
  IoU is 0.976/0.969 on ordinary/rotated pages. Reports and PDFs are in
  `artifacts/text-winrt/x64/Release`. Poppler's existing host font-substitution
  warnings remain; these checks do not measure interactive pen latency.

These checks establish native migration and packaging, not current XAML
presentation, input latency, whole-app memory or Visual Studio CPU/memory graphs.
The full product goal remains open for those acceptance steps.

2026-10-09 desktop and environment follow-up:

- Computer-use initially observed all three VS 2015 solution projects failing
  to load with a missing SDK 14393 message; restarting and explicitly reloading
  reproduced the message. Independent MSBuild 14 enumeration found the SDK.
- After the user's manual SDK confirmation, read-only process inspection found
  VS running the solution and a live `Debug-CppWinRT/AppX/LitePdfViewer.exe`.
  Both `Windows.Data.Pdf.dll` and `PdfNative.Rendering.dll` were loaded. The
  current launch manifest retains target/minimum 14393. This supersedes the
  earlier load-failure observation, without establishing its root cause.
- `scripts/Test-VS2015Sdk.ps1` checks SDK discovery in a fresh 32-bit .NET
  Framework process using MSBuild 14, all platform contracts and x86/x64/ARM
  libraries. It passed; evidence is `artifacts/diagnostics/sdk2015-check.json`.
  No SDK retarget or SDK manifest replacement was made.
- The collector executable/service/COM check also passed again. The live app
  module hashes and a single uncontrolled Debug memory sample are recorded in
  `artifacts/diagnostics/live-app-check.json`; neither check establishes usable
  VS graphs or Release performance.
- The user stopped computer-use with Escape before PDF interaction tests at
  that point; later explicit reauthorization resumed desktop validation. A separate
  test package, `Ben.UwPdfComputerUse`, was registered from the checked x64
  Debug package under `artifacts/computer-use/x64-debug` (test-only identity,
  display name and protocol). The original deployment was restored after a
  same-version registration attempt was rejected; original app data was not
  removed. The separate test package was not launched or visually accepted.

2026-10-09 background native document cleanup:

- Export now awaits native writer disposal on a worker before closing its input
  or opening the save picker. A text document whose open completes after a file
  switch is also disposed on a worker. Final native store release can otherwise
  execute on the UI thread; these changes remove those two synchronous paths.
- `scripts/Test-NativeCleanup.ps1` compiles the actual export handler and
  `PdfTextSource` with native/platform doubles and a single-thread UI context.
  Success, append/write errors, cancellation and abandoned text opens pass:
  the UI heartbeat runs during native teardown, cleanup occurs exactly once,
  input closes afterward and errors/cancellation retain their original behavior.
  Both synchronous-close negative controls fail their intended assertions.
  Source hashes and scope are in `artifacts/native-tests/native-cleanup/result.json`.
- All eight default application builds and package checks pass with these C#
  changes. Logs are `artifacts/cleanup-ui/<architecture>/<configuration>/build.log`;
  the current package/source record is `artifacts/text-winrt/package-checks.json`.
  Isolated build outputs preserve the user's running VS debug deployment.
- This verifies cleanup control flow and packaging. Actual PDF teardown cost,
  scrolling white pages, input/frame latency and current VS graphs remain open
  for desktop/performance acceptance.

2026-10-09 background managed text work:

- After native extraction, managed glyph/quad expansion is explicitly queued on
  a worker even when the native task completes synchronously. Cancellation is
  checked every 256 glyphs (including surrogate pairs at odd UTF-16 offsets).
  The caller rechecks cancellation and source disposal before returning glyphs;
  cancellation/failure still releases the text gate for subsequent reads.
- Cached and newly extracted search pages queue normalization, glyph mapping
  and matching on a worker. Only immutable per-page data is passed to that work.
  UI publication checks the search version and document generation again after
  matching; changing the query/file cannot publish an old match or progress.
  Cancellation reaches both managed geometry expansion and search mapping.
- `scripts/Test-TextWorkers.ps1` compiles production text source, matching and
  search-loop methods with synchronous native/platform doubles. An occupied
  worker makes the scheduling tests deterministic. CJK/surrogate/angled quad
  mapping, bounded cancellation, gate retry, disposal during queued conversion,
  cached forward/backward/wrapped/no-match scans and stale query/file results
  pass. Three negative controls restoring UI glyph conversion, UI search and
  missing post-match generation checks fail their intended assertions.
  Source hashes are in `artifacts/native-tests/text-workers/result.json`.
- `Test-TextSearch.ps1` and the production cleanup tests also pass after the
  changes. These checks establish scheduling and result ownership; native
  extraction cost, whole-app memory and actual input/frame latency still need
  desktop/device measurements.
- All eight default Debug/Release application builds completed with these
  changes; logs are `artifacts/text-workers/<architecture>/<configuration>/build.log`.
  The current source/package record is `artifacts/text-winrt/package-checks.json`.
  Builds used isolated outputs, preserving the running user's debug deployment.

2026-10-09 search continuation correction:

- Computer-use reproduced a forward-search regression in `performance-600.pdf`:
  the selected heading on page 168 was followed by page 167 after Enter. Bringing
  the heading into view left the prior page's tail visible, so the reading-page
  indicator differed from the selection page. Search incorrectly reused that
  reading indicator as its cursor and discarded the selection anchor.
- Next/previous now start from a valid selection belonging to the current
  document. A restarted query still starts at the reading page. This preserves
  the leading-edge indicator's intentional behavior for short/landscape pages.
- Production search-loop tests cover both directions when the selection and
  reading pages differ, plus query restart. The ReadingCursor negative control
  restores the former calculation and fails the forward-continuation assertion.
  The text-worker suite now has four negative controls.
- Actual VS x64 Debug UI verification passed: forward 1 -> 2 -> 3, backward
  3 -> 2 -> 1 -> 600, then forward 600 -> 1 for the repeated heading query.
  Even with the prior page's tail still visible, navigation follows the selected
  result. ARM/device and input-latency acceptance remain separate.
- All eight default Debug/Release builds and package checks passed after this
  correction. Logs are `artifacts/search-cursor/<architecture>/<configuration>/build.log`;
  `artifacts/text-winrt/package-checks.json` records the current source/package
  hashes, OS baselines, registrations, dependencies and exact licenses.

2026-10-09 resumed computer-use acceptance:

- The original VS 2015 deployment was tested, not the separate test identity.
  The running x64 Debug application uses the default C++/WinRT bridge. Actual
  Diagnostic Tools CPU and memory curves were visible in the existing session
  (over 186 minutes) and both fresh F5 sessions. The final corrected-search
  session exceeded 8 minutes; the CPU tooltip and live private-byte curve were
  readable, with no unexpected-failure banner. The collector JSON remains a
  service/COM check, not evidence of these GUI observations.
- `known-text.pdf`: a mouse drag selected the complete Chinese heading;
  the right-click menu offered copy, highlight, select all and cancel selection.
  Copy followed by clipboard paste into search produced the exact heading.
  A yellow highlight was saved, the file was switched, and reopening restored
  the highlight. The normal save picker exported
  `artifacts/native-tests/computer-use-highlight-export.pdf` (3316 bytes).
  Independent pypdf inspection verified three pages and a page-2 `/Highlight`
  with eight QuadPoints numbers and an `/AP` appearance stream.
- `performance-600.pdf` (34,628,642 bytes): actual wheel forward/reverse,
  Ctrl+End/Home and long thumb drags reached distant pages with page images
  present in every post-input snapshot. Dragging 6 DIP left of the thin visible
  thumb also scrolled successfully. These screenshots settle after input;
  they cannot rule out transient white frames or measure inertia/input latency.
- Uncontrolled Debug samples after repeated scrolling showed private bytes
  fall from 283,353,088 to 202,743,808 rather than only increase. Debug output
  reported individual native surface operations at 18-25 ms for the observed
  sizes. Neither is a controlled peak-memory, frame-time or Release result.
- Hardware pinch, Surface Pen pressure/palm rejection, Release frame timing,
  actual memory-pressure events and ARM/old-Windows device runtime remain open.

2026-10-10 actual x64 Release desktop measurement:

- The checked x64 Release package was extracted under
  `artifacts/computer-use/x64-release` and registered with the separate test
  identity `Ben.UwPdfReleaseVerification`. Only identity, display name and
  protocol differ; executable/native payloads are those of the checked package.
  The original VS Debug deployment and its annotation data were preserved.
  Missing Microsoft.NET.Native.Framework.1.3/Runtime.1.4 dependencies were
  installed from the package's bundled x64 dependencies. Actual loaded modules
  confirmed .NET Native, Windows.Data.Pdf and PdfNative.Rendering. Directly
  executing the EXE lacks the package dependency context and failed; launching
  its registered application identity worked and is the correct test path.
- `Measure-ViewerProcess.ps1` collected 1372 samples over five minutes from
  that exact Release executable, initially empty, then reading the 600-page
  fixture and switching to `known-text.pdf`. Requested sampling interval was
  200 ms; actual sample times are recorded. Observed maximum private bytes:
  277,336,064 (264.5 MiB); maximum sampled working set: 365,068,288 (348.2 MiB).
  Last sample after the file switch: 183,222,272 private bytes (174.7 MiB).
  This is one finite run, not a leak proof, GPU total or controlled startup test.
  Data and operation times are `artifacts/performance/release-scroll-20261010.json`
  and `release-scroll-actions-20261010.json`. The final collector also passed
  a short actual-process run; the long report records its earlier script hash.
- Actual Release UI wheel forward/reverse, first/last-page jumps and thin/wide
  thumb drags displayed page images in every settled snapshot. WPR rejected
  CPU/GPU/XAML/desktop-composition recording with `0xc5585011` (cannot enable
  system profiling policy); subsequent status confirmed no recording. No ETL
  or frame-time claim is made. Transient whites and input latency remain open.
- A separate XAML event-only WPR profile, with no system sampler, also failed
  to start with `0x80070005` (access denied). WPR status again confirmed no
  recording. No profiling or security policy was changed to bypass the failure.
- Two automated mouse strokes were saved through Ctrl+S and exported through
  the normal picker. Independent inspection of
  `artifacts/native-tests/computer-use-release-ink-export.pdf` (5673 bytes)
  confirmed three pages and two standard `/Ink` annotations with InkList and
  `/AP`. After closing/restarting the Release application, reopening the input
  restored both strokes at the same relative page positions. Automated drags
  produced short captured strokes, so this does not establish complete pointer
  trajectories, pressure, palm rejection or hardware pen latency.
- The cold reopen also exposed a sizing discrepancy: the window grew to the
  intrinsic page size, while the initial fit stayed at 67% from the earlier,
  smaller startup viewport. The page and ink remained aligned. This requires
  a window-sizing/initial-fit correction before Quick Look sizing acceptance.

2026-10-10 initial fit after intrinsic window sizing:

- Once asynchronous automatic sizing completes, the current document performs
  the existing anchor-preserving layout/refit operation. It uses the settled
  viewport, without restarting first-page rendering or scrolling to page zero.
  Existing custom-zoom mode is respected; the document-generation check runs
  before the operation so an older open cannot refit a replacement document.
- The production startup/control-flow suite checks that this final operation
  happens after sizing and preserves simulated user zoom/reading ownership;
  stale-open completion cannot invoke it. `NoFinalFit` restores the missing
  operation and fails the intended assertion. Viewport-anchor geometry checks
  also pass. These doubles do not establish actual viewport timing or pixels.
- Actual x64 Release cold-open verification with the same 600x800-point PDF
  changed from the old 67% result to 100% in the 738x868-DIP window, on page 1.
  Both saved mouse strokes remained aligned. The revised checked Release
  payload runs from `artifacts/computer-use/x64-release-final-fit`; only its test
  manifest identity/display/protocol and test version 1.0.8.1 differ, retaining
  the same test family annotation data. Production package version is unchanged.
- All eight Debug/Release application builds and package verifiers passed with
  the refit correction. Logs are under `artifacts/preview-final-fit`; current
  package/source hashes are in `artifacts/text-winrt/package-checks.json`, and
  live tested Release module hashes are in
  `artifacts/performance/final-fit-live-release.json`. All five startup negative
  controls failed their intended assertions.
- Computer-use additionally exposed Alt+Space advancing the reading page while
  opening the window's system menu. The key handler currently accepts Space
  without checking Alt; this separate keyboard regression remains to correct.

Reproducible legacy native document check: `scripts/Test-NativeText.ps1`, followed by
`tests/VerifyNativeDocument.py`. Generated inputs/results are in
`artifacts/native-tests`; the geometry-derived highlight export is
`geometry-exported.pdf` and can be rendered with Poppler for visual inspection.

Debug build logs are evidence of compilation only. Runtime observations from an
older build do not prove the latest native text/search/export/cache changes.

Earlier geometry build evidence: `artifacts/geometry-final-debug-{x64,x86,ARM}.log` and
`artifacts/geometry-final-release-{x64,x86,ARM}.log`, all with zero errors. The native test
harness also exercises caller-stream positions/closure and queued-read closure.

After sharing the rendering core and adding the opt-in bridge, the default
application was rebuilt in all six configurations with zero errors:
`artifacts/render-core-{debug,release}-{x64,x86,ARM}.log`. The default packages
were inspected and contain no opt-in renderer DLL. This separates the new
bridge's build/package evidence from the existing ARM32-compatible application.

C++/WinRT renderer migration evidence: `artifacts/cppwinrt-app-{debug,release}-{x64,x86}.log`.
The opt-in packages live under `LitePdfViewer/AppPackages/CppWinRT`. The default
ARM32 bridge remains buildable and shares `PdfNative/PdfRenderDevice.cpp`; this
does not yet migrate the auxiliary native components or establish ARM64 support.

`scripts/Test-CppWinRT.ps1` loads the real component DLL with its activation
export and exercises its WinRT ABI. It checks failed async-call object lifetime
and unloading, then reads a GPU-rendered image from the production core to reject
blank/uniform output. It deliberately does not claim to test a successful XAML
surface draw through the DLL. Results and component/input hashes are saved in
`artifacts/cppwinrt/{x64,x86}/{Debug,Release}/smoke-result.json`.
`scripts/Test-CppWinRTPackage.ps1` checks each generated package's architecture,
activation registration and renderer DLL hash. All four configurations passed.
The Release renderer DLL on this host is approximately 202 KiB (x64); this is a
component size, not the full application size or process memory.

Scheduler regression check: `scripts/Test-RenderScheduler.ps1` compiles the
production scheduler and exercises blank-page priority, gesture deferral,
failed-page isolation, changed viewport ranges and bounded directional prefetch. This proves selection order,
not runtime scrolling latency or absence of white pages on hardware.

2026-10-08 scrolling/backend follow-up, before the 14393 retarget:

- Both native bridges expose an async device factory; setup no longer happens
  in the C# render continuation on the UI thread. Actual legacy and C++/WinRT
  factory smoke checks pass. XAML surface creation and EndDraw remain UI work.
- Unsupported setup/interop is suppressed until backend reset. Page-specific
  draw errors keep the device; device loss recreates it. E_OUTOFMEMORY remains
  retryable. Stale initialization/draw/decode/upload results are cancelled and
  partial allocations are released. `Test-RasterBackend.ps1` compiles the actual
  backend against platform doubles and checks these decisions and lifetimes.
- `Test-ScrollActivity.ps1` compiles the production horizontal/vertical/wheel/
  direct-manipulation state helper, including paused gestures and reset behavior.
- `Test-RenderLoop.ps1` extracts and compiles the current production worker with
  work/platform doubles, then verifies that missing pages render while gestures
  are active, rejected thumbnails are not selected in a tight loop, background
  work resumes, and hidden windows wait. `render-loop-result.json` records the
  source hash. This does not execute real XAML/input events.
- New page/detail/thumbnail rasters are attached before old resources retire.
  Old visible resources receive two distinct rendering callbacks of grace,
  capped at 32 MiB/four entries; hidden/suspend/reset/unload cleanup is explicit.
  Transition bytes are counted when trimming page-cache candidates. The actual
  retirement policy passes frame duplication, staggered replacements, byte/entry
  pressure and repeated-clear tests. This is a bounded grace period, not proof
  of compositor/GPU completion or a guarantee that all white pages are fixed.

Default follow-up logs: `artifacts/raster-init-{debug,release}-{x64,x86,ARM}.log`.
Opt-in follow-up logs: `artifacts/raster-init-cppwinrt-{debug,release}-{x64,x86}.log`.
Release configurations use .NET Native. All ten configurations build successfully.
The native rendering core was not changed in this follow-up; the engine-only
baseline below is not an app performance comparison for these changes.

2026-10-09 baseline change: target and minimum are now 10.0.14393.0 in the
application, both legacy native projects and source manifest. Native test and
benchmark scripts use SDK 14393. Build/package evidence from before this change
does not prove the retarget. `scripts/Test-PackageBaseline.ps1` checks the actual
packaged Windows.Universal minimum and MaxVersionTested; it rejects an older
10240/10586 package.

Surface-content loss is now handled through the system CompositionTarget event,
in addition to resume and draw-time device errors. Suspend cleanup accesses the
MainPage directly from Window.Content, matching the application activation path.
Current-build GPU loss/suspend/resume behavior still requires runtime validation.

Retarget evidence: the signed Microsoft SDK installer completed with exit code
0. Default Debug/Release x86/x64/ARM32 builds succeeded, including .NET Native
Release: `artifacts/sdk14393-{debug,release}-{x64,x86,ARM}.log`. The four opt-in
builds also succeeded: `artifacts/sdk14393-cppwinrt-{debug,release}-{x64,x86}.log`.
All ten actual package baselines passed; the four opt-in packages passed
registration/DLL hash checks, and default packages contain no opt-in renderer DLL.
`artifacts/sdk14393/package-checks.json` records source and package hashes and a
UTC timestamp. Legacy async-factory and native ink checks were rebuilt against
SDK 14393; native text/export and exported ink alignment checks passed. These
checks ran on this host and do not prove Windows 1607/ARM device runtime or input
latency. The current collector service/COM check also passed after SDK setup;
Visual Studio graph collection remains unverified.

2026-10-09 ARM64 renderer follow-up:

- The Visual Studio Installer exited with code 5007 because the calling process
  was not elevated; no system ARM64 component installation was completed.
- `Prepare-CppWinRTArm64.ps1` prepared a local compiler/OneCore CRT overlay from
  this host's installed VS 2026 catalog. Official payload SHA-256 values were
  verified before extraction; matching installed headers were copied locally.
  `artifacts/arm64-tools/toolchain.json` records catalog hash, selected package
  identities and payload hashes. The system installation is unchanged.
- The actual C++/WinRT renderer cross-compiled for ARM64 Debug and Release with
  SDK 26100. `build-result.json` in each output directory records toolchain,
  source, DLL and metadata hashes. `Test-CppWinRTBinary.ps1` verifies those
  hashes, ARM64 PE machine, DLL/AppContainer/ASLR/NX flags, activation/unload
  exports and absence of dynamic CRT dependencies. Both configurations passed.
- `Test-CppWinRT.ps1 -Platform ARM64 -SkipBuild -CompileOnly` builds the real
  activation/device/pixel smoke harness for execution on an ARM64 device.
  Cross compilation does not execute its assertions. `smoke-build-result.json`
  is kept separate from runtime smoke results and marks runtime unverified.

Subsequent full-app follow-up: `LitePdfViewer.ARM64.sln` and its C# project use
the same source/XAML/asset item file as the default application, UWP 6.2.14 and
.NET Native 2.2. The package/project minimum is 17763, target SDK 26100; default
x86/x64/ARM32 configurations remain v140/14393. ARM64 native auxiliary projects
now have v145 configurations. The local overlay includes Store CRT and C++/CX
platform metadata; `Prepare-UwpArm64Targets.ps1` copies the installed v180 base
targets and adds hash-verified official ARM64/UWP rules. The build uses x64 host
tools and disables MSBuild node reuse; no system registration is changed.

The complete ARM64 Debug/Release builds passed, including actual .NET Native code
generation and MSIX packaging. `Test-UwpArm64Package.ps1` checks all executable/
DLL payloads are native ARM64, five required WinRT registrations, both component
DLL hashes, the 17763/26100 manifest baseline and .NET Native 2.2/VCLibs
dependencies. `artifacts/arm64-tools/package-{debug,release}-result.json` records
actual package hashes, images and dependencies. Debug correctly uses
Microsoft.NET.Native.Framework.Debug.2.2, and includes an ARM64 debug UCRT;
the checker distinguishes this from the Release framework dependency.
Default x86/x64/ARM32 and all four x86/x64 opt-in builds passed after the shared
item-file change. The Debug build reported PRI257 because
its resource default is Chinese and generated resources include English; this
warning does not establish a localized-resource runtime result.

The new renderer build record initially failed under MSBuild 14's PowerShell
environment because Get-FileHash was unavailable. Build-CppWinRT now uses .NET
SHA-256 directly for those records; all four actual legacy-host opt-in builds
passed, and ARM64 Debug/Release packages were rebuilt with the corrected script.
All six renderer binaries pass architecture, export, static CRT and build-hash
checks. Actual x86/x64 Debug/Release DLL activation/device/pixel smoke checks
passed against the newly built binaries; ARM64 smoke executables cross-compiled
but were not executed.

The final package pass checks six default, four x86/x64 opt-in and two ARM64
packages. `artifacts/arm64-tools/package-checks.json` records all twelve package
hashes, current application/native/project input hashes, OS baselines and scope.
The earlier SDK retarget report remains historical after the shared item-file
and ARM64 configuration changes; use this newer report for the current state.
Successful ARM64 XAML drawing, touch/pen input, installation and performance
still require device validation; package structure does not prove these.

Page-position regression check: `scripts/Test-PageGeometry.ps1` compiles the
production `PdfViewportAnchor` helper. It verifies mathematical re-anchoring,
not the timing of XAML layout or cancellation of a pending anchor by real input.

Engine baseline: generate `artifacts/native-tests/performance-600.pdf` with
`tests/CreatePerformanceFixture.py`, then run `scripts/Measure-NativeRaster.ps1`.
The fixture is 600 pages, 40 unique JPEG images and 34,628,642 bytes. Reports
record the input SHA-256 and 128 renders per output width: forward, reverse,
jump and repeated-jump samples. They measure a standalone Windows.Data.Pdf /
IPdfRendererNative host, with event-based GPU completion and a single reused
surface. They exclude application caches, XAML, auxiliary text and input. Their
working-set/private-memory values are not the viewer's total memory acceptance.

2026-10-08 engine baseline, after solution builds completed, on this host's
hardware device (128 samples per width):

| Output width | GetPage P95 | Render + GPU completion P95 | Peak process working set |
| --- | --- | --- | --- |
| 768 px | 0.01 ms | 22.83 ms | 212.9 MiB |
| 1600 px | 0.02 ms | 24.87 ms | 253.2 MiB |

The baseline above predates sharing the production core. Its reports are
preserved as `raster-benchmark-*-before-core-sharing.json`. A subsequent run
through `PdfNative/PdfRenderDevice.cpp`, after all builds completed, gave:

| Output width | GetPage P95 | Render + GPU completion P95 | Peak process working set |
| --- | --- | --- | --- |
| 768 px | 0.01 ms | 22.96 ms | 212.9 MiB |
| 1600 px | 0.02 ms | 25.40 ms | 253.3 MiB |

These pre-retarget reports record production-core/benchmark source hashes and a UTC
completion timestamp. Similar sampled numbers do not establish equivalence
across devices or prove application-level latency improvements.

These are sampled engine results, not a before/after app comparison or a frame
time guarantee. Earlier measurements overlapped compilation and were slower;
wait for builds to finish before collecting a comparable baseline. The app's
600-page cold/warm scroll and total-memory acceptance remain outstanding.

2026-10-09 memory and surface-recovery follow-up:

- Device recovery now resets failure flags for all pages, including first-draw
  failures that never entered the raster cache. Valid software base images
  survive a native surface reset; lost native details/thumbnails are discarded.
- Loaded/unloaded handlers subscribe/unsubscribe `MemoryManager` usage and
  limit notifications. Worker notifications are coalesced onto the view
  dispatcher, and the early `NewLimit` is retained until the property catches
  up. No per-frame memory polling or forced GC was added.
- Raster budgets are normally at most 64 MiB and one eighth of the system app
  limit, capped at 32/8/4 MiB for Medium/High/OverLimit (1-MiB foreground floor).
  Pressure hysteresis avoids immediately refilling at Medium after eviction.
  High pressure releases offscreen rasters/presenters/clean annotations,
  thumbnails/detail patches and retired images, suspends speculative work,
  and keeps foreground base images and unsaved annotations. Hidden windows
  release their remaining images when notified under pressure.
  A transition back to safely low pressure resets page/detail/thumbnail/text
  failure flags once. Repeated Low notifications do not repeatedly retry a
  persistent failure; the regression first exposed and then covered this gap.
- `Test-MemoryRecovery.ps1` compiles the actual controller/policy and extracts
  production trimming, clean-annotation release, render scheduling and recovery
  methods. Platform/work-double checks cover event coalescing, early limit
  changes, reload generations, static subscription cleanup, integer extremes,
  hysteresis, foreground/dirty/pending-ink preservation, hidden cleanup and
  prefetch resumption. Negative controls removing uncached-failure recovery or
  ignoring NewLimit fail the corresponding regression assertions.
- Retirement budget shrink/zero/recovery checks and existing production-loop
  and backend regression checks pass. These headless checks exclude actual OS
  pressure, XAML presentation, touch/pen input and total application memory.
  Current-build hardware scrolling/white-page/performance acceptance is pending.
- All 12 final-source builds and package checks passed after the recovery
  follow-up: default Debug/Release x86/x64/ARM32, opt-in Debug/Release x86/x64,
  and full Debug/Release ARM64. Source hashes were captured before compilation
  and rechecked afterward. The refreshed `artifacts/arm64-tools/package-checks.json`
  records source/package hashes and verification scope; legacy build logs use
  the `memory-{default,cppwinrt}-{debug,release}-{platform}.log` names. ARM64
  Debug retains the previously recorded PRI257 resource-language warning.
- Memory/recovery tests also pass under Windows PowerShell 5.1, with current
  production and harness hashes in `artifacts/native-tests/memory-recovery-result.json`.
  A third negative control omitting the safe-pressure retry transition fails.
  The current VS 2015 collector executable/service/COM check passes; current
  Visual Studio graph collection remains unverified.

The production-core engine benchmark was rerun after all compiler processes
exited, using SDK 10.0.14393.0 on this x64 host, hardware rendering and the same
600-page fixture (128 renders per width). The earlier post-core-sharing reports
were preserved as `raster-benchmark-*-before-sdk14393-*.json` before replacement.

| Output width | GetPage P95 | Render + GPU completion P95 | Peak process working set |
| --- | --- | --- | --- |
| 768 px | 0.01 ms | 22.61 ms | 208.5 MiB |
| 1600 px | 0.02 ms | 24.35 ms | 252.4 MiB |

The `raster-benchmark-{768,1600}.json` reports record SDK, input/source
hashes and UTC completion times. The CLI compiler emitted C4447 for its `main`
signature; the harness explicitly initializes WinRT and both runs passed.
These measurements reuse one surface and exclude XAML, the auxiliary engine,
adaptive page caches and live input. They establish a current-SDK engine
baseline, not a before/after comparison of this memory change or Windows 1607
device runtime, frame presentation, scrolling/pen latency or total app memory.
These reports precede the ARM32 header cleanup below; their recorded source
hashes are historical and do not describe the final application packages.

2026-10-09 ARM32 C++/WinRT follow-up:

- `Prepare-CppWinRTArm32.ps1` prepares MSVC 14.44.35207 locally from the installed
  Visual Studio catalog's pinned 17.14 compiler, English resources, CRT headers
  and ARM32 static OneCore CRT. Official VSIX payload hashes are verified;
  compiler/CRT servicing package versions are separately recorded. No system
  installation or registration was changed.
- SDK 26100 lacks ARM32 libraries and its C++/WinRT base rejects ARM32. The
  build uses the original Microsoft.Windows.CppWinRT 2.0.230706.1 generator for
  matching base and Windows projections. Its Microsoft author and NuGet
  repository signatures passed `dotnet nuget verify --all`; the archive SHA-256
  is pinned and its MIT notice is copied into packaged third-party licenses.
- ARM32 links UCRT/UM libraries from SDK 14393 and uses its Win32/PDF declarations.
  Modern UCRT/WRL helper headers, MIDL and reference metadata use SDK 26100;
  these are recorded separately in `build-result.json`. The shared device header
  now includes only WRL's client smart pointers, avoiding an unused module/factory
  dependency. No modified vendor headers or fabricated architecture macros are used.
- Both Debug/Release renderer binaries cross-compiled. Their source/output
  hashes, ARMNT (0x1c4) machine, AppContainer/ASLR/NX, activation/unload exports
  and static CRT checks pass. Both smoke executables cross-compile with runtime
  explicitly unverified; the test refuses ARM32 execution on this x64 host.
  Successful cross compilation does not establish ARM32 device activation,
  XAML drawing, Windows 1607 compatibility or input/memory performance.
- The compiler PDB now has an explicit architecture/configuration-local `/Fd`
  path. Concurrent ARM64 and legacy-solution builds previously shared the working
  directory's `vc140.pdb` and failed with C1041. The final concurrent build
  matrices both exited zero and all four Debug compiler PDBs are isolated.
  The standalone smoke harness explicitly links `ole32.lib`; narrowing the core
  WRL include exposed its implicit COM initialization/marshaler dependency.
- All 14 current-source application builds and package checks pass: six default
  x86/x64/ARM32 packages, six opt-in C++/WinRT packages, and two separate ARM64
  packages. The first twelve retain target/minimum 14393; ARM64 retains minimum
  17763 and target 26100. Auxiliary registrations, exact native DLL hashes,
  default-package isolation and the packaged C++/WinRT MIT notice are verified.
  `artifacts/arm64-tools/package-checks.json` records current source/package hashes
  and successful build exit codes; the previous twelve-package record is preserved
  as `package-checks-before-arm32-*.json`. Legacy logs are
  `artifacts/arm32-tools/app-{Default,CppWinRT}-{Debug,Release}-{ARM,x64,x86}.log`.
- The final Debug/Release x86/x64 DLL smokes execute successfully on this x64
  host, including activation, async factory, argument errors, coroutine lifetime,
  unload and shared-core pixels. The legacy async renderer factory smoke also
  passes. ARM32/ARM64 smoke executables remain compile-only. No application
  installation, old-Windows/device runtime, XAML presentation or input/performance
  acceptance is implied by these checks.

2026-10-09 preview startup follow-up:

- Document startup previously awaited work-area probing and window sizing before
  creating pages or rendering the first page. On the legacy probe path, this
  included a navigation wait of up to two seconds and the automatic-resize grace
  period. Startup now establishes its initial fit/reading position, starts sizing
  alongside first-page rendering, and starts visible/geometry workers before
  awaiting sizing completion. An initial raster's stale continuation returns
  before updating UI or starting workers for a replacement document.
- Only an in-flight legacy work-area query is shared; a completed result is not
  reused for subsequent files/monitor changes. Probe event handlers are detached,
  and the control is removed even when cleanup navigation throws. The native
  region API remains first choice; the legacy taskbar-excluding fallback remains
  for older Windows and ambiguous display regions. Screen resolution alone is
  not substituted for the monitor work area.
- Manual resizing during the query prevents a late automatic resize. The latest
  sizing request owns suppression of automatic resize events, so an older delayed
  continuation cannot clear the newer request's flag. Window/query failures are
  isolated from valid PDF loading. Initial fit/focus occur before rendering yields;
  first-page completion no longer resets a reading position established meanwhile.
- `Test-PreviewStartup.ps1` compiles actual loading/sizing/query/probe methods with
  platform/work doubles under a single-thread synchronization context. Pending
  native sizing and legacy navigation still permit first-page and worker startup;
  stale files, manual size, overlapping resize continuations, single pending
  probe, fresh monitor results, cleanup failures and nonfatal query/resize errors
  pass. Four negative controls (await sizing before rendering, omit stale-raster
  guard, clear resize ownership unconditionally, duplicate pending probes) fail
  their intended assertions. Current production memory controller, render-loop
  and page-anchor checks also pass. Input/source hashes are recorded in
  `artifacts/native-tests/preview-startup/result.json`.
- This is control-flow/lifetime evidence. No actual Windows monitor selection,
  WebView memory, cold startup, XAML frame timing, scrolling/pinch/pen latency or
  total-process memory result is claimed. The remaining C++/WinRT auxiliary
  migration and device/UI/performance acceptance still apply.
- All fourteen application builds with this startup change completed successfully:
  default and opt-in x86/x64/ARM32 Debug/Release, plus separate ARM64 Debug/Release.
  The source snapshot is `artifacts/preview-startup/source-at-build.json`, with
  legacy logs `artifacts/preview-startup/app-{Default,CppWinRT}-{Debug,Release}-{ARM,x64,x86}.log`.
  The current fourteen-package hash/registration/baseline/license checks are
  recorded in `artifacts/arm64-tools/package-checks.json`; the preceding record
  is preserved as `package-checks-before-preview-startup-*.json`. Architecture
  baselines and native renderer implementations did not change in this follow-up.

2026-10-09 ink/display C++/WinRT follow-up:

- `InkOutlineCore` now shares standard C++ outline capture between the legacy
  and C++/WinRT bridges. Fixed-size coordinates replace a separate allocation
  for each geometry command. Allocation failures are reported through COM
  geometry callbacks instead of throwing a C++ exception across that boundary.
  The modern bridge clones the live stroke before suspension, retains its owner
  through completion and exposes explicit Close. C# export selects this bridge
  in the opt-in and ARM64 application builds.
- Keeping one InkD2DRenderer across temporary stroke snapshots reproduced
  native heap corruption. The unchanged x64 Release smoke failed at iteration
  15; both captured stacks fail while destroying the renderer's cached
  StrokeInfo tree, and the first dump classifies the failure as double free.
  Each Describe now creates/releases its own renderer on the drawing thread
  while its snapshot remains alive. D3D/D2D devices and context are still reused
  under the core mutex; Close cannot race a draw on that context.
  This is an application lifetime workaround, not a claim about the unpublished
  implementation's complete root cause or behavior on all Windows builds.
- Actual x64 Release ink/display DLL testing passes 100 consecutive runs. x64
  Debug and x86 Debug/Release each pass 25 runs: 175 total, preserving 16
  concurrent Describe requests followed by repeated Close, as well as snapshot,
  invalid argument, pending-owner, failed-coroutine and unload checks. All three
  pressure/constant/rectangular transformed outlines match the saved pre-port
  legacy JSON byte-for-byte. Results, fixture/script/component/executable hashes
  and per-run outline hashes are recorded in each `ink-smoke-result.json`.
- The legacy wrapper also queues an explicit shared-core lease, with no later
  access to the exporter's native fields. `Test-NativeInk.ps1 -RepeatCount 100`
  passes 100 runs with 16 queued exports during Close and the same byte parity.
  Its newly extended test initially crashed after all checks passed because it
  called RoUninitialize before releasing its retained stroke/tasks. The dump
  shows the invalid COM vtable inside an already-unloaded Inking DLL. Apartment
  RAII now releases those objects first. This test teardown failure is distinct
  from the production renderer-cache heap corruption above.
- `PreviewDisplayCore` shares the existing guarded display-region query with
  both bridges; missing APIs, failed/null COM results, invisible/ambiguous
  regions and invalid sizes still request the existing fallback. Seventeen
  core scenarios plus null input pass with COM/API doubles and balanced
  references. Actual x86/x64 DLL tests activate the static display factory and
  verify null-view fallback; real monitor/DPI/taskbar behavior remains unverified.
- The current legacy writer exports actual modern outlines into standard Ink
  annotations and appearance streams. Text remains intact; cropped/rotated
  page alignment has pixel-mask IoU .976/.969. Page 1/3 PNGs were inspected after
  rendering. `artifacts/ink-winrt/export-verification.json` records current input,
  writer/core/test binary, PDF and PNG hashes. Font substitution notices from
  Poppler remain test-host rendering limitations.
- All 14 final-source application builds and package checks pass after the
  legacy shared-lease change. The 73-file snapshot is
  `artifacts/ink-winrt/source-at-build.json`; logs are
  `artifacts/ink-winrt/app-{Default,CppWinRT}-{Debug,Release}-{ARM,x64,x86}.log`
  and the ARM64 build logs. The canonical package record remains
  `artifacts/arm64-tools/package-checks.json`, with earlier snapshots preserved.
  Checks require all three modern activation registrations, exact DLL payloads,
  legacy auxiliary registrations, MIT notice, architecture and OS baselines.
  ARM32/ARM64 renderer and ink/display smoke executables cross-compile; their
  reports explicitly exclude runtime execution on this x64 host.
- Text extraction/annotation-writer bridge migration, live XAML/monitor/input
  acceptance, scrolling white-page/latency and total-process memory measurements,
  old-device runtime and current VS graph collection remain required.

## 2026-10-15 — ink selection, save history, pen selection and Windows OCR

- `scripts/Test-ReaderEditing.ps1` compiles a temporary in-process QA entry point,
  uses independent generated PDF fixtures, runs the native Windows APIs, then
  restores production launch/project source, rebuilds and synchronizes the
  existing Debug layout. It neither installs a package nor uses computer use,
  UI Automation, desktop screenshots or injected pointer/keyboard input.
- Native x64 QA passed rectangular selection through `InkStrokeContainer`, a
  padded drag target for thin ink, moving transforms, cancel restoration, undo
  without deleting selected strokes, editable ISF persistence, original/saved
  snapshots, native PDF preview of embedded ink and restoration of the original
  PDF plus sidecars. A missing snapshot PDF forced the restore error path: the
  original PDF bytes and editable ink survived, the reader reloaded, and input
  was re-enabled. Snapshot ownership is checked against the current document.
- Native text quad hit testing and pen tolerance passed at 64/80/100/160%; two
  selection handles retain screen-space sizing and disable system manipulation
  on the handle itself. Fingers can still pan the document outside handles.
- Actual `Windows.Media.Ocr` recognized the raster-only two-page fixture. Native
  extraction confirmed the fixture has no PDF text; automatic OCR fallback,
  normalized word boxes, selection and complete animated search (2 matches)
  passed. This host has en-US, ja, zh-Hans-CN and zh-Hant-HK OCR engines; the
  raster recognition acceptance test uses English. Recognition remains offline.
- Portable production-source text worker tests now cover native text bypassing
  OCR, empty-page fallback, explicit OCR overrides, cancellation/disposal during
  recognition and gate recovery. Copying OCR words with geometry-free spaces
  and line breaks is checked against the production selection methods. Search,
  animated scrolling, keyboard and zoom checks also pass.
- The resource validator passes 200 corresponding entries in zh-Hans, zh-Hant,
  en-US and ja-JP. New menus/dialogs use native UWP controls, the page's Light
  theme and bounded native ScrollViewers; new ink selection uses Microsoft's
  Segoe MDL2 Assets Marquee glyph and stays in More to preserve toolbar width.
- Final production x64 Debug and local signed .NET Native Release 1.0.11.0
  builds pass. Both retain local development identity for testing and the
  original F5 path; canonical manifests retain the supplied Store identity.
  The test package passes the 14393 minimum/target check and exact six native
  activation registrations/DLL hashes. Signature, four resource languages and
  all 67 logo assets were checked. Package/archive hashes and production source
  hashes are recorded next to the test ZIP in `artifacts/releases`.
- Reports, build logs and native control renderings are in
  `artifacts/reader-editing-qa`; physical barrel-button behavior, touchscreen
  operation and pen selection feel still require hardware acceptance. The
  tests do not claim physical input coverage. No test launch/compile hook is
  left in production source and the test package is not automatically installed.


## 2026-10-15 — precision touchpad zoom and default raster sharpness

- Replaced the `PointerPressed` zoom-source heuristic with tracked native
  `ChangeView` goals and `ViewChanging.NextView/FinalView` observations. This
  handles precision touchpads that never raise XAML pointer presses, native
  final callbacks after gesture completion, and user interruption of fit.
  New explicit zoom/fit requests cancel queued slider values. Snapshot restore
  retains the previous fit/manual mode while restoring zoom.
- Visible pages target 1.5× device-pixel sampling; the per-page refinement
  threshold is now 5% with an 8-pixel rounding dead band. Native x64 QA verified
  an otherwise retained 768-pixel image upgrades to a 900-pixel settled target.
  Full-page/detail pixel limits and the adaptive shared cache remain enforced;
  memory pressure disables oversampling. DPI notifications refresh render
  targets while preserving the requested zoom.
- `scripts/Test-ZoomQuality.ps1` passed native pointerless zoom remaining at
  125% through final events and delayed relayout, preserved explicit fit,
  canceled stale slider callbacks, animated 64/80/100/125/160/200% targets,
  rapid retarget followed by fit, default raster sizing and DPI refinement.
  The script records native control bitmap comparisons and reports under
  `artifacts/zoom-quality-qa`, restores temporary launch/project hooks and
  finishes with a production Debug build/layout. It uses no computer use,
  external UI automation or synthetic pointer input. Physical precision-
  touchpad hardware operation remains user acceptance.
- Production-source zoom, raster policy, render scheduler/loop, memory
  pressure/recovery, animated search and text/OCR worker regressions passed;
  all four localization catalogs still contain 200 corresponding entries.


## 2026-10-15 — high-zoom cost, native navigation and input modes

- Above 125%, overview width is limited to 1024 pixels and speculative raster
  work is suspended. The visible detail patch retains its 2 MP cap and a
  quarter-viewport runway. Offscreen detail rasters and high-zoom presenters
  are released; incoming CPU/GPU allocation is reserved by trimming before
  drawing. Renderer wakeups use Low dispatcher priority so input runs first.
  Default/fit text still uses the existing 1.5x device-pixel target.
- Fixed fit-mode page navigation issuing an immediate FitToWindow(false)
  before an animated scroll. Fit now submits a single animated view change
  for zoom and page position. Native tests observed intermediate animation
  frames through mixed page sizes and a cold image-heavy target page.
- Windows Ink supports stroke selection and transforms within its API;
  deletion is unnecessary. The Marquee button is directly on the annotation
  toolbar. Its transparent XAML overlay receives primary mouse drags in
  selection mode, while barrel input retains UnprocessedInput. Native ink
  selection/move/cancel/undo, actual visual hit testing, and save/restore pass.
- Text selection accepts mouse/cursor drag, touch hold-to-select, and touch
  handle drags. Pen input is rejected by text and handle paths; pen hover in
  the document restores the last drawing tool and opens the annotation card.
  The text tool closes the card/flyouts. Touch inking is removed. Pen hover,
  actual barrel-button routing, mouse drags and touchscreen feel still require
  device acceptance; the native tests do not inject hardware input.
- `Test-ZoomQuality.ps1 -PerformancePdf artifacts/native-tests/performance-600.pdf`
  passed. On this x64 Debug run, the small fixture at 400% retained 13,869,428
  raster bytes; eight short scrolls caused three additional render requests,
  with none after settling. The 600-page/40-image fixture in pen mode reported
  294,391,808 app bytes before zoom and 301,404,160 after settling at 400%.
  Returning to fit and navigating a cold page reported 372,740,096 app bytes,
  16,923,096 cached raster bytes and 22 native intermediate animation events.
  These are fixture/device observations, not a total-process memory cap or
  a physical frame-rate/CPU benchmark. PDF internals, XAML, ink and text have
  allocations beyond the raster budget.
- Production x64 Debug builds/layout restoration passed; test launch/compile
  hooks are absent from production source. Production zoom, raster quality,
  memory/scheduler, render-loop and search regressions pass; localization
  validation retains 200 matching resources in each of the four catalogs.


## 2026-10-15 — freehand ink lasso

- Replaced rectangle construction with the actual freehand Polyline for both
  mouse/cursor selection and pen barrel input. Both event routes consume
  intermediate points in timestamp order. The temporary dashed line stays
  screen-sized through zoom; release closes the path and passes it directly
  to InkStrokeContainer.SelectWithPolyLine. Cancel removes the temporary path.
- Native QA covers a concave loop excluding a stroke inside its bounding box
  but outside its shape, an open path closing at release, circular selection,
  cancel, move, undo, save and editable restore. Physical mouse/pen input still
  requires device acceptance; the test calls production methods and native
  containers without injecting desktop input.
- The four localized tool labels and help now describe lasso selection.


## 2026-10-15 — Surface diagnostics, quiet cancellation and cross-page text

- Located the latest nonempty VS2015 report in the local Temp folder:
  `Report.4A71B345-7A6E-4111-A5F6-2EB42667F826.diagsession`, saved at 16:42.
  Its PID 6272 matches the supplied Surface screenshot. Windows tracerpt
  decoded 6,639 ETL events without loss; raw data and a Chinese analysis are
  preserved under `artifacts/surface-session`. The provider manifest is absent
  locally: mapping metric 1 to private bytes, 2 to CPU percentage and events
  7/8 to GC markers is an explicit interpretation of payloads and the VS graph.
  Values show a 376.7 MiB peak and 188.6 MiB final sample, CPU-like samples
  peaking at 95.83 and 133 paired GC-like intervals. This summary trace has no
  allocation stacks, heap snapshots or CPU call stacks and cannot prove a leak.
- Normal managed search/glyph/OCR cancellation returns a null result instead
  of raising OperationCanceledException. Search checks it before publishing;
  scroll cancellation and pending page-geometry work also exit quietly.
  Production worker regression captures first-chance exceptions and verifies
  no cancellation exceptions during bounded glyph expansion/search mapping.
  Disposal and actual extraction failures retain their separate handling.
- Selection uses page/glyph endpoints, document-owned capture and a 30 ms
  coalescing timer. Autoscroll is elapsed-time based, accelerates with edge
  distance/dwell, and continues while endpoint text is loading. Endpoint
  glyphs survive presenter virtualization; intermediate pages load on demand
  for copy/highlight and are released afterwards. Revision checks reject
  asynchronous copy/highlight results after selection/file changes.
- Native x64 tests passed forward/reverse four-page copy, endpoint
  virtualization, edge scrolling across pages, and two-page highlight
  save/reload with editable sidecars. After warmup, 500 updates reuse the same
  Polygon/PointCollection/handle graph and observed zero Gen0 collections in
  that loop. This does not claim post-change Surface hardware/GC measurements.
- App/MainPage XAML and dependent code items now belong to each real project.
  The main project's AnyCPU design-time native reference maps to x86 while
  real x64 remains x64. MSBuild 14 design probes pass for both, and production
  x64 Debug builds retain only the existing Share deprecation warnings.

### Pen lasso and affine editing (2026-10-15)

- Fixed the overlay's mouse-left-button filter excluding pen tip/barrel contacts.
  Explicit lasso now has one XAML capture owner, with the underlying InkPresenter
  hit testing/input disabled for that tool. Drawing tools retain the native
  LeaveUnprocessed barrel route; hover/eraser/touch never start a lasso.
- Followed Microsoft's UWP ScrollViewer pen-interaction guidance: disable System
  manipulation on the content and selection layer during the gesture, then
  restore the previous content mode on release, cancel, lost capture, tool change
  or save. Touch document panning remains available outside selection gestures.
  Reference: https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.controls.scrollviewer#pen-interaction
- Added an oriented blue outline with a white halo, pale selection fill, eight
  28-DIP hit targets with 10-DIP visible grips, and a separate 24-DIP rotation
  grip using Microsoft's MDL2 Rotate glyph. Grip sizes remain stable at document
  zoom. The minimum frame leaves room to move thin strokes between the grips.
- Corners scale proportionally, edges stretch, and the rotation grip rotates the
  group around its center. Shift snaps rotation to 15-degree increments. Move,
  resize and rotate compose from the gesture's original native PointTransforms;
  positive bounded scales prevent collapsing/flipping the frame. One gesture is
  one undo action; a tap adds none. Esc cancels a live transform and Ctrl+Z undoes
  a selected transform. All four UI languages include the handle names/help.
- Native in-process x64 QA exercises group lasso, eight resize/stretch directions,
  shrink, 90-degree rotation, move/scale after rotation, positive-scale limits,
  unchanged outside ink, exact cancellation/undo and fixed grip sizes at
  64/100/400%. Native hit testing reaches the rotation grip. Save and snapshot
  restore preserve the composed stroke transform; PDF rendering checks its ink
  footprint against the transformed native bounds. Existing OCR, cross-page
  selection and save-recovery regressions also pass. Production x64 Debug builds
  retain the two pre-existing Share deprecation warnings.
- These are in-process application/native API tests, without desktop automation
  or synthetic hardware events. Actual Surface pen tip/barrel capture and touch
  panning still require device acceptance. RenderTargetBitmap previews inspect
  the XAML selection chrome; native InkCanvas ink is checked through PDF rendering.
