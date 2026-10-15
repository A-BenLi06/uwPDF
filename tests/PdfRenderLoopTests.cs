using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Platform/work doubles. The worker method is extracted from current MainPage
// source at test time; none of its scheduling decisions are copied here.
namespace Windows.UI.Xaml
{
    sealed class Window
    {
        public static readonly Window Current = new Window();
        public bool Visible = true;
    }
}
namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        sealed class PageView { public bool RenderFailed, DetailFailed; }
        readonly List<PageView> pageViews = new List<PageView> { new PageView() };
        object document = new object();
        ulong activeRenderToken = 1;
        DateTime thumbnailSettledAt = DateTime.MinValue;
        TaskCompletionSource<bool> wake;
        public bool ScrollSettled = true, ZoomSettled, MissingPage, ThumbnailPending = true, TextPending = true;
        public int PageDraws, ThumbnailSelections, ThumbnailDraws, TextStarts, WaitCalls, WaitMilliseconds;
        bool IsScrollSettled() { return ScrollSettled; }
        bool IsZoomSettled() { return ZoomSettled; }
        int FindNextRenderIndex() { return MissingPage && !pageViews[0].RenderFailed ? 0 : -1; }
        Task RenderPageCoreAsync(uint index, ulong token) { PageDraws++; MissingPage = false; return Task.FromResult(true); }
        PageView FindNextDetailPage() { return null; }
        Task RenderPageDetailAsync(PageView page, ulong token) { return Task.FromResult(true); }
        int FindNextThumbnailIndex() { ThumbnailSelections++; return ThumbnailPending ? 0 : -1; }
        Task RenderThumbnailCoreAsync(uint index, ulong token)
        {
            ThumbnailDraws++;
            // Rejected work is synchronous, as in the real execution guard.
            // Bound a regressed busy loop so a failing test cannot hang forever.
            if (!ZoomSettled || !ScrollSettled)
            {
                if (ThumbnailDraws >= 3) throw new Exception("Rejected thumbnail busy loop");
                return Task.FromResult(true);
            }
            ThumbnailPending = false;
            return Task.FromResult(true);
        }
        PageView FindNextTextPage() { return TextPending ? pageViews[0] : null; }
        Task RunTextWorkAsync(PageView page, ulong token) { TextStarts++; TextPending = false; return Task.FromResult(true); }
        Task WaitForRenderWorkAsync(int milliseconds = 0)
        {
            WaitCalls++; WaitMilliseconds = milliseconds;
            wake = new TaskCompletionSource<bool>();
            return wake.Task;
        }
        public Task Start() { return RunRenderLoopAsync(activeRenderToken); }
        public void Wake() { wake.SetResult(true); }
        public void Stop() { activeRenderToken++; Wake(); }
    }
}
class PdfRenderLoopTests
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void CheckGesture(bool scrolling)
    {
        var page = new LitePdfViewer.MainPage { MissingPage = true, ScrollSettled = !scrolling, ZoomSettled = scrolling };
        var loop = page.Start();
        Require(!loop.IsCompleted && page.PageDraws == 1, "Gesture prevented a missing visible page from rendering.");
        Require(page.ThumbnailSelections == 0 && page.ThumbnailDraws == 0 && page.TextStarts == 0,
            "Unsettled input selected rejected background work or started text extraction.");
        Require(page.WaitCalls == 1 && page.WaitMilliseconds == 120, "Unsettled worker did not yield while waiting for input to finish.");
        page.ScrollSettled = page.ZoomSettled = true; page.Wake();
        Require(page.ThumbnailDraws == 1 && page.TextStarts == 1 && page.WaitCalls == 2 && page.WaitMilliseconds == 0,
            "Background work did not resume and return to event-only idle waiting.");
        page.Stop(); loop.GetAwaiter().GetResult();
    }
    static void Main()
    {
        CheckGesture(false); CheckGesture(true);
        Windows.UI.Xaml.Window.Current.Visible = false;
        var page = new LitePdfViewer.MainPage { MissingPage = true, ZoomSettled = true };
        var loop = page.Start();
        Require(page.PageDraws == 0 && page.ThumbnailSelections == 0 && page.WaitMilliseconds == 0, "Hidden window performed speculative rendering.");
        page.Stop(); loop.GetAwaiter().GetResult();
        Console.WriteLine("PASS: current production render loop yields during zoom/scroll, fills missing pages, resumes background work and sleeps when hidden");
    }
}
