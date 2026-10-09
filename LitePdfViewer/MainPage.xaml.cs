using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Ellipse = Windows.UI.Xaml.Shapes.Ellipse;

namespace LitePdfViewer
{
    public sealed partial class MainPage : Page
    {
        private const int RenderWindow = 2;
        private const int EvictWindow = 3;
        private const long BitmapCacheBudget = PdfMemoryBudget.MaximumBytes;
        // Only pages this close to the current one get zoom-scaled resolution; the
        // rest of the prefetch window stays at fit-width so zooming never multiplies
        // memory across the whole window.
        private const int HighResWindow = 1;
        private const double MaxRenderWidth = 2560;
        // Up to 16 MB of BGRA pixels plus a XAML surface, within the shared budget.
        private const double MaxRenderPixels = 4000000;
        private static readonly TimeSpan ZoomSettleDelay = TimeSpan.FromMilliseconds(300);
        private const double ThumbWidth = 80.0;
        // Pages measured per background chunk before yielding to the UI thread.
        private const int AspectProbeChunk = 8;

        private static readonly string[] WindowsInkPalette = new string[]
        {
            // Row 1: Grayscale / Monochromes
            "#000000", "#767676", "#A6A6A6", "#CCCCCC", "#E1E1E1", "#FFFFFF",
            // Row 2: Reds & Oranges
            "#881798", "#E81123", "#EA4300", "#FF8C00", "#FFB900", "#FFF100",
            // Row 3: Greens & Teals & Blues
            "#107C10", "#008272", "#00B7C3", "#0078D7", "#004E8C", "#002050",
            // Row 4: Purples & Pinks
            "#4C1A57", "#744DA9", "#B146C2", "#E3008C", "#EA005E", "#C239B3",
            // Row 5: Earth & Pastels
            "#8E562E", "#C19C00", "#9A9A9A", "#8CBD18", "#00CC6A", "#68CCCA"
        };

        // Page chrome draws a 1px hairline on every edge, so a page occupies
        // LayoutHeight + 2 + gap in the stack. Kept as a constant so the cached
        // offset table and the visual tree can never drift apart.
        private const double PageBorderThickness = 1;

        // Brushes are immutable here, so one instance can back every page and
        // thumbnail instead of allocating a fresh brush per element per update.
        private static readonly SolidColorBrush PageFillBrush = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush PageBorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        private static readonly SolidColorBrush ThumbIdleBorderBrush = new SolidColorBrush(Color.FromArgb(32, 0, 0, 0));
        private static readonly SolidColorBrush ThumbSelectedBorderBrush = new SolidColorBrush(Color.FromArgb(255, 10, 122, 255));
        private static readonly SolidColorBrush ThumbIdleTextBrush = new SolidColorBrush(Color.FromArgb(255, 134, 134, 139));
        private static readonly SolidColorBrush ThumbSelectedTextBrush = new SolidColorBrush(Color.FromArgb(255, 10, 122, 255));

        private readonly SemaphoreSlim renderGate = new SemaphoreSlim(1, 1);
        private readonly PdfRasterBackend rasterBackend = new PdfRasterBackend();
        private readonly PdfRasterRetirement retiredRasters = new PdfRasterRetirement(BitmapCacheBudget / 2, 4);
        private bool retirementRenderingSubscribed;
        private Func<PageView, bool> needsVisibleRefinement;
        private readonly PdfScrollActivity scrollActivity = new PdfScrollActivity();
        private readonly Thickness pageGap = new Thickness(0, 0, 0, 10);
        private readonly List<PageView> pageViews = new List<PageView>();
        private readonly Dictionary<int, ThumbnailItem> thumbnailItems = new Dictionary<int, ThumbnailItem>();
        private readonly List<SwatchItem> penSwatches = new List<SwatchItem>();
        private readonly List<SwatchItem> highlighterSwatches = new List<SwatchItem>();

        // Indices whose Image/InkCanvas subtree currently exists. Page turns scan
        // this instead of the whole document.
        private readonly List<int> realizedPages = new List<int>();
        private readonly HashSet<int> cachedPages = new HashSet<int>();

        // Cumulative page tops in unzoomed content space. Lets page hit-testing and
        // scroll targeting be pure arithmetic instead of visual-tree transforms.
        private double[] pageTops = new double[0];
        private int aspectProbedCount;

        private int dirtyCount;
        private bool lastDirtyShown;
        private int lastZoomPercent = -1;
        private int selectedThumbIndex = -1;
        private StorageFolder annotationFolder;
        private bool palettesBuilt;
        private DispatcherTimer relayoutTimer;

        private sealed class SwatchItem
        {
            public Button Button;
            public Ellipse Ring;
            public Color Color;
        }

        private PdfDocument document;
        private StorageFile currentFile;
        private uint pageIndex;
        private ulong activeRenderToken;
        private double dpiScale = 1;
        private double lastColumnWidth;

        private InkTool currentInkTool = InkTool.None;
        private Color penColor = Color.FromArgb(255, 232, 17, 35); // #E81123
        private double penStrokeSize = 3;
        private Color highlighterColor = Color.FromArgb(255, 255, 241, 0); // #FFF100
        private double highlighterStrokeSize = 12;
        private bool touchInkingEnabled;
        private bool isUpdatingZoomSlider;
        private DateTime zoomSettledAt = DateTime.MinValue;
        private float lastObservedZoom = 1f;
        private float? pendingSliderZoom;
        private DispatcherTimer sliderZoomTimer;
        private bool thumbsRequested;
        private Storyboard sidebarStoryboard;
        private bool isPromptingSave;
        private long cachedBitmapBytes;
        private DateTime thumbnailSettledAt = DateTime.MinValue;
        private TaskCompletionSource<bool> renderWake = new TaskCompletionSource<bool>();
        private bool textWorkerBusy;
        private bool renderWakeQueued;

        public MainPage()
        {
            InitializeComponent();
            rasterBackend.DeviceLost += ClearLostRenderSurfaces;
            Window.Current.VisibilityChanged += (sender, args) =>
            {
                if (!args.Visible) ReleaseRetiredRenderSurfaces();
                QueueMemoryUpdate();
                WakeRenderer();
            };
            Loaded += (sender, args) => StartMemoryMonitoring();
            Unloaded += (sender, args) => { StopMemoryMonitoring(); ReleaseRetiredRenderSurfaces(); };
            InitializePreviewWindow();
            try
            {
                dpiScale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            }
            catch (Exception)
            {
                dpiScale = 1;
            }

            // Catch shortcuts even when a HUD button owns keyboard focus.
            AddHandler(KeyDownEvent, new KeyEventHandler(Page_KeyDown), true);

            // The two palettes are 60 buttons and 120 ellipses. Building them in the
            // constructor put all of that on the cold-launch path for a flyout most
            // sessions never open, so they are built the first time one is shown.
            UpdatePenPreview();
            UpdateHighlighterPreview();

            ThumbnailPane.SizeChanged += (s, e) =>
            {
                ThumbnailPane.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
                };
            };

            RegisterCloseRequestedHandler();

            SetInkTool(InkTool.None);
            UpdateUi();
        }

        public async void OpenActivatedFile(StorageFile file)
        {
            await LoadDocumentAsync(file);
        }

        public async void OpenLaunchArguments(string arguments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(arguments))
                {
                    return;
                }

                StorageFile file = null;
                if (string.Equals(arguments, "sample", StringComparison.OrdinalIgnoreCase))
                {
                    file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///TestAssets/SkimSample.pdf"));
                }
                else if (arguments.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
                {
                    file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(arguments));
                }
                else if (arguments.StartsWith("litepdfviewer://", StringComparison.OrdinalIgnoreCase) ||
                         arguments.StartsWith("litepdfviewerpreview://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(arguments);
                    if (string.Equals(uri.Host, "sample", StringComparison.OrdinalIgnoreCase) ||
                        uri.AbsolutePath.IndexOf("sample", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///TestAssets/SkimSample.pdf"));
                    }
                }
                else if (Path.IsPathRooted(arguments))
                {
                    file = await StorageFile.GetFileFromPathAsync(arguments);
                }

                if (file != null)
                {
                    await LoadDocumentAsync(file);
                }
            }
            catch (Exception ex)
            {
                FileNameText.Text = "Could not open PDF: " + ex.Message;
            }
        }

        // Persist all dirty annotations.
        public async Task SaveCurrentAnnotationsAsync()
        {
            if (currentFile == null || document == null)
            {
                return;
            }

            var token = activeRenderToken;
            var pending = new List<PageView>();
            foreach (var view in pageViews) if (view.Dirty) pending.Add(view);
            foreach (var view in pending)
            {
                // An export/save may overlap opening another file. Never walk
                // the new document's page list after an asynchronous disk write.
                if (token != activeRenderToken) return;
                await SavePageAnnotationsAsync(view);
            }

            UpdateDirtyState();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenViaPickerAsync();
        }

        private async Task OpenViaPickerAsync()
        {
            if (HasUnsavedChanges())
            {
                var promptResult = await PromptSaveUnsavedChangesAsync();
                if (promptResult == SavePromptResult.Cancel)
                {
                    return;
                }
                else if (promptResult == SavePromptResult.Save)
                {
                    await SaveCurrentAnnotationsAsync();
                }
                else
                {
                    // User chose "Don't Save": discard pending unsaved changes
                    for (var i = 0; i < pageViews.Count; i++)
                    {
                        pageViews[i].Dirty = false;
                    }
                    dirtyCount = 0;
                    UpdateDirtyState();
                }
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".pdf");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadDocumentAsync(file);
            }
        }

        private async Task LoadDocumentAsync(StorageFile file)
        {
            ResetSearch();
            var renderToken = ++activeRenderToken;
            document = null;
            ResetPageViews();
            ResetThumbnails();

            currentFile = file;
            annotationFolder = null;
            FileNameText.Text = file.Name;
            EmptyState.Visibility = Visibility.Collapsed;

            try
            {
                var loadedDocument = await PdfDocument.LoadFromFileAsync(file);
                if (renderToken != activeRenderToken) return;
                document = loadedDocument;
                pageIndex = 0;
                var hasIntrinsicSize = false;
                if (document.PageCount > 0)
                {
                    using (var firstPage = document.GetPage(0))
                    {
                        // PdfPage.Size already applies CropBox/MediaBox and Rotation,
                        // but Windows reports it at 96 DPI. Recover PDF points (72 DPI).
                        var size = firstPage.Size;
                        hasIntrinsicSize = size.Width > 0 && size.Height > 0;
                        firstPageSize = hasIntrinsicSize ? new Size(size.Width * 72 / 96, size.Height * 72 / 96) : new Size(600, 800);
                    }
                }
                thumbsRequested = thumbnailPreference ?? document.PageCount > 1;
                ThumbsToggle.IsChecked = thumbsRequested;
                CreatePagePlaceholders(document);
                textSource = new PdfTextSource(file);
                // No forced UpdateLayout() here: page positions come from the cached
                // offset table, so nothing downstream needs a synchronous layout pass
                // over every page before the first paint.
                SetThumbsVisible(thumbsRequested, false);
                // Set the initial reading position before any asynchronous work
                // gives the user a chance to scroll or select a different page.
                FitToWindow(false);
                ScheduleRelayout();
                Focus(FocusState.Programmatic);
                UpdateUi();

                // Work-area probing and automatic window sizing must not hold
                // the first page or the visible-page render loop behind them.
                var previewSizing = ApplyPreviewWindowSizeAsync(renderToken, hasIntrinsicSize);

                // First page paints immediately at viewport resolution; the rest streams in.
                if (pageViews.Count > 0)
                {
                    await RenderPageCoreAsync(0, renderToken);
                }

                if (renderToken != activeRenderToken) return;
                ScheduleRelayout();
                UpdateUi();

                var ignoredLoop = RunRenderLoopAsync(renderToken);
                var ignoredProbe = ProbeAspectsAsync(renderToken);
                await previewSizing;
                if (renderToken != activeRenderToken) return;
            }
            catch (Exception ex)
            {
                if (renderToken != activeRenderToken) return;
                ++activeRenderToken;
                document = null;
                ResetPageViews();
                ResetThumbnails();
                EmptyState.Visibility = Visibility.Visible;
                FileNameText.Text = "Could not open PDF: " + ex.Message;
            }

            UpdateUi();
        }

        private void ThumbsToggle_Click(object sender, RoutedEventArgs e)
        {
            // Click fires before IsChecked flips, so the toggle's own state lags by one
            // press; track the request here and keep the visual in sync explicitly.
            thumbsRequested = !thumbsRequested;
            thumbnailPreference = thumbsRequested;
            ThumbsToggle.IsChecked = thumbsRequested;
            UpdateThumbnailWindow();
            AnimateThumbnailPane(thumbsRequested);
        }

        private void SetThumbsVisible(bool requested)
        {
            SetThumbsVisible(requested, false);
        }

        private void SetThumbsVisible(bool requested, bool animated)
        {
            var show = requested && document != null && pageViews.Count > 0;
            if (animated)
            {
                AnimateThumbnailPane(show);
            }
            else
            {
                if (sidebarStoryboard != null)
                {
                    sidebarStoryboard.Stop();
                }
                ThumbnailPane.Width = show ? PreviewSizing.ThumbnailRail : 0;
                ThumbnailPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                UpdateThumbnailWindow();
                UpdatePageCentering();
            }
        }

        private void AnimateThumbnailPane(bool open)
        {
            var canShow = open && document != null && pageViews.Count > 0;
            if (sidebarStoryboard != null)
            {
                sidebarStoryboard.Stop();
            }

            if (canShow)
            {
                ThumbnailPane.Visibility = Visibility.Visible;
            }

            var currentWidth = double.IsNaN(ThumbnailPane.Width) ? ThumbnailPane.ActualWidth : ThumbnailPane.Width;
            if (currentWidth < 0)
            {
                currentWidth = 0;
            }
            var targetWidth = canShow ? PreviewSizing.ThumbnailRail : 0.0;

            sidebarStoryboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = currentWidth,
                To = targetWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(animation, ThumbnailPane);
            Storyboard.SetTargetProperty(animation, "Width");
            sidebarStoryboard.Children.Add(animation);

            sidebarStoryboard.Completed += (s, e) =>
            {
                ThumbnailPane.Width = targetWidth;
                if (!canShow)
                {
                    ThumbnailPane.Visibility = Visibility.Collapsed;
                }
                UpdatePageCentering();
                ScheduleRelayout();
                if (DocumentScroller != null)
                {
                    UpdateHudBarResponsive(DocumentScroller.ActualWidth);
                }
            };

            sidebarStoryboard.Begin();
        }

        // ============================== Page layout ==============================

        private double ComputeColumnWidth()
        {
            // Keep page/ink geometry stable across window resizes. The ScrollViewer
            // fits that natural point size rather than rebuilding at viewport width.
            return Math.Max(1, firstPageSize.Width);
        }

        private void UpdatePageCentering()
        {
            if (DocumentScroller == null || PageFrame == null || pageViews.Count == 0)
            {
                return;
            }

            var viewportWidth = DocumentScroller.ViewportWidth;
            if (viewportWidth < 1)
            {
                viewportWidth = DocumentScroller.ActualWidth;
            }
            if (viewportWidth < 1)
            {
                return;
            }

            var zoom = DocumentScroller.ZoomFactor;
            if (zoom <= 0.01f)
            {
                zoom = 1.0f;
            }

            var requiredFrameWidth = Math.Max(lastColumnWidth + 2 * PageBorderThickness, viewportWidth / zoom);
            if (Math.Abs(PageFrame.Width - requiredFrameWidth) > 0.5)
            {
                PageFrame.Width = requiredFrameWidth;
            }

            if (lastColumnWidth * zoom <= viewportWidth && DocumentScroller.HorizontalOffset > 0.5f)
            {
                DocumentScroller.ChangeView(0f, null, null, true);
            }
        }

        private void CreatePagePlaceholders(PdfDocument pdfDocument)
        {
            ResetPageViews();

            var columnWidth = ComputeColumnWidth();
            lastColumnWidth = columnWidth;

            // Only the first page is measured up front. This used to call GetPage() on
            // every page before the first paint, which is a synchronous page-tree
            // parse per page and scaled straight with document length. Virtually
            // every PDF is uniform, so page 0's aspect is already the right answer
            // for the whole file, and ProbeAspectsAsync confirms it (or corrects the
            // odd page) in the background without blocking the load.
            var aspect = ProbePageAspect(pdfDocument, 0, 1.4142);

            for (uint i = 0; i < pdfDocument.PageCount; i++)
            {
                var view = CreatePageView(i, columnWidth, columnWidth * aspect);
                view.AspectKnown = i == 0;
                pageViews.Add(view);
            }

            aspectProbedCount = pageViews.Count > 0 ? 1 : 0;
            RebuildPageOffsets();
            UpdateThumbnailWindow();
            UpdatePageCentering();
        }

        private static double ProbePageAspect(PdfDocument pdfDocument, uint index, double fallback)
        {
            try
            {
                if (index >= pdfDocument.PageCount)
                {
                    return fallback;
                }

                using (var page = pdfDocument.GetPage(index))
                {
                    var size = page.Size;
                    if (size.Width > 0 && size.Height > 0)
                    {
                        return size.Height / size.Width;
                    }
                }
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        // Measures the remaining pages in chunks, yielding between them so the UI
        // thread is never held. A uniform document confirms the assumed aspect and
        // nothing moves at all; a mixed one corrects itself, and the reading position
        // is re-anchored whenever a correction lands above it.
        private async Task ProbeAspectsAsync(ulong token)
        {
            try
            {
                while (token == activeRenderToken && document != null && aspectProbedCount < pageViews.Count)
                {
                    if (!IsScrollSettled() || !IsZoomSettled()) { await Task.Delay(120); continue; }
                    var start = aspectProbedCount;
                    var end = Math.Min(pageViews.Count, start + AspectProbeChunk);
                    var firstChanged = int.MaxValue;

                    // Shares the render gate: PdfDocument is not documented as safe
                    // for a GetPage here while a RenderToStreamAsync runs elsewhere.
                    await renderGate.WaitAsync();
                    try
                    {
                        if (token != activeRenderToken || document == null)
                        {
                            return;
                        }

                        // Input may have restarted while this probe waited on a
                        // raster draw. Yield again instead of measuring mid-fling.
                        if (!IsScrollSettled() || !IsZoomSettled()) continue;

                        var slice = System.Diagnostics.Stopwatch.StartNew();
                        for (var i = start; i < end; i++)
                        {
                            var view = pageViews[i];
                            var aspect = view.AspectKnown ? view.Aspect : ProbePageAspect(document, view.Index, double.NaN);
                            if (ConfirmPageAspect(view, aspect))
                            {
                                if (i < firstChanged)
                                {
                                    firstChanged = i;
                                }
                            }
                            if (slice.ElapsedMilliseconds >= 4) { end = i + 1; break; }
                        }

                        aspectProbedCount = end;
                    }
                    finally
                    {
                        renderGate.Release();
                    }

                    if (firstChanged != int.MaxValue)
                    {
                        for (var i = firstChanged; i < end; i++)
                        {
                            UpdatePageSize(pageViews[i]);
                        }

                        RebuildPageOffsets();
                        WakeRenderer();
                    }

                    await Task.Delay(16);
                }
            }
            catch (Exception)
            {
                // Best-effort: an unmeasured page simply keeps the assumed aspect.
            }
        }

        private void RebuildLayout()
        {
            if (document == null || pageViews.Count == 0)
            {
                return;
            }

            var columnWidth = ComputeColumnWidth();
            if (Math.Abs(columnWidth - lastColumnWidth) < 1.5)
            {
                UpdatePageCentering();
                if (fitPageToViewport) RefitPreservingPosition();
                return;
            }

            lastColumnWidth = columnWidth;
            for (var i = 0; i < pageViews.Count; i++)
            {
                var view = pageViews[i];
                view.LayoutWidth = columnWidth;
                view.LayoutHeight = columnWidth * view.Aspect;
                // A new layout size is a natural retry point for a page that failed
                // to render at the previous one.
                view.RenderFailed = false;
                view.DetailFailed = false;
                UpdatePageSize(view);
            }

            RebuildPageOffsets();
            UpdatePageCentering();

            if (fitPageToViewport)
            {
                RefitPreservingPosition();
            }
        }

        // Unseen pages are metadata only. An absolute Canvas maintains the full
        // scroll extent; page controls exist only around the viewport. Strokes
        // outlive those controls, so eviction never discards unsaved annotations.
        private PageView CreatePageView(uint index, double layoutWidth, double layoutHeight)
        {
            return new PageView
            {
                Index = index,
                LayoutWidth = layoutWidth,
                LayoutHeight = layoutHeight,
                Aspect = layoutHeight / layoutWidth
            };
        }

        private void RealizePage(PageView view)
        {
            if (view.IsRealized)
            {
                return;
            }

            view.Chrome = new Border
            {
                Width = view.LayoutWidth + 2 * PageBorderThickness,
                Height = view.LayoutHeight + 2 * PageBorderThickness,
                Background = PageFillBrush, BorderBrush = PageBorderBrush,
                BorderThickness = new Thickness(PageBorderThickness)
            };
            AutomationProperties.SetName(view.Chrome, "PDF page " + (view.Index + 1).ToString(CultureInfo.InvariantCulture));
            Canvas.SetTop(view.Chrome, pageTops[(int)view.Index]);
            PageStack.Children.Add(view.Chrome);

            var image = new Image
            {
                Width = view.LayoutWidth,
                Height = view.LayoutHeight,
                Stretch = Stretch.Fill
            };
            AutomationProperties.SetName(image, "Rendered PDF page " + (view.Index + 1).ToString(CultureInfo.InvariantCulture));

            var inkLayer = new InkCanvas
            {
                Width = view.LayoutWidth,
                Height = view.LayoutHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Reattach the surviving stroke container before wiring events so the
            // restore itself cannot be mistaken for a user edit.
            inkLayer.InkPresenter.StrokeContainer = view.EnsureStrokes();
            ConfigureInkCanvas(inkLayer, CreateInkAttributes(), view.InkLoaded && !view.IsLoadingInk);

            inkLayer.InkPresenter.StrokesCollected += (sender, args) =>
            {
                if (args.Strokes.Count > 0)
                {
                    for (var i = 0; i < args.Strokes.Count; i++) view.UndoKinds.Add(false);
                    MarkPageDirty(view);
                }
            };

            inkLayer.InkPresenter.StrokesErased += (sender, args) =>
            {
                if (args.Strokes.Count > 0)
                {
                    MarkPageDirty(view);
                }
            };

            var host = new Grid
            {
                Width = view.LayoutWidth,
                Height = view.LayoutHeight,
                Background = PageFillBrush
            };
            host.Children.Add(image);
            host.Children.Add(inkLayer);

            view.Host = host;
            view.Image = image;
            view.InkLayer = inkLayer;
            view.Chrome.Child = host;
            AttachTextLayers(view);
            AttachDetailLayer(view);

            if (view.Source != null)
            {
                image.Source = view.Source;
            }

            realizedPages.Add((int)view.Index);
        }

        private void VirtualizePage(PageView view)
        {
            if (!view.IsRealized)
            {
                return;
            }

            // Control virtualization must not discard a useful bitmap. Retain it
            // under the cache budget so reversing a scroll reuses the surface.
            view.Image.Source = null;
            ReleasePageDetail(view);
            view.DetailImage = null;
            view.DetailLayer = null;
            if (selectedTextPage == view) ClearTextSelection();
            view.Glyphs = null;
            view.HighlightLayer = null;
            view.SelectionLayer = null;

            // Detach the container so the discarded presenter keeps no claim on the
            // strokes we are about to carry forward.
            view.InkLayer.InkPresenter.StrokeContainer = new InkStrokeContainer();
            view.Chrome.Child = null;
            PageStack.Children.Remove(view.Chrome);
            view.Chrome = null;
            view.Host = null;
            view.Image = null;
            view.InkLayer = null;

            realizedPages.Remove((int)view.Index);
            ReleaseCleanAnnotations(view);
            if (memoryConstrained) ReleasePageBitmap(view);
        }

        private static void ReleaseCleanAnnotations(PageView view)
        {
            if (view.IsRealized || view.Dirty || view.IsLoadingInk) return;
            // Saved data can be restored from the sidecar. Dirty pages keep their
            // containers so scrolling never discards an unsaved stroke/highlight.
            if (view.Strokes != null) view.Strokes.Clear();
            view.Strokes = null;
            view.Highlights.Clear();
            view.InkLoaded = false;
            view.HighlightsLoaded = false;
        }

        private void MarkPageDirty(PageView view)
        {
            view.Revision++;
            if (view.Dirty)
            {
                return;
            }

            view.Dirty = true;
            dirtyCount++;
            UpdateDirtyState();
        }

        private void ClearPageDirty(PageView view)
        {
            if (!view.Dirty)
            {
                return;
            }

            view.Dirty = false;
            dirtyCount--;
            ReleaseCleanAnnotations(view);
        }

        private void UpdatePageSize(PageView view)
        {
            if (!view.IsRealized) return;
            view.Chrome.Width = view.LayoutWidth + 2 * PageBorderThickness;
            view.Chrome.Height = view.LayoutHeight + 2 * PageBorderThickness;

            if (!view.IsRealized)
            {
                return;
            }

            view.Host.Width = view.LayoutWidth;
            view.Host.Height = view.LayoutHeight;
            view.Image.Width = view.LayoutWidth;
            view.Image.Height = view.LayoutHeight;
            view.InkLayer.Width = view.LayoutWidth;
            view.InkLayer.Height = view.LayoutHeight;
            RedrawTextLayers(view);
            UpdateDetailLayer(view);
        }

        private void ResetPageViews()
        {
            ReleaseRetiredRenderSurfaces();
            pendingLayoutAnchor = null;
            fitReferencePage = 0;
            ClearTextSelection();
            if (textSource != null) { textSource.Dispose(); textSource = null; }
            for (var i = realizedPages.Count - 1; i >= 0; i--) VirtualizePage(pageViews[realizedPages[i]]);
            foreach (var view in pageViews)
            {
                ReleasePageBitmap(view);
                view.Strokes = null;
            }

            pageViews.Clear();
            realizedPages.Clear();
            cachedPages.Clear();
            PageStack.Children.Clear();
            pageTops = new double[0];
            aspectProbedCount = 0;
            dirtyCount = 0;
            lastDirtyShown = false;
            lastZoomPercent = -1;
            cachedBitmapBytes = 0;
            textWorkerBusy = false;
            scrollActivity.Reset();
            WakeRenderer();
        }

        // Page tops in unzoomed content space. Rebuilt whenever a layout width
        // changes; everything that needs a page position reads this table instead of
        // asking the visual tree for a transform.
        private void RebuildPageOffsets()
        {
            if (pageTops.Length != pageViews.Count)
            {
                pageTops = new double[pageViews.Count];
            }

            var top = 0.0;
            for (var i = 0; i < pageViews.Count; i++)
            {
                pageTops[i] = top;
                top += pageViews[i].LayoutHeight + 2 * PageBorderThickness + pageGap.Bottom;
            }
            PageStack.Width = lastColumnWidth + 2 * PageBorderThickness;
            PageStack.Height = Math.Max(0, top - pageGap.Bottom);
            foreach (var index in realizedPages) Canvas.SetTop(pageViews[index].Chrome, pageTops[index]);
        }

        // ============================== Render scheduler ==============================
        // One background worker: pages nearest the viewport first, thumbnails fill the
        // gaps, far pages are evicted so a long document never accumulates bitmaps.

        private async Task RunRenderLoopAsync(ulong token)
        {
            while (token == activeRenderToken)
            {
                if (document == null)
                {
                    return;
                }
                if (!Window.Current.Visible)
                {
                    await WaitForRenderWorkAsync();
                    continue;
                }

                // Failures are isolated per item. This catch used to sit outside the
                // loop, so a single unrenderable page ended all further page and
                // thumbnail work for the document.
                try
                {
                    var next = FindNextRenderIndex();
                    if (next >= 0)
                    {
                        try
                        {
                            await RenderPageCoreAsync((uint)next, token);
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception)
                        {
                            // Stop retrying this page; RebuildLayout clears the flag
                            // so a resize or zoom gives it another chance.
                            if (token == activeRenderToken && next < pageViews.Count)
                            {
                                pageViews[next].RenderFailed = true;
                            }
                        }

                        continue;
                    }

                    var detail = FindNextDetailPage();
                    if (detail != null)
                    {
                        try { await RenderPageDetailAsync(detail, token); }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            if (token == activeRenderToken) detail.DetailFailed = true;
                            System.Diagnostics.Debug.WriteLine("PDF detail failed: " + ex.Message);
                        }
                        continue;
                    }

                    // Thumbnails are only worth rendering once the pane has actually
                    // been asked for. Rendering every page of the document into a
                    // bitmap for a sidebar most sessions never open was the largest
                    // block of pure waste in the background loop.
                    // Match the execution guard. Selecting a thumbnail while
                    // zooming only to reject it synchronously would spin this
                    // UI-thread loop and prevent the gesture from completing.
                    var inputSettled = IsScrollSettled() && IsZoomSettled();
                    var thumb = inputSettled ? FindNextThumbnailIndex() : -1;
                    if (thumb >= 0)
                    {
                        await RenderThumbnailCoreAsync((uint)thumb, token);
                        continue;
                    }

                    // Text is useful only for pages actually visible. Extract one
                    // at a time, after raster work, so flicks do not queue glyphs.
                    var textPage = inputSettled ? FindNextTextPage() : null;
                    if (textPage != null)
                    {
                        var ignoredText = RunTextWorkAsync(textPage, token);
                    }
                    if (!inputSettled || DateTime.UtcNow < thumbnailSettledAt)
                        await WaitForRenderWorkAsync(120);
                    else
                        await WaitForRenderWorkAsync();
                }
                catch (Exception)
                {
                    // Keep the loop alive; the next pass re-evaluates what needs work.
                    await WaitForRenderWorkAsync(150);
                }
            }
        }

        private int FindNextRenderIndex()
        {
            if (pageViews.Count == 0)
            {
                return -1;
            }

            // While the zoom is still moving (slider drag, pinch, animation) only fill
            // blank pages; resolution upgrades wait until the zoom settles, otherwise
            // every intermediate zoom step would render a fresh set of huge bitmaps.
            var zoomSettled = IsZoomSettled() && IsScrollSettled();
            int first, last;
            GetVisiblePageRange(out first, out last);
            // Reuse the delegate; scrolling must not allocate one per wake.
            if (needsVisibleRefinement == null) needsVisibleRefinement = VisiblePageNeedsRefinement;
            var visible = PdfRenderScheduler.FindVisible(pageViews, first, last,
                zoomSettled, needsVisibleRefinement);
            if (visible >= 0) return visible;
            if (memoryConstrained) return -1;
            // Once visible pages are filled, prepare only the adjacent page in
            // the movement direction. Waiting for a complete fling to stop
            // makes every newly exposed image page pay its full cold render.
            if (!IsScrollSettled())
                return PdfRenderScheduler.FindAheadMissing(pageViews, first, last, scrollActivity.Direction);
            for (var offset = 1; offset <= RenderWindow; offset++)
            {
                var before = first - offset;
                if (before >= 0 && PageNeedsWork(pageViews[before], zoomSettled))
                {
                    return before;
                }

                var after = last + offset;
                if (after < pageViews.Count && PageNeedsWork(pageViews[after], zoomSettled))
                {
                    return after;
                }
            }

            return -1;
        }

        private bool PageNeedsWork(PageView view, bool zoomSettled)
        {
            // A page that threw is skipped rather than retried forever, which would
            // otherwise spin the loop and starve every other page.
            if (view.RenderFailed)
            {
                return false;
            }

            if (!view.IsRendered)
            {
                return true;
            }

            return zoomSettled && PageResolutionNeedsWork(view, ComputeRenderTargetWidth(view));
        }

        private bool VisiblePageNeedsRefinement(PageView view)
        {
            return PageResolutionNeedsWork(view, ComputeRenderTargetWidth(view));
        }

        private bool PageResolutionNeedsWork(PageView view, double target)
        {
            if (view.NeedsRender(target)) return true;
            // Keep a sharper cached surface when a page leaves the viewport. Only
            // shrink it if protected visible surfaces exceed the total budget.
            return cachedBitmapBytes > activeBitmapCacheBudget &&
                view.RenderedWidth - target > Math.Max(96, target * 0.3);
        }

        private bool IsZoomSettled()
        {
            return DateTime.UtcNow >= zoomSettledAt && pendingSliderZoom == null;
        }

        private double ComputeRenderTargetWidth(PageView view)
        {
            // 1:1 device pixels at the current zoom; re-renders automatically when the
            // user zooms in beyond the cached resolution.
            var zoom = Math.Min(1.0, DocumentScroller.ZoomFactor);
            if (Math.Abs((int)view.Index - (int)pageIndex) <= HighResWindow)
            {
                zoom = DocumentScroller.ZoomFactor;
            }

            var target = view.LayoutWidth * dpiScale * zoom;
            var aspect = view.Aspect > 0 ? view.Aspect : 1.4142;
            var pixelBudget = RenderPixelBudget(view);
            var pixelCap = Math.Sqrt(pixelBudget / aspect);
            // A cheap first surface keeps a newly exposed page readable while
            // scrolling; the existing surface remains until its upgrade arrives.
            if ((!IsScrollSettled() || !IsZoomSettled()) && !view.IsRendered) target = Math.Min(768, target);
            return Math.Max(1, Math.Min(Math.Min(MaxRenderWidth, pixelCap), Math.Max(120, target)));
        }

        private async Task RenderPageCoreAsync(uint index, ulong token)
        {
            if (document == null || index >= (uint)pageViews.Count)
            {
                return;
            }

            PageView view = null;
            PdfPageRaster pending = null;

            await renderGate.WaitAsync();
            try
            {
                if (token != activeRenderToken || document == null || index >= (uint)pageViews.Count)
                {
                    return;
                }

                if (!PageStillWanted(index, token)) return;
                view = pageViews[(int)index];
                if (memoryConstrained && !IsPageVisible(index)) return;
                // A refinement may have waited behind geometry work while input
                // restarted. Missing pages still render during the gesture.
                if (view.IsRendered && (!IsScrollSettled() || !IsZoomSettled())) return;
                var target = ComputeRenderTargetWidth(view);
                var budgetAtStart = activeBitmapCacheBudget;
                if (!PageResolutionNeedsWork(view, target))
                {
                    return;
                }

                using (var page = document.GetPage(view.Index))
                {
                    var size = page.Size;
                    if (ConfirmPageAspect(view, size.Width > 0 ? size.Height / size.Width : double.NaN))
                    {
                        RefreshPageGeometry(view);
                        target = ComputeRenderTargetWidth(view);
                    }
                    var options = BuildRenderOptions(page, view, target);
                    pending = await rasterBackend.RenderAsync(page, options.DestinationWidth,
                        options.DestinationHeight, new Rect());

                    if (!PageStillWanted(index, token))
                    {
                        return;
                    }
                    if (memoryConstrained && !IsPageVisible(index)) return;
                    // A limit can drop while the native draw is running. Discard
                    // its oversized result; keep the displayed image until a
                    // replacement using the new pixel budget is ready.
                    if (activeBitmapCacheBudget < budgetAtStart &&
                        pending.Bytes > RenderPixelBudget(view) * 8 * 1.1) return;
                    // Preserve the current surface if a gesture began while the
                    // worker was drawing its replacement. Do not swap refinements
                    // into an actively moving viewport.
                    if (view.IsRendered && (!IsScrollSettled() || !IsZoomSettled())) return;

                    // Only commit pages still near the viewport. Abandoned rasters
                    // are disposed; replaced visible ones get a bounded grace period.
                    int visibleFirst, visibleLast;
                    GetVisiblePageRange(out visibleFirst, out visibleLast);
                    if (index >= visibleFirst && index <= visibleLast)
                    {
                        RealizePage(view);
                        UpdatePageSize(view);
                    }
                    // Native pages retain only their GPU surface. The fallback
                    // retains CPU pixels as well and accounts for both allocations.
                    var bytes = pending.Bytes;
                    var previousRaster = view.Raster;
                    TrimBitmapCache(view, bytes + (view.IsRealized && previousRaster != null ? previousRaster.Bytes : 0));
                    // Attach the replacement without an intermediate null Source.
                    if (view.Image != null) view.Image.Source = pending.Source;
                    ReleasePageDetail(view);
                    cachedBitmapBytes -= view.BitmapBytes;
                    view.Raster = pending;
                    pending = null;
                    view.BitmapBytes = bytes;
                    cachedBitmapBytes += bytes;
                    cachedPages.Add((int)view.Index);
                    RetireRaster(previousRaster, view.IsRealized);

                    view.IsRendered = true;
                    view.RenderedWidth = options.DestinationWidth;
                }
            }
            finally
            {
                if (pending != null) pending.Dispose();
                renderGate.Release();
            }

            if (view != null && view.IsRealized && !view.InkLoaded && PageStillWanted(index, token))
            {
                // Disk reads must not hold up the next visible page's raster.
                // RestoreVisibleInkAsync isolates errors from page rendering.
                var ignored = RestoreVisibleInkAsync(view, token);
            }
        }

        private double RenderPixelBudget(PageView view)
        {
            int first, last;
            GetVisiblePageRange(out first, out last);
            // Spend most of the budget on visible detail; speculative pages should
            // not reduce the current page's resolution equally at high zoom.
            var visible = view.Index >= first && view.Index <= last;
            var share = visible ? 0.65 / Math.Max(1, last - first + 1) : 0.35 / (RenderWindow * 2);
            return Math.Min(MaxRenderPixels, activeBitmapCacheBudget / 8.0 * share);
        }

        private PdfPageRenderOptions BuildRenderOptions(PdfPage page, PageView view, double targetWidth)
        {
            var size = page.Size;
            var width = (uint)Math.Max(1, Math.Round(targetWidth));
            var height = (uint)Math.Max(1, Math.Round(size.Width > 0 ? targetWidth * size.Height / size.Width : targetWidth));
            var maxPixels = RenderPixelBudget(view);
            var pixels = (double)width * height;
            if (pixels > maxPixels)
            {
                var shrink = Math.Sqrt(maxPixels / pixels);
                width = (uint)Math.Max(1, Math.Floor(width * shrink));
                height = (uint)Math.Max(1, Math.Floor(height * shrink));
            }

            return new PdfPageRenderOptions
            {
                DestinationWidth = width,
                DestinationHeight = height,
                // BMP is a straight copy to encode/decode; PNG compression was pure
                // overhead for an in-memory hand-off.
                BitmapEncoderId = BitmapEncoder.BmpEncoderId
            };
        }

        private async Task RenderThumbnailCoreAsync(uint index, ulong token)
        {
            PdfPageRaster pending = null;
            await renderGate.WaitAsync();
            try
            {
                if (token != activeRenderToken || document == null || index >= document.PageCount ||
                    memoryConstrained || !thumbsRequested || !thumbnailItems.ContainsKey((int)index) ||
                    !IsScrollSettled() || !IsZoomSettled() || DateTime.UtcNow < thumbnailSettledAt)
                {
                    return;
                }

                using (var page = document.GetPage(index))
                {
                    var size = page.Size;
                    var scale = size.Width > 0 && size.Height > 0 ? Math.Min(ThumbWidth / size.Width, 112 / size.Height) : 1;
                    var width = (uint)Math.Max(1, Math.Round(size.Width * scale));
                    var height = (uint)Math.Max(1, Math.Min(112, Math.Round(size.Height * scale)));
                    pending = await rasterBackend.RenderAsync(page, width, height, new Rect());

                    if (memoryConstrained || token != activeRenderToken || !thumbsRequested || !thumbnailItems.ContainsKey((int)index))
                    {
                        return;
                    }

                    ThumbnailItem item;
                    if (token == activeRenderToken && thumbnailItems.TryGetValue((int)index, out item))
                    {
                        item.Image.Height = Math.Min(112, ThumbWidth * height / Math.Max(1.0, width));
                        var previousRaster = item.Raster;
                        item.Raster = pending;
                        item.Image.Source = pending.Source;
                        pending = null;
                        RetireRaster(previousRaster, true);
                        item.IsRendered = true;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                ThumbnailItem item;
                if (token == activeRenderToken && thumbnailItems.TryGetValue((int)index, out item)) item.RenderFailed = true;
            }
            finally
            {
                if (pending != null) pending.Dispose();
                renderGate.Release();
            }
        }

        // Walks only the realized pages instead of the whole document, so a page
        // turn in a 900-page file costs a handful of checks rather than 900.
        private void EvictFarPages()
        {
            int first, last;
            GetVisiblePageRange(out first, out last);
            var window = memoryConstrained ? 0 : EvictWindow;
            for (var i = realizedPages.Count - 1; i >= 0; i--)
            {
                var index = realizedPages[i];
                if (index >= first - window && index <= last + window)
                {
                    continue;
                }

                // VirtualizePage mutates realizedPages; the reverse walk keeps the
                // remaining indices valid.
                VirtualizePage(pageViews[index]);
            }
        }

        private void ReleasePageBitmap(PageView view)
        {
            ReleasePageDetail(view);
            cachedPages.Remove((int)view.Index);
            cachedBitmapBytes -= view.BitmapBytes;
            view.BitmapBytes = 0;
            if (view.Image != null)
            {
                view.Image.Source = null;
            }

            if (view.Raster != null) { view.Raster.Dispose(); view.Raster = null; }

            view.IsRendered = false;
            view.RenderedWidth = 0;
        }

        // ============================== Navigation ==============================

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (document == null || pageIndex == 0)
            {
                return;
            }

            GoToPage(pageIndex - 1);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (document == null || pageIndex + 1 >= document.PageCount)
            {
                return;
            }

            GoToPage(pageIndex + 1);
        }

        private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var control = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (control && e.Key == Windows.System.VirtualKey.F && document != null)
            {
                e.Handled = true;
                ShowSearch();
                return;
            }

            if (control && e.Key == Windows.System.VirtualKey.O)
            {
                e.Handled = true;
                await OpenViaPickerAsync();
                return;
            }

            if (control && e.Key == Windows.System.VirtualKey.S)
            {
                e.Handled = true;
                await SaveCurrentAnnotationsWithFeedbackAsync();
                return;
            }

            if (document == null)
            {
                return;
            }

            // Leave native text inputs (such as the zoom box) in control of their shortcuts.
            if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox) return;
            if (control && e.Key == Windows.System.VirtualKey.C && selectedTextPage != null)
            {
                e.Handled = true;
                CopySelectedText();
                return;
            }
            if (control && e.Key == Windows.System.VirtualKey.A && currentInkTool == InkTool.None)
            {
                var view = GetCurrentPageView();
                if (view != null)
                {
                    await EnsurePageTextAsync(view, activeRenderToken);
                    if (pageViews.Contains(view)) SelectAllPageText(view);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Escape && selectedTextPage != null)
            {
                ClearTextSelection();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Space:
                case Windows.System.VirtualKey.PageDown:
                case Windows.System.VirtualKey.Right:
                case Windows.System.VirtualKey.Down:
                    if (pageIndex + 1 < document.PageCount)
                    {
                        e.Handled = true;
                        GoToPage(pageIndex + 1);
                    }
                    break;
                case Windows.System.VirtualKey.Back:
                case Windows.System.VirtualKey.PageUp:
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Up:
                    if (pageIndex > 0)
                    {
                        e.Handled = true;
                        GoToPage(pageIndex - 1);
                    }
                    break;
                case Windows.System.VirtualKey.Home:
                    e.Handled = true;
                    GoToPage(0);
                    break;
                case Windows.System.VirtualKey.End:
                    e.Handled = true;
                    GoToPage(document.PageCount - 1);
                    break;
                case Windows.System.VirtualKey.Add:
                case (Windows.System.VirtualKey)0xBB: // OemPlus
                    e.Handled = true;
                    SetZoom(DocumentScroller.ZoomFactor * 1.25f, true);
                    break;
                case Windows.System.VirtualKey.Subtract:
                case (Windows.System.VirtualKey)0xBD: // OemMinus
                    e.Handled = true;
                    SetZoom(DocumentScroller.ZoomFactor / 1.25f, true);
                    break;
                case Windows.System.VirtualKey.Number0:
                case Windows.System.VirtualKey.NumberPad0:
                    e.Handled = true;
                    FitToWindow(true);
                    break;
            }
        }

        private void GoToPage(uint nextIndex)
        {
            if (document == null || nextIndex >= document.PageCount)
            {
                return;
            }

            pageIndex = nextIndex;
            if (fitPageToViewport) FitToWindow(false);
            ScrollToPage(nextIndex, true);
            UpdateThumbnailSelection();
            EvictFarPages();
            UpdateUi();
        }

        private void FitToWindow()
        {
            FitToWindow(true);
        }

        private void FitToWindow(bool animated)
        {
            pendingLayoutAnchor = null;
            fitPageToViewport = true;
            var fitIndex = pageIndex;
            fitReferencePage = fitIndex;
            var view = GetCurrentPageView();
            if (view == null) return;
            var zoom = FitZoom(view);
            PageFrame.Width = Math.Max(lastColumnWidth + 2 * PageBorderThickness, Math.Max(1, DocumentScroller.ViewportWidth) / zoom);
            DocumentScroller.ChangeView(0f, (float)TopOfPage(fitIndex, zoom), zoom, !animated);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(DocumentScroller.ZoomFactor * 1.25f, true);
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(DocumentScroller.ZoomFactor / 1.25f, true);
        }

        private void FitButton_Click(object sender, RoutedEventArgs e)
        {
            FitToWindow(true);
        }

        private void SetZoom(float newZoom)
        {
            SetZoom(newZoom, true);
        }

        private void SetZoom(float newZoom, bool animated)
        {
            pendingLayoutAnchor = null;
            fitPageToViewport = false;
            newZoom = (float)Math.Max(DocumentScroller.MinZoomFactor, Math.Min(DocumentScroller.MaxZoomFactor, newZoom));
            var oldZoom = DocumentScroller.ZoomFactor;
            if (Math.Abs(newZoom - oldZoom) < 0.005f)
            {
                return;
            }

            var viewportWidth = Math.Max(1, DocumentScroller.ViewportWidth);
            var viewportHeight = Math.Max(1, DocumentScroller.ViewportHeight);

            var requiredFrameWidth = Math.Max(lastColumnWidth + 2 * PageBorderThickness, viewportWidth / newZoom);
            if (Math.Abs(PageFrame.Width - requiredFrameWidth) > 0.5)
            {
                PageFrame.Width = requiredFrameWidth;
            }

            float targetOffsetX;
            if (lastColumnWidth * newZoom <= viewportWidth)
            {
                targetOffsetX = 0f;
            }
            else
            {
                targetOffsetX = (float)Math.Max(0, (lastColumnWidth * newZoom - viewportWidth) / 2.0);
            }

            var centerY = (DocumentScroller.VerticalOffset + viewportHeight / 2) / oldZoom;
            var targetOffsetY = (float)Math.Max(0, centerY * newZoom - viewportHeight / 2);

            DocumentScroller.ChangeView(targetOffsetX, targetOffsetY, newZoom, !animated);
        }

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isUpdatingZoomSlider || DocumentScroller == null)
            {
                return;
            }

            // ValueChanged fires for every pixel of a drag; coalesce to ~30 Hz so the
            // ScrollViewer and page layout are not hammered with intermediate zooms.
            pendingSliderZoom = (float)(e.NewValue / 100.0);
            MarkZoomChanging();
            if (sliderZoomTimer == null)
            {
                sliderZoomTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                sliderZoomTimer.Tick += SliderZoomTimer_Tick;
            }

            if (!sliderZoomTimer.IsEnabled)
            {
                sliderZoomTimer.Start();
            }
        }

        private void SliderZoomTimer_Tick(object sender, object e)
        {
            sliderZoomTimer.Stop();
            if (pendingSliderZoom == null)
            {
                return;
            }

            var zoom = pendingSliderZoom.Value;
            pendingSliderZoom = null;
            MarkZoomChanging();
            SetZoom(zoom, false);
        }

        private void MarkZoomChanging()
        {
            zoomSettledAt = DateTime.UtcNow + ZoomSettleDelay;
            WakeRenderer();
        }

        private void ZoomPercentButton_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentScroller == null)
            {
                return;
            }

            var percent = (int)Math.Round(DocumentScroller.ZoomFactor * 100);
            ZoomInputBox.Text = percent.ToString(CultureInfo.InvariantCulture);
            ZoomInputBox.SelectAll();
        }

        private void ZoomInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                ApplyCustomZoom();
                ZoomInputFlyout.Hide();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                ZoomInputFlyout.Hide();
            }
        }

        private void ZoomInputOkButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyCustomZoom();
            ZoomInputFlyout.Hide();
        }

        private void ApplyCustomZoom()
        {
            if (DocumentScroller == null || string.IsNullOrWhiteSpace(ZoomInputBox.Text))
            {
                return;
            }

            var text = ZoomInputBox.Text.Trim().TrimEnd('%').Trim();
            double percent;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
            {
                percent = Math.Max(25, Math.Min(400, percent));
                SetZoom((float)(percent / 100.0), true);
            }
        }

        private void UpdateZoomControls()
        {
            if (DocumentScroller == null)
            {
                return;
            }

            var percent = (int)Math.Round(DocumentScroller.ZoomFactor * 100);
            // ViewChanged fires for every frame of a scroll, where the zoom is
            // usually unchanged; bail before the string work rather than after it.
            if (percent == lastZoomPercent)
            {
                return;
            }

            lastZoomPercent = percent;

            var text = percent.ToString(CultureInfo.InvariantCulture) + "%";
            if (ZoomLabel.Text != text)
            {
                ZoomLabel.Text = text;
            }

            if (ZoomSlider != null && Math.Abs(ZoomSlider.Value - percent) >= 1)
            {
                isUpdatingZoomSlider = true;
                try
                {
                    ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, percent));
                }
                finally
                {
                    isUpdatingZoomSlider = false;
                }
            }
        }

        private void ScrollToPage(uint index, bool animated)
        {
            pendingLayoutAnchor = null;
            if (index >= (uint)pageViews.Count)
            {
                return;
            }

            DocumentScroller.ChangeView(null, (float)TopOfPage(index, DocumentScroller.ZoomFactor), null, !animated);
        }

        // Top of the page expressed in the ScrollViewer's offset space (zoom applied).
        // Read straight from the cached table, so this stays correct even before the
        // page has ever been measured or realized.
        private double TopOfPage(uint index, float zoom)
        {
            if (index >= (uint)pageViews.Count || pageTops.Length != pageViews.Count)
            {
                return 0;
            }

            return Math.Max(0, pageTops[index] * zoom);
        }

        private void DocumentScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) pendingLayoutAnchor = null;
            if (document == null)
            {
                return;
            }

            var zoom = DocumentScroller.ZoomFactor;
            scrollActivity.Observe(DocumentScroller.HorizontalOffset, DocumentScroller.VerticalOffset,
                e.IsIntermediate, DateTime.UtcNow);
            if (scrollActivity.IsDirectManipulationActive && Math.Abs(zoom - lastObservedZoom) > 0.001f)
                fitPageToViewport = false;
            if (Math.Abs(zoom - lastObservedZoom) > 0.001f)
            {
                lastObservedZoom = zoom;
                MarkZoomChanging();
            }

            UpdatePageCentering();
            UpdateCurrentPageFromScroll();
            RestoreVisiblePages();
            UpdateZoomControls();
            if (!e.IsIntermediate) FollowCurrentThumbnail();
            var reference = GetFitReferencePage();
            if (!e.IsIntermediate && fitPageToViewport && reference != null &&
                Math.Abs(DocumentScroller.ZoomFactor - FitZoom(reference, false)) > 0.005f)
                ScheduleRelayout();
            WakeRenderer();
        }

        // Scrollbar/layout changes can affect fit even though natural page width is stable.
        private void DocumentScroller_LayoutUpdated(object sender, object e)
        {
            if (document == null)
            {
                return;
            }

            RestoreLayoutAnchor();
            var reference = GetFitReferencePage();
            if (fitPageToViewport && !scrollActivity.IsDirectManipulationActive && IsScrollSettled() && reference != null &&
                Math.Abs(DocumentScroller.ZoomFactor - FitZoom(reference, false)) > 0.005f)
            {
                ScheduleRelayout();
            }
        }

        // Binary search over the cached offset table. The previous version probed
        // Chrome.TransformToVisual() at each step, which forced layout-dependent
        // transform work O(log n) times per scroll frame; this touches no visuals at
        // all and runs entirely on cached doubles.
        private void UpdateCurrentPageFromScroll()
        {
            if (document == null || pageViews.Count == 0 || pageTops.Length != pageViews.Count)
            {
                return;
            }

            var zoom = DocumentScroller.ZoomFactor;
            if (zoom <= 0.01f)
            {
                zoom = 1f;
            }

            var viewportHeight = DocumentScroller.ViewportHeight;
            if (viewportHeight < 1)
            {
                viewportHeight = DocumentScroller.ActualHeight;
            }

            // Anchor near the leading edge: a short/landscape page should not be
            // skipped just because the next page occupies the viewport centre.
            var top = DocumentScroller.VerticalOffset / zoom;
            var leading = PageAtOffset(top);
            var anchor = top + Math.Min(viewportHeight / zoom * 0.1, pageViews[leading].LayoutHeight * 0.25);
            var lo = PageAtOffset(anchor);
            if ((uint)lo != pageIndex)
            {
                pageIndex = (uint)lo;
                UpdateThumbnailSelection();
                EvictFarPages();
                // Scrolling only changes which page is current; the full UpdateUi()
                // pass (tooltips, visibility, thumbnail pane, dirty scan) is not
                // needed on every page boundary crossed during a flick.
                UpdatePageIndicators();
            }
        }

        // The subset of UpdateUi() that actually varies with the page index.
        private void UpdatePageIndicators()
        {
            var hasDocument = document != null && pageViews.Count > 0;
            PreviousButton.IsEnabled = hasDocument && pageIndex > 0;
            NextButton.IsEnabled = hasDocument && pageIndex + 1 < document.PageCount;
            PageStatusText.Text = hasDocument
                ? string.Format(CultureInfo.InvariantCulture, "{0} / {1}", pageIndex + 1, document.PageCount)
                : "0 / 0";
        }

        // ============================== Thumbnails ==============================

        // ============================== Annotation storage ==============================

        private bool HasUnsavedChanges()
        {
            return currentFile != null && document != null && dirtyCount > 0;
        }

        private void UpdateDirtyState()
        {
            var dirty = HasUnsavedChanges();
            if (SaveButton != null)
            {
                SaveButton.IsEnabled = document != null;
            }

            // Tooltip and title text only change when the flag flips; this runs on
            // every stroke and every page change, so the string work is worth
            // skipping.
            if (dirty == lastDirtyShown)
            {
                return;
            }

            lastDirtyShown = dirty;

            if (SaveButton != null)
            {
                ToolTipService.SetToolTip(SaveButton, dirty ? "保存批注 (Ctrl+S) *" : "已保存 (Ctrl+S)");
            }

            if (FileNameText != null && currentFile != null)
            {
                FileNameText.Text = (dirty ? "* " : "") + currentFile.Name;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentAnnotationsWithFeedbackAsync();
        }

        private async Task SaveCurrentAnnotationsWithFeedbackAsync()
        {
            if (currentFile == null || document == null)
            {
                return;
            }

            var token = activeRenderToken;
            try
            {
                await SaveCurrentAnnotationsAsync();
                if (token != activeRenderToken) return;
                ShowSaveFeedback();
            }
            catch (Exception ex)
            {
                if (token != activeRenderToken) return;
                var errorDialog = new Windows.UI.Popups.MessageDialog("保存失败: " + ex.Message, "错误");
                await errorDialog.ShowAsync();
            }
        }

        private void ShowSaveFeedback()
        {
            if (SaveButton == null)
            {
                return;
            }

            if (SaveButton.RenderTransform == null || !(SaveButton.RenderTransform is CompositeTransform))
            {
                SaveButton.RenderTransformOrigin = new Point(0.5, 0.5);
                SaveButton.RenderTransform = new CompositeTransform();
            }

            var transform = (CompositeTransform)SaveButton.RenderTransform;
            var sb = new Storyboard();
            var animX = new DoubleAnimation
            {
                From = 0.85,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animX, transform);
            Storyboard.SetTargetProperty(animX, "ScaleX");

            var animY = new DoubleAnimation
            {
                From = 0.85,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animY, transform);
            Storyboard.SetTargetProperty(animY, "ScaleY");

            sb.Children.Add(animX);
            sb.Children.Add(animY);
            sb.Begin();

            ToolTipService.SetToolTip(SaveButton, "已保存 ✓");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (SaveButton != null)
                {
                    ToolTipService.SetToolTip(SaveButton, HasUnsavedChanges() ? "保存墨迹 (Ctrl+S) *" : "已保存 (Ctrl+S)");
                }
            };
            timer.Start();
        }

        private async Task<SavePromptResult> PromptSaveUnsavedChangesAsync()
        {
            if (isPromptingSave)
            {
                return SavePromptResult.Cancel;
            }

            isPromptingSave = true;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "未保存的更改"
                };

                var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                var text = new TextBlock
                {
                    Text = "当前文档有未保存的墨迹批注。是否保存更改？",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 24),
                    FontSize = 14
                };
                panel.Children.Add(text);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var saveBtn = new Button
                {
                    Content = "保存",
                    MinWidth = 76,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    Foreground = new SolidColorBrush(Colors.White)
                };
                var dontSaveBtn = new Button
                {
                    Content = "不保存",
                    MinWidth = 76,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var cancelBtn = new Button
                {
                    Content = "取消",
                    MinWidth = 76
                };

                buttonPanel.Children.Add(saveBtn);
                buttonPanel.Children.Add(dontSaveBtn);
                buttonPanel.Children.Add(cancelBtn);
                panel.Children.Add(buttonPanel);

                dialog.Content = panel;

                var promptResult = SavePromptResult.Cancel;
                saveBtn.Click += (s, e) =>
                {
                    promptResult = SavePromptResult.Save;
                    dialog.Hide();
                };
                dontSaveBtn.Click += (s, e) =>
                {
                    promptResult = SavePromptResult.DontSave;
                    dialog.Hide();
                };
                cancelBtn.Click += (s, e) =>
                {
                    promptResult = SavePromptResult.Cancel;
                    dialog.Hide();
                };

                await dialog.ShowAsync();
                return promptResult;
            }
            finally
            {
                isPromptingSave = false;
            }
        }

        private async Task<bool> ConfirmCloseAsync()
        {
            if (!HasUnsavedChanges())
            {
                return true;
            }

            var promptResult = await PromptSaveUnsavedChangesAsync();
            if (promptResult == SavePromptResult.Save)
            {
                await SaveCurrentAnnotationsAsync();
                return true;
            }
            else if (promptResult == SavePromptResult.DontSave)
            {
                for (var i = 0; i < pageViews.Count; i++)
                {
                    pageViews[i].Dirty = false;
                }
                dirtyCount = 0;
                UpdateDirtyState();
                return true;
            }
            else
            {
                return false;
            }
        }

        private void RegisterCloseRequestedHandler()
        {
            try
            {
                var previewType = Type.GetType("Windows.UI.Core.Preview.SystemNavigationManagerPreview, Windows, ContentType=WindowsRuntime");
                if (previewType != null)
                {
                    var getMethod = previewType.GetTypeInfo().GetDeclaredMethod("GetForCurrentView");
                    if (getMethod != null)
                    {
                        var manager = getMethod.Invoke(null, null);
                        if (manager != null)
                        {
                            var closeEvent = previewType.GetTypeInfo().GetDeclaredEvent("CloseRequested");
                            if (closeEvent != null && closeEvent.EventHandlerType != null)
                            {
                                var genericArg = closeEvent.EventHandlerType.GenericTypeArguments[0];
                                var helperMethod = typeof(MainPage).GetTypeInfo()
                                    .GetDeclaredMethod(nameof(CreateCloseRequestedHandler))
                                    .MakeGenericMethod(genericArg);

                                var handler = (Delegate)helperMethod.Invoke(this, null);
                                var addMethod = closeEvent.AddMethod;
                                if (addMethod == null)
                                {
                                    addMethod = previewType.GetTypeInfo().GetDeclaredMethod("add_CloseRequested");
                                }
                                if (addMethod != null)
                                {
                                    addMethod.Invoke(manager, new object[] { handler });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CloseRequested registration failed: " + ex.Message);
            }
        }

        private EventHandler<T> CreateCloseRequestedHandler<T>()
        {
            return async (sender, args) =>
            {
                if (args == null)
                {
                    return;
                }

                var typeInfo = typeof(T).GetTypeInfo();
                var getDeferralMethod = typeInfo.GetDeclaredMethod("GetDeferral");
                var handledProp = typeInfo.GetDeclaredProperty("Handled");

                object deferral = null;
                if (getDeferralMethod != null)
                {
                    deferral = getDeferralMethod.Invoke(args, null);
                }

                try
                {
                    var canClose = await ConfirmCloseAsync();
                    if (!canClose && handledProp != null)
                    {
                        handledProp.SetValue(args, true);
                    }
                }
                finally
                {
                    if (deferral != null)
                    {
                        var completeMethod = deferral.GetType().GetTypeInfo().GetDeclaredMethod("Complete");
                        if (completeMethod != null)
                        {
                            completeMethod.Invoke(deferral, null);
                        }
                    }
                }
            };
        }

        // Loads into the PageView's own stroke container rather than a live
        // InkCanvas, so it works whether or not the page is currently realized.
        private async Task LoadInkAsync(PageView view)
        {
            if (view.InkLoaded || view.IsLoadingInk)
            {
                return;
            }

            view.IsLoadingInk = true;
            try
            {
                var token = activeRenderToken;
                await EnsurePageGeometryAsync(view, token);
                var folder = await GetAnnotationFolderAsync();
                if (token != activeRenderToken) return;
                var strokes = view.EnsureStrokes();
                strokes.Clear();
                var file = await TryGetInkFileAsync(view.Index, folder);
                if (file != null)
                {
                    using (var stream = await file.OpenSequentialReadAsync())
                    {
                        await strokes.LoadAsync(stream);
                    }
                }

                if (token != activeRenderToken) return;
                view.InkLoaded = true;
                try { await LoadTextHighlightsAsync(view, folder, token); }
                catch (Exception)
                {
                    if (view.Chrome != null) AutomationProperties.SetHelpText(view.Chrome, "无法读取已保存的高亮批注");
                }
            }
            finally
            {
                view.IsLoadingInk = false;
                if (view.InkLayer != null)
                    ConfigureInkCanvas(view.InkLayer, CreateInkAttributes(), view.InkLoaded);
                ReleaseCleanAnnotations(view);
            }
        }

        private async Task SaveInkAsync(PageView view, StorageFolder folder)
        {
            if (currentFile == null || view == null || view.IsLoadingInk)
            {
                return;
            }

            if (view.Strokes == null)
            {
                return;
            }

            var strokes = view.Strokes.GetStrokes();
            if (!view.InkLoaded && strokes.Count == 0)
            {
                return;
            }

            var fileName = GetInkFileName(view.Index);

            if (strokes.Count == 0)
            {
                var existing = await folder.TryGetItemAsync(fileName) as StorageFile;
                if (existing != null) await existing.DeleteAsync(StorageDeleteOption.PermanentDelete);

                return;
            }

            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                await view.Strokes.SaveAsync(stream);
            }

        }

        private async Task<StorageFile> TryGetInkFileAsync(uint index, StorageFolder folder)
        {
            return await folder.TryGetItemAsync(GetInkFileName(index)) as StorageFile;
        }

        // Cached per document: this used to hit the filesystem for every page load
        // and every page save.
        private async Task<StorageFolder> GetAnnotationFolderAsync()
        {
            if (annotationFolder != null)
            {
                return annotationFolder;
            }

            var root = ApplicationData.Current.LocalFolder;
            var file = currentFile;
            var folderName = "ink-" + StableId(file.Path + file.DateCreated.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
            var folder = await root.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
            if (file == currentFile) annotationFolder = folder;
            return folder;
        }

        private static string StableId(string text)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }

                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        private static string GetInkFileName(uint index)
        {
            return "page-" + index.ToString(CultureInfo.InvariantCulture) + ".isf";
        }

        // ============================== Ink tools ==============================

        private void BuildColorPalette(Grid grid, List<SwatchItem> list, bool isHighlighter)
        {
            if (grid == null)
            {
                return;
            }

            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            for (int r = 0; r < 5; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            for (int c = 0; c < 6; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            for (int i = 0; i < WindowsInkPalette.Length; i++)
            {
                int row = i / 6;
                int col = i % 6;
                var color = ParseHexColor(WindowsInkPalette[i]);

                var ring = new Ellipse
                {
                    Width = 28,
                    Height = 28,
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    StrokeThickness = 2,
                    Visibility = Visibility.Collapsed
                };

                var circle = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                    StrokeThickness = 0.5
                };

                var container = new Grid
                {
                    Width = 28,
                    Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                container.Children.Add(ring);
                container.Children.Add(circle);

                var btn = new Button
                {
                    Content = container,
                    Width = 36,
                    Height = 36,
                    Padding = new Thickness(0),
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0)
                };

                var swatch = new SwatchItem { Button = btn, Ring = ring, Color = color };
                list.Add(swatch);

                var capturedColor = color;
                btn.Click += (s, e) =>
                {
                    if (isHighlighter)
                    {
                        highlighterColor = capturedColor;
                        UpdateSwatchSelection(highlighterSwatches, highlighterColor);
                        UpdateHighlighterPreview();
                        if (currentInkTool == InkTool.Highlighter)
                        {
                            ApplyInkAttributes();
                        }
                    }
                    else
                    {
                        penColor = capturedColor;
                        UpdateSwatchSelection(penSwatches, penColor);
                        UpdatePenPreview();
                        if (currentInkTool == InkTool.Pen)
                        {
                            ApplyInkAttributes();
                        }
                    }
                };

                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                grid.Children.Add(btn);
            }
        }

        private static void UpdateSwatchSelection(List<SwatchItem> swatches, Color selectedColor)
        {
            for (int i = 0; i < swatches.Count; i++)
            {
                var item = swatches[i];
                bool matches = item.Color.R == selectedColor.R && item.Color.G == selectedColor.G && item.Color.B == selectedColor.B;
                item.Ring.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static Color ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb(255, r, g, b);
        }

        private void TopTouchWritingButton_Click(object sender, RoutedEventArgs e)
        {
            touchInkingEnabled = TopTouchWritingButton.IsChecked == true;
            RefreshRealizedInkCanvases();
        }

        private void EnsurePalettesBuilt()
        {
            if (palettesBuilt)
            {
                return;
            }

            palettesBuilt = true;
            BuildColorPalette(PenColorGrid, penSwatches, false);
            BuildColorPalette(HighlighterColorGrid, highlighterSwatches, true);
            UpdateSwatchSelection(penSwatches, penColor);
            UpdateSwatchSelection(highlighterSwatches, highlighterColor);
        }

        private void TopPenButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentInkTool == InkTool.Pen)
            {
                EnsurePalettesBuilt();
                FlyoutBase.ShowAttachedFlyout(TopPenButton);
            }
            else
            {
                SetInkTool(InkTool.Pen);
            }
        }

        private void TopHighlighterButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentInkTool == InkTool.Highlighter)
            {
                EnsurePalettesBuilt();
                FlyoutBase.ShowAttachedFlyout(TopHighlighterButton);
            }
            else
            {
                SetInkTool(InkTool.Highlighter);
            }
        }

        private void TopEraserButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentInkTool == InkTool.Eraser)
            {
                SetInkTool(InkTool.None);
            }
            else
            {
                SetInkTool(InkTool.Eraser);
            }
        }

        private void PenSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            penStrokeSize = Math.Max(1, e.NewValue);
            if (currentInkTool == InkTool.Pen)
            {
                ApplyInkAttributes();
            }
            UpdatePenPreview();
        }

        private void HighlighterSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            highlighterStrokeSize = Math.Max(4, e.NewValue);
            if (currentInkTool == InkTool.Highlighter)
            {
                ApplyInkAttributes();
            }
            UpdateHighlighterPreview();
        }

        private void UpdatePenPreview()
        {
            if (PenPreviewStroke != null)
            {
                PenPreviewStroke.Stroke = new SolidColorBrush(penColor);
                PenPreviewStroke.StrokeThickness = penStrokeSize;
            }
        }

        private void UpdateHighlighterPreview()
        {
            if (HighlighterPreviewStroke != null)
            {
                HighlighterPreviewStroke.Stroke = new SolidColorBrush(highlighterColor);
                HighlighterPreviewStroke.StrokeThickness = highlighterStrokeSize;
            }
        }

        private void SetInkTool(InkTool tool)
        {
            if (tool != InkTool.None) ClearTextSelection();
            SetTextCursor(false);
            currentInkTool = tool;
            if (TopSelectButton != null) TopSelectButton.IsChecked = tool == InkTool.None;

            if (TopPenButton != null)
            {
                TopPenButton.IsChecked = tool == InkTool.Pen;
            }
            if (TopHighlighterButton != null)
            {
                TopHighlighterButton.IsChecked = tool == InkTool.Highlighter;
            }
            if (TopEraserButton != null)
            {
                TopEraserButton.IsChecked = tool == InkTool.Eraser;
            }

            RefreshRealizedInkCanvases();
        }

        // Only pages with a live InkCanvas need touching, and the attribute object is
        // built once for the whole sweep. Both loops used to run over every page in
        // the document and allocate a fresh InkDrawingAttributes per page, which the
        // stroke-size sliders triggered continuously while being dragged.
        private void RefreshRealizedInkCanvases()
        {
            if (realizedPages.Count == 0)
            {
                return;
            }

            var attributes = CreateInkAttributes();
            for (var i = 0; i < realizedPages.Count; i++)
            {
                var view = pageViews[realizedPages[i]];
                ConfigureInkCanvas(view.InkLayer, attributes, view.InkLoaded && !view.IsLoadingInk);
                pageViews[realizedPages[i]].SelectionLayer.IsHitTestVisible = currentInkTool == InkTool.None;
            }
        }

        private void ConfigureInkCanvas(InkCanvas inkCanvas, InkDrawingAttributes attributes, bool ready)
        {
            inkCanvas.IsHitTestVisible = ready && currentInkTool != InkTool.None;
            if (!ready || currentInkTool == InkTool.None)
            {
                inkCanvas.InkPresenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.None;
            }
            else
            {
                var types = Windows.UI.Core.CoreInputDeviceTypes.Pen | Windows.UI.Core.CoreInputDeviceTypes.Mouse;
                if (touchInkingEnabled)
                {
                    types |= Windows.UI.Core.CoreInputDeviceTypes.Touch;
                }
                inkCanvas.InkPresenter.InputDeviceTypes = types;
            }

            inkCanvas.InkPresenter.InputProcessingConfiguration.Mode =
                currentInkTool == InkTool.Eraser ? InkInputProcessingMode.Erasing :
                currentInkTool == InkTool.Pen || currentInkTool == InkTool.Highlighter ? InkInputProcessingMode.Inking :
                InkInputProcessingMode.None;

            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(attributes);
        }

        private void ApplyInkAttributes()
        {
            if (realizedPages.Count == 0)
            {
                return;
            }

            var attributes = CreateInkAttributes();
            for (var i = 0; i < realizedPages.Count; i++)
            {
                pageViews[realizedPages[i]].InkLayer.InkPresenter.UpdateDefaultDrawingAttributes(attributes);
            }
        }

        private InkDrawingAttributes CreateInkAttributes()
        {
            var isHighlighter = currentInkTool == InkTool.Highlighter;
            var size = isHighlighter ? highlighterStrokeSize : penStrokeSize;
            var color = isHighlighter ? highlighterColor : penColor;

            if (isHighlighter && color.A == 255)
            {
                color.A = 210;
            }

            var attributes = new InkDrawingAttributes();
            attributes.Color = color;
            attributes.Size = new Size(size, size);
            attributes.IgnorePressure = false;
            attributes.FitToCurve = true;
            attributes.PenTip = isHighlighter ? PenTipShape.Rectangle : PenTipShape.Circle;

            try
            {
                attributes.DrawAsHighlighter = isHighlighter;
            }
            catch (Exception)
            {
                // Older inking runtimes can expose the property but fail when setting it.
            }

            return attributes;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            var view = GetCurrentPageView();
            if (view == null || view.IsLoadingInk || view.Strokes == null)
            {
                return;
            }

            var strokes = view.Strokes.GetStrokes();
            var undoHighlight = view.UndoKinds.Count > 0 ? view.UndoKinds[view.UndoKinds.Count - 1] : strokes.Count == 0;
            if (view.UndoKinds.Count > 0) view.UndoKinds.RemoveAt(view.UndoKinds.Count - 1);
            if (undoHighlight && view.Highlights.Count > 0)
            {
                view.Highlights.RemoveAt(view.Highlights.Count - 1);
                MarkPageDirty(view);
                RedrawTextLayers(view);
                return;
            }
            if (strokes.Count == 0)
            {
                return;
            }

            strokes[strokes.Count - 1].Selected = true;
            view.Strokes.DeleteSelected();
            MarkPageDirty(view);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var view = GetCurrentPageView();
            if (view == null || view.IsLoadingInk || view.Strokes == null || (view.Strokes.GetStrokes().Count == 0 && view.Highlights.Count == 0))
            {
                return;
            }

            view.Strokes.Clear();
            view.Highlights.Clear();
            view.UndoKinds.Clear();
            if (selectedTextPage == view) ClearTextSelection();
            RedrawTextLayers(view);
            MarkPageDirty(view);
        }

        // ============================== Resize / state ==============================

        private void DocumentScroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageCentering();
            ScheduleRelayout();
            UpdateHudBarResponsive(e.NewSize.Width);
        }

        private void DocumentSurface_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageCentering();
            ScheduleRelayout();
            if (DocumentScroller != null)
            {
                UpdateHudBarResponsive(DocumentScroller.ActualWidth);
            }
        }

        private void UpdateHudBarResponsive(double availableWidth)
        {
            if (HudBar != null)
            {
                HudBar.MaxWidth = Math.Max(260, availableWidth - 24);
                if (ZoomSlider != null)
                {
                    ZoomSlider.Visibility = availableWidth < 460 ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        // One reusable timer instead of a fresh Task plus async state machine per
        // resize event; a window drag raises these continuously.
        private void ScheduleRelayout()
        {
            if (relayoutTimer == null)
            {
                relayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                relayoutTimer.Tick += (s, e) =>
                {
                    relayoutTimer.Stop();
                    RebuildLayout();
                };
            }

            // Restarting resets the countdown, so a drag-resize rebuilds once at the end.
            relayoutTimer.Stop();
            relayoutTimer.Start();
        }

        private PageView GetCurrentPageView()
        {
            if (pageViews.Count == 0 || pageIndex >= (uint)pageViews.Count)
            {
                return null;
            }

            return pageViews[(int)pageIndex];
        }

        private void UpdateUi()
        {
            var hasDocument = document != null && pageViews.Count > 0;
            var hasPrevious = hasDocument && pageIndex > 0;
            var hasNext = hasDocument && pageIndex + 1 < document.PageCount;

            HudBar.Visibility = hasDocument ? Visibility.Visible : Visibility.Collapsed;
            if (InkControlsPanel != null)
            {
                InkControlsPanel.Visibility = hasDocument ? Visibility.Visible : Visibility.Collapsed;
            }
            PreviousButton.IsEnabled = hasPrevious;
            NextButton.IsEnabled = hasNext;
            ZoomInButton.IsEnabled = hasDocument;
            ZoomOutButton.IsEnabled = hasDocument;
            ZoomPercentButton.IsEnabled = hasDocument;
            ZoomSlider.IsEnabled = hasDocument;
            FitButton.IsEnabled = hasDocument;

            TopTouchWritingButton.IsEnabled = hasDocument;
            TopSelectButton.IsEnabled = hasDocument;
            AnnotationToolsButton.IsEnabled = hasDocument;
            TopPenButton.IsEnabled = hasDocument;
            TopHighlighterButton.IsEnabled = hasDocument;
            TopEraserButton.IsEnabled = hasDocument;
            TopUndoButton.IsEnabled = hasDocument;
            TopClearButton.IsEnabled = hasDocument;

            if (SaveButton != null)
            {
                SaveButton.IsEnabled = hasDocument;
            }
            UpdateDirtyState();

            SetThumbsVisible(hasDocument && thumbsRequested);
            EmptyState.Visibility = hasDocument ? Visibility.Collapsed : Visibility.Visible;

            PageStatusText.Text = hasDocument
                ? string.Format(CultureInfo.InvariantCulture, "{0} / {1}", pageIndex + 1, document.PageCount)
                : "0 / 0";
        }

        private sealed class PageView : IPdfRenderState
        {
            public uint Index { get; set; }
            public Border Chrome { get; set; }
            public Grid Host { get; set; }
            public Image Image { get; set; }
            public PdfPageRaster Raster { get; set; }
            public ImageSource Source { get { return Raster == null ? null : Raster.Source; } }
            public PdfPageRaster DetailRaster { get; set; }
            public Canvas DetailLayer { get; set; }
            public Image DetailImage { get; set; }
            public Rect DetailRegion { get; set; }
            public double DetailScale { get; set; }
            public bool DetailFailed { get; set; }
            public InkCanvas InkLayer { get; set; }
            public double LayoutWidth { get; set; }
            public double LayoutHeight { get; set; }
            public double Aspect { get; set; }
            public bool AspectKnown { get; set; }
            public double RenderedWidth { get; set; }
            public long BitmapBytes { get; set; }
            public bool IsRendered { get; set; }
            public bool RenderFailed { get; set; }
            public bool IsLoadingInk { get; set; }
            public bool InkLoaded { get; set; }
            public bool Dirty { get; set; }
            public int Revision { get; set; }
            public Canvas HighlightLayer { get; set; }
            public Canvas SelectionLayer { get; set; }
            public List<TextGlyph> Glyphs { get; set; }
            public Task TextTask { get; set; }
            public bool TextFailed { get; set; }
            public bool HighlightsLoaded { get; set; }
            public readonly List<TextHighlight> Highlights = new List<TextHighlight>();
            // true = text highlight, false = collected ink stroke; newest action first on undo.
            public readonly List<bool> UndoKinds = new List<bool>();

            // Dirty strokes survive virtualization; saved offscreen data is released.
            public InkStrokeContainer Strokes { get; set; }

            public bool IsRealized
            {
                get { return InkLayer != null; }
            }

            public InkStrokeContainer EnsureStrokes()
            {
                if (Strokes == null)
                {
                    Strokes = new InkStrokeContainer();
                }

                return Strokes;
            }

            public bool NeedsRender(double targetWidth)
            {
                if (!IsRendered)
                {
                    return true;
                }

                return targetWidth - RenderedWidth > Math.Max(96, targetWidth * 0.3);
            }
        }

        private sealed class ThumbnailItem
        {
            public PdfPageRaster Raster { get; set; }
            public Button Button { get; set; }
            public Border Card { get; set; }
            public TextBlock Number { get; set; }
            public Image Image { get; set; }
            public bool IsRendered { get; set; }
            public bool RenderFailed { get; set; }
        }

        private enum InkTool
        {
            None,
            Pen,
            Highlighter,
            Eraser
        }

        private enum SavePromptResult
        {
            Save,
            DontSave,
            Cancel
        }
    }
}
