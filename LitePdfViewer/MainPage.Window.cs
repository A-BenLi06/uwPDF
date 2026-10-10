using System;
using System.Threading.Tasks;
#if CPPWINRT_RENDERER
using PreviewDisplay = PdfNative.Rendering.PreviewDisplay;
#else
using PreviewDisplay = PdfNative.PreviewDisplay;
#endif
using Windows.ApplicationModel.Core;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI;
using Windows.System;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private Size firstPageSize = new Size(600, 800);
        private bool userSized;
        private bool previewSizeInitialized;
        private bool applyingPreviewSize;
        private ulong applyingPreviewSizeToken;
        private Task<Size> pendingWorkAreaRead;
        private bool fitPageToViewport = true;
        private bool? thumbnailPreference;
        private DispatcherTimer hudIdleTimer;
        private Storyboard hudFade;
        private bool hudPointerOver;
        private bool hudFlyoutOpen;

        private void InitializeReaderChrome()
        {
            hudIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hudIdleTimer.Tick += (s, e) =>
            {
                hudIdleTimer.Stop();
                if (hudPointerOver || hudFlyoutOpen || HasHudFocus()) return;
                hudFade = new Storyboard();
                var fade = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
                Storyboard.SetTarget(fade, HudBar);
                Storyboard.SetTargetProperty(fade, "Opacity");
                hudFade.Children.Add(fade);
                hudFade.Begin();
            };
            MainChrome.AddHandler(PointerMovedEvent, new PointerEventHandler((s, e) => RevealHud()), true);
            MainChrome.AddHandler(PointerPressedEvent, new PointerEventHandler((s, e) => RevealHud()), true);
            HudBar.SizeChanged += (s, e) => HudRevealZone.Width = e.NewSize.Width;
            HudRevealZone.PointerEntered += (s, e) => RevealHud();
            HudRevealZone.PointerMoved += (s, e) => RevealHud();
            HudRevealZone.PointerPressed += (s, e) => { RevealHud(); e.Handled = true; };
            HudBar.PointerEntered += (s, e) => { hudPointerOver = true; RevealHud(); };
            HudBar.PointerExited += (s, e) => { hudPointerOver = false; RevealHud(); };
            HudBar.GotFocus += (s, e) => RevealHud();
            HudBar.LostFocus += (s, e) => RevealHud();
            ZoomInputFlyout.Opened += (s, e) => { hudFlyoutOpen = true; RevealHud(); };
            ZoomInputFlyout.Closed += (s, e) => { hudFlyoutOpen = false; RevealHud(); };
            Loaded += (s, e) => RevealHud();
            Unloaded += (s, e) => { hudIdleTimer.Stop(); if (hudFade != null) hudFade.Stop(); };
        }

        private bool HasHudFocus()
        {
            var focused = FocusManager.GetFocusedElement() as DependencyObject;
            while (focused != null)
            {
                if (focused == HudBar) return true;
                focused = VisualTreeHelper.GetParent(focused);
            }
            return false;
        }

        private void RevealHud()
        {
            if (hudFade != null) { hudFade.Stop(); hudFade = null; }
            HudBar.Opacity = 1;
            hudIdleTimer.Stop();
            if (HudBar.Visibility == Visibility.Visible) hudIdleTimer.Start();
        }

        private void PenSizePreset_Click(object sender, RoutedEventArgs e)
        {
            PenSizeSlider.Value = double.Parse((string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        }

        private void HighlighterSizePreset_Click(object sender, RoutedEventArgs e)
        {
            HighlighterSizeSlider.Value = double.Parse((string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        }

        private void CloseThumbnails_Click(object sender, RoutedEventArgs e)
        {
            ThumbsToggle.IsChecked = false;
            ThumbsToggle_Click(ThumbsToggle, e);
        }

        private void InitializePreviewWindow()
        {
            var appView = ApplicationView.GetForCurrentView();
            appView.SetPreferredMinSize(new Size(500, 320));
            appView.TitleBar.BackgroundColor = Color.FromArgb(255, 246, 246, 247);
            appView.TitleBar.InactiveBackgroundColor = Color.FromArgb(255, 246, 246, 247);
            appView.TitleBar.ForegroundColor = Colors.Black;
            appView.TitleBar.InactiveForegroundColor = Colors.Gray;
            appView.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appView.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            appView.TitleBar.ButtonForegroundColor = Colors.Black;
            appView.TitleBar.ButtonInactiveForegroundColor = Colors.Gray;
            // Keep the system title row available for dragging at every window width.
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = false;
            Window.Current.SetTitleBar(null);
            appView.Title = "uwPDF";

            DocumentScroller.DirectManipulationStarted += (s, e) =>
            {
                pendingLayoutAnchor = null;
                scrollActivity.BeginGesture();
            };
            DocumentScroller.DirectManipulationCompleted += (s, e) =>
            {
                scrollActivity.EndGesture();
                WakeRenderer();
            };
            DocumentScroller.AddHandler(PointerPressedEvent,
                new PointerEventHandler((s, e) => pendingLayoutAnchor = null), true);
            DocumentScroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((s, e) =>
            {
                pendingLayoutAnchor = null;
                scrollActivity.NoteWheel(DateTime.UtcNow);
                if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) fitPageToViewport = false;
            }), true);

            Window.Current.SizeChanged += (s, e) =>
            {
                if (previewSizeInitialized && !applyingPreviewSize) userSized = true;
                ScheduleRelayout();
            };
            Window.Current.Closed += (s, e) =>
            {
                userSized = false;
                previewSizeInitialized = false;
            };
        }

        private async Task ApplyPreviewWindowSizeAsync(ulong token, bool hasIntrinsicSize)
        {
            if (token != activeRenderToken || userSized) return;
            // A manual resize while the work-area query is pending must win.
            previewSizeInitialized = true;
            try
            {
                var workArea = await ReadWorkAreaAsync();
                if (token != activeRenderToken || userSized) return;
                var size = PreviewSizing.Calculate(hasIntrinsicSize ? firstPageSize.Width : 0,
                    hasIntrinsicSize ? firstPageSize.Height : 0, thumbsRequested, workArea.Width, workArea.Height);
                applyingPreviewSizeToken = token;
                applyingPreviewSize = true;
                try
                {
                    var appView = ApplicationView.GetForCurrentView();
                    // Respect system-managed layouts (snap, tablet, full screen); the
                    // OS may reject a resize. Page fit still uses the actual viewport.
                    appView.TryResizeView(new Size(Math.Max(500, size.Width), Math.Max(320, size.Height)));
                    // Size events are delivered asynchronously after TryResizeView.
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                    await Task.Delay(150);
                }
                finally
                {
                    // An older file's delayed continuation cannot clear a newer
                    // request's suppression of automatic resize events.
                    if (applyingPreviewSizeToken == token) applyingPreviewSize = false;
                }
            }
            catch (Exception) { /* Window sizing failure must not reject a readable PDF. */ }
        }

        private async Task<Size> ReadWorkAreaAsync()
        {
            var nativeArea = PreviewDisplay.TryGetWorkArea(ApplicationView.GetForCurrentView());
            if (nativeArea.Width > 0 && nativeArea.Height > 0) return nativeArea;
            // Share only an in-flight query. Each later query reads the current
            // monitor again, including when monitors have the same resolution.
            if (pendingWorkAreaRead == null || pendingWorkAreaRead.IsCompleted)
                pendingWorkAreaRead = ProbeLegacyWorkAreaAsync();
            return await pendingWorkAreaRead;
        }

        private async Task<Size> ProbeLegacyWorkAreaAsync()
        {
            // EdgeHTML supplies taskbar-excluding logical dimensions on 14393,
            // where ApplicationView.GetDisplayRegions is not available.
            var host = (Grid)Content;
            var probe = new WebView(WebViewExecutionMode.SeparateThread)
                { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false };
            var ready = new TaskCompletionSource<bool>();
            TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> navigationCompleted =
                (s, e) => ready.TrySetResult(e.IsSuccess);
            probe.NavigationCompleted += navigationCompleted;
            host.Children.Add(probe);
            try
            {
                probe.NavigateToString("<html><head><script>function workArea(){return JSON.stringify({w:screen.availWidth,h:screen.availHeight});}</script></head><body></body></html>");
                if (await Task.WhenAny(ready.Task, Task.Delay(2000)) == ready.Task && await ready.Task)
                {
                    var result = JsonObject.Parse(await probe.InvokeScriptAsync("workArea", new string[0]));
                    var width = result.GetNamedNumber("w"); var height = result.GetNamedNumber("h");
                    if (width > 0 && height > 0) return new Size(width, height);
                }
            }
            catch (Exception) { }
            finally
            {
                probe.NavigationCompleted -= navigationCompleted;
                try { probe.NavigateToString("<html></html>"); }
                catch (Exception) { }
                finally { host.Children.Remove(probe); }
            }
            // If screen probing is unavailable, stay inside the current view.
            var bounds = Window.Current.Bounds;
            return new Size(bounds.Width + PreviewSizing.ScreenMargin * 2, bounds.Height + PreviewSizing.ScreenMargin * 2);
        }

        private float FitZoom(PageView view, bool updateLimits = true)
        {
            var width = DocumentScroller.ViewportWidth;
            var height = DocumentScroller.ViewportHeight;
            if (width < 1) width = Math.Max(1, DocumentScroller.ActualWidth);
            if (height < 1) height = Math.Max(1, DocumentScroller.ActualHeight);
            var zoom = (float)Math.Max(0.01, Math.Min(1, Math.Min(width / (view.LayoutWidth + 2 * PageBorderThickness),
                height / (view.LayoutHeight + 2 * PageBorderThickness))));
            if (updateLimits)
            {
                DocumentScroller.MinZoomFactor = Math.Min(0.25f, zoom);
                isUpdatingZoomSlider = true;
                try { ZoomSlider.Minimum = DocumentScroller.MinZoomFactor * 100; }
                finally { isUpdatingZoomSlider = false; }
            }
            return zoom;
        }

        private void AnnotationToolsButton_Click(object sender, RoutedEventArgs e)
        {
            bool opening = AnnotationToolbar.Visibility != Visibility.Visible;
            AnnotationToolbar.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
            AnnotationToolsButton.Background = new SolidColorBrush(opening ? Color.FromArgb(255, 220, 235, 250) : Colors.Transparent);
            ToolTipService.SetToolTip(AnnotationToolsButton, opening ? "收起批注工具，返回文字选择" : "展开批注工具");
            if (!opening) SetInkTool(InkTool.None);
        }
    }
}
