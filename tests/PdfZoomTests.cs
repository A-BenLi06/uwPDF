using System;
using System.Collections.Generic;

namespace Windows.UI.Xaml.Controls
{
    public sealed class ScrollViewerViewChangedEventArgs { public bool IsIntermediate; }
    public sealed class ScrollViewerView { public float ZoomFactor; }
    public sealed class ScrollViewerViewChangingEventArgs { public ScrollViewerView NextView, FinalView; }
}

namespace LitePdfViewer
{
    public sealed class ZoomTimer { public bool Running; public void Stop() { Running=false; } }
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
        private object selectedTextPage;
        private object selectedInkPage;
        private uint? inkGesturePointer;
        private void DrawInkSelection(object view) { }
        private uint? selectionPointer;
        private void RedrawSelectionPages() { }
        private bool fitPageToViewport;
        private float? programmaticZoomTarget, pendingSliderZoom;
        private ZoomTimer sliderZoomTimer = new ZoomTimer();
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
        private void EvictFarPages() { }
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
            SetZoom(.9f, false);
            Check(pendingLayoutAnchor == null && !fitPageToViewport,
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
            // A queued slider intent must not override a newer zoom button/fit.
            pendingSliderZoom=.8f; sliderZoomTimer.Running=true;
            SetZoom(1.25f,true);
            Check(pendingSliderZoom==null && !sliderZoomTimer.Running,"Stale slider work remained queued after manual zoom");
            // Precision touchpad zoom does not raise PointerPressed and may
            // publish its final view after DirectManipulationCompleted.
            programmaticZoomTarget=null; fitPageToViewport=true;
            DocumentScroller_ViewChanging(null,new Windows.UI.Xaml.Controls.ScrollViewerViewChangingEventArgs {
                NextView=new Windows.UI.Xaml.Controls.ScrollViewerView { ZoomFactor=1.3f },
                FinalView=new Windows.UI.Xaml.Controls.ScrollViewerView { ZoomFactor=1.5f } });
            Check(!fitPageToViewport,"Touchpad zoom without a pointer event retained fit mode");
            programmaticZoomTarget=null; fitPageToViewport=true;
            DocumentScroller.ZoomFactor=1.6f;
            DocumentScroller_ViewChanged(null,new Windows.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs { IsIntermediate=false });
            Check(!fitPageToViewport,"Coalesced final gesture view triggered fit snap-back");
            fitPageToViewport=true; programmaticZoomTarget=.8f; lastObservedZoom=1.6f;
            DocumentScroller_ViewChanging(null,new Windows.UI.Xaml.Controls.ScrollViewerViewChangingEventArgs {
                NextView=new Windows.UI.Xaml.Controls.ScrollViewerView { ZoomFactor=1.4f },
                FinalView=new Windows.UI.Xaml.Controls.ScrollViewerView { ZoomFactor=.8f } });
            Check(fitPageToViewport,"A tracked programmatic fit was mistaken for user zoom");
            DocumentScroller.ZoomFactor=.8f;
            DocumentScroller_ViewChanged(null,new Windows.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs { IsIntermediate=false });
            Check(fitPageToViewport && programmaticZoomTarget==null,"Completed fit lost mode or retained its request marker");
            fitPageToViewport=true; programmaticZoomTarget=.8f;
            DocumentScroller_ViewChanging(null,new Windows.UI.Xaml.Controls.ScrollViewerViewChangingEventArgs {
                NextView=new Windows.UI.Xaml.Controls.ScrollViewerView { ZoomFactor=.9f },
                FinalView=new Windows.UI.Xaml.Controls.ScrollViewerView { ZoomFactor=1.1f } });
            Check(!fitPageToViewport && programmaticZoomTarget==null,"Native gesture failed to override an in-flight fit");
            // Retargeting to the currently displayed intermediate value must
            // cancel the old goal rather than treating it as an idle no-op.
            DocumentScroller.ZoomFactor=1; programmaticZoomTarget=2; count=DocumentScroller.Requests;
            SetZoom(1,false);
            Check(DocumentScroller.Requests==count+1 && programmaticZoomTarget==1,"Retarget at current zoom left old animation running");

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
            Console.WriteLine("PASS: production zoom preserves the content extent and reading anchor, submits one view change, honors zoom limits, rejects stale slider work and distinguishes touchpad zoom from programmatic fit.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
