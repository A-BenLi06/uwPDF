using System;
using System.Collections.Generic;

namespace Windows.UI.Xaml.Controls
{
    public sealed class ScrollViewerViewChangedEventArgs { public bool IsIntermediate; }
}

namespace LitePdfViewer
{
    public sealed class ZoomFrame { public double Width = double.NaN; }
    public sealed class ZoomScroller
    {
        public float ZoomFactor = 1, MinZoomFactor = .25f, MaxZoomFactor = 4;
        public double ViewportWidth = 800, ViewportHeight = 600, VerticalOffset = 500, HorizontalOffset;
        public int Requests;
        public bool AnimationDisabled;
        public Action OnFrame;
        public bool ChangeView(double? x, double? y, float? zoom, bool disableAnimation)
        {
            Requests++;
            AnimationDisabled = disableAnimation;
            var initial = ZoomFactor;
            var target = zoom ?? initial;
            for (int i = 1; i <= 20; i++)
            {
                ZoomFactor = initial + (target - initial) * i / 20;
                if (OnFrame != null) OnFrame();
            }
            if (x.HasValue) HorizontalOffset = x.Value;
            if (y.HasValue) VerticalOffset = y.Value;
            return true;
        }
    }
    public sealed partial class MainPage
    {
        private readonly List<object> pageViews = new List<object> { new object() };
        private readonly ZoomScroller DocumentScroller = new ZoomScroller();
        private readonly ZoomFrame PageFrame = new ZoomFrame();
        private double lastColumnWidth = 612;
        private const double PageBorderThickness = 1;
        private object pendingLayoutAnchor;
        private bool fitPageToViewport;
        private bool pointerZoomGesture;
        private int changing;
        private float lastObservedZoom = 1;
        private readonly object document = new object();
        private readonly PdfScrollActivity scrollActivity = new PdfScrollActivity();
        private void MarkZoomChanging() { changing++; }
        private void UpdateCurrentPageFromScroll() { }
        private void RestoreVisiblePages() { }
        private void UpdateZoomControls() { }
        private void UpdateSearchFocus() { }
        private void FollowCurrentThumbnail() { }
        private object GetFitReferencePage() { return null; }
        private float FitZoom(object reference, bool updateLimits) { return .5f; }
        private void ScheduleRelayout() { }
        private void WakeRenderer() { }

        public void CheckZooms()
        {
            UpdatePageCentering();
            Check(PageFrame.Width == 614, "Natural width was not initialized from Auto/NaN.");
            pendingLayoutAnchor = new object();
            fitPageToViewport = true;
            pointerZoomGesture = true;
            SetZoom(.9f, false);
            Check(pendingLayoutAnchor == null && !fitPageToViewport && !pointerZoomGesture,
                "Manual zoom kept a stale layout anchor, fit mode, or pointer gesture.");
            DocumentScroller.OnFrame = () =>
            {
                UpdatePageCentering();
                Check(PageFrame.Width == 614, "An intermediate zoom changed the content extent.");
            };
            foreach (var width in new[] { 800d, 400d })
            {
                DocumentScroller.ViewportWidth = width;
                SetZoom(1, false);
                foreach (var target in new[] { .8f, .64f, .512f, .25f, .8f, 1.25f, .8f })
                {
                    var requests = DocumentScroller.Requests;
                    var expectedCenter = (DocumentScroller.VerticalOffset + 300) / DocumentScroller.ZoomFactor;
                    SetZoom(target, true);
                    Check(DocumentScroller.Requests == requests + 1, "Centering issued an extra ChangeView during animation.");
                    Check(Math.Abs(DocumentScroller.ZoomFactor - target) < .0001, "Wrong zoom target.");
                    Check(!DocumentScroller.AnimationDisabled, "Animated zoom was disabled.");
                    Check(Math.Abs(DocumentScroller.HorizontalOffset - Math.Max(0, (614 * target - width) / 2)) < .001,
                        "Horizontal target no longer centers the page.");
                    Check(Math.Abs(DocumentScroller.VerticalOffset - Math.Max(0, expectedCenter * target - 300)) < .01,
                        "Zoom lost the reading anchor.");
                }
            }
            SetZoom(0, false);
            Check(DocumentScroller.ZoomFactor == .25f && DocumentScroller.AnimationDisabled, "Minimum zoom/slider behavior changed.");
            SetZoom(100, true);
            Check(DocumentScroller.ZoomFactor == 4, "Maximum zoom changed.");
            int count = DocumentScroller.Requests;
            SetZoom(4, true);
            Check(DocumentScroller.Requests == count, "Unchanged zoom restarted animation.");
            Check(changing > 0, "Zoom did not notify the render scheduler.");
            // DirectManipulationStarted is also raised by native programmatic
            // animation. Fit should only end when actual pointer zoom occurs.
            scrollActivity.BeginGesture();
            fitPageToViewport = true;
            pointerZoomGesture = false;
            DocumentScroller.ZoomFactor = .9f;
            DocumentScroller_ViewChanged(null, new Windows.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs { IsIntermediate = true });
            Check(fitPageToViewport, "A programmatic native zoom cancelled fit mode.");
            pointerZoomGesture = true;
            DocumentScroller.ZoomFactor = .8f;
            DocumentScroller_ViewChanged(null, new Windows.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs { IsIntermediate = true });
            Check(!fitPageToViewport, "A pointer zoom failed to leave fit mode.");
            scrollActivity.EndGesture();
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
internal static class PdfZoomTests
{
    public static int Main()
    {
        try
        {
            new LitePdfViewer.MainPage().CheckZooms();
            Console.WriteLine("PASS: production zoom preserves the content extent and reading anchor, submits one view change, honors zoom limits and slider animation mode.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
