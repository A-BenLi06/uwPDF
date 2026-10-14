using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace Windows.UI.Xaml.Controls
{
    public sealed class ScrollViewerViewChangedEventArgs : EventArgs { public bool IsIntermediate; }
    public sealed class ScrollViewer
    {
        private EventHandler<ScrollViewerViewChangedEventArgs> changed;
        public int Listeners, Requests;
        public double VerticalOffset = 100, ScrollableHeight = 500, Target;
        public bool AnimationDisabled, Accepted = true;
        public event EventHandler<ScrollViewerViewChangedEventArgs> ViewChanged
        { add { changed += value; Listeners++; } remove { changed -= value; Listeners--; } }
        public bool ChangeView(double? x, double? y, float? z, bool disableAnimation)
        { Requests++; Target = y.Value; AnimationDisabled = disableAnimation; return Accepted; }
        public void Frame(double value, bool intermediate)
        { VerticalOffset = value; if (changed != null) changed(this, new ScrollViewerViewChangedEventArgs { IsIntermediate = intermediate }); }
    }
}
namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        public readonly ScrollViewer DocumentScroller = new ScrollViewer();
        public Task Scroll(double offset, CancellationToken cancellation) { return ScrollToSearchMatchAsync(offset, cancellation); }
    }
}
internal static class PdfSearchScrollTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static int Main()
    {
        var page = new LitePdfViewer.MainPage(); var viewer = page.DocumentScroller;
        var scrolling = page.Scroll(900, CancellationToken.None);
        Check(viewer.Requests == 1 && viewer.Target == 500 && !viewer.AnimationDisabled, "Search did not submit one clamped animated request.");
        viewer.Frame(250, true);
        Check(!scrolling.IsCompleted, "An intermediate frame completed navigation.");
        viewer.Frame(500, false); scrolling.GetAwaiter().GetResult();
        Check(viewer.Listeners == 0, "Completed scroll retained its event handler.");
        page.Scroll(900, CancellationToken.None).GetAwaiter().GetResult();
        Check(viewer.Requests == 1, "An equal clamped target restarted animation.");
        using (var cancellation = new CancellationTokenSource())
        {
            scrolling = page.Scroll(-40, cancellation.Token);
            Check(viewer.Target == 0, "Negative target was not clamped.");
            cancellation.Cancel();
            try { scrolling.GetAwaiter().GetResult(); throw new Exception("Cancelled navigation completed."); }
            catch (OperationCanceledException) { }
            Check(viewer.Listeners == 0, "Cancellation retained a listener.");
        }
        viewer.Accepted = false;
        page.Scroll(200, CancellationToken.None).GetAwaiter().GetResult();
        Check(viewer.Listeners == 0, "Rejected view request retained a listener.");
        Console.WriteLine("PASS: one native animated search request, bounds, intermediate/final completion, equal targets, cancellation and listener cleanup.");
        return 0;
    }
}
