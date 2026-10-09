using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LitePdfViewer;

namespace Windows.System
{
    public enum AppMemoryUsageLevel { Low, Medium, High, OverLimit }
    public sealed class AppMemoryUsageLimitChangingEventArgs : EventArgs { public ulong NewLimit; }
    public static class MemoryManager
    {
        public static ulong AppMemoryUsage, AppMemoryUsageLimit;
        public static AppMemoryUsageLevel AppMemoryUsageLevel;
        public static event EventHandler<object> AppMemoryUsageIncreased, AppMemoryUsageDecreased;
        public static event EventHandler<AppMemoryUsageLimitChangingEventArgs> AppMemoryUsageLimitChanging;
        public static void Increased() { if (AppMemoryUsageIncreased != null) AppMemoryUsageIncreased(null, null); }
        public static void Decreased() { if (AppMemoryUsageDecreased != null) AppMemoryUsageDecreased(null, null); }
        public static void LimitChanging(ulong limit) { if (AppMemoryUsageLimitChanging != null) AppMemoryUsageLimitChanging(null, new AppMemoryUsageLimitChangingEventArgs { NewLimit = limit }); }
    }
}
namespace Windows.UI.Core
{
    public enum CoreDispatcherPriority { Normal }
    public sealed class CoreDispatcher
    {
        readonly Queue<Action> work = new Queue<Action>();
        public int Pending { get { return work.Count; } }
        public Task RunAsync(CoreDispatcherPriority priority, Action action)
        {
            var done = new TaskCompletionSource<bool>();
            work.Enqueue(() => { try { action(); done.SetResult(true); } catch (Exception ex) { done.SetException(ex); } });
            return done.Task;
        }
        public void Drain() { while (work.Count != 0) work.Dequeue()(); }
    }
}
namespace Windows.UI.Xaml
{
    public sealed class Window
    {
        public static readonly Window Current = new Window();
        public bool Visible = true;
    }
}
namespace Windows.UI.Xaml.Media.Imaging { public sealed class SurfaceImageSource { } }
namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        const long MiB = 1024 * 1024;
        sealed class Raster : IDisposable { public object Source = new object(); public int Releases; public void Dispose() { Releases++; } }
        sealed class Strokes { public int Count = 1; public void Clear() { Count = 0; } }
        sealed class PageView : IPdfRenderState
        {
            public int Index;
            public bool RenderFailed { get; set; }
            public bool IsRendered { get; set; }
            public bool DetailFailed, TextFailed = true, IsRealized, Dirty, IsLoadingInk, InkLoaded = true, HighlightsLoaded = true, NeedsRefinement;
            public long BitmapBytes;
            public Raster Raster, DetailRaster;
            public Strokes Strokes = new Strokes();
            public List<int> Highlights = new List<int> { 1 };
            public object Source { get { return Raster == null ? null : Raster.Source; } }
        }
        sealed class ThumbnailItem { public Raster Raster = new Raster(); public bool IsRendered = true, RenderFailed = true; }
        readonly List<PageView> pageViews = new List<PageView>();
        readonly HashSet<int> cachedPages = new HashSet<int>();
        readonly List<int> realizedPages = new List<int>();
        readonly Dictionary<int, ThumbnailItem> thumbnailItems = new Dictionary<int, ThumbnailItem>();
        readonly PdfRasterRetirement retiredRasters = new PdfRasterRetirement(32 * MiB, 4);
        readonly Windows.UI.Core.CoreDispatcher Dispatcher = new Windows.UI.Core.CoreDispatcher();
        long cachedBitmapBytes;
        uint pageIndex = 1;
        int wakes;
        const int RenderWindow = 2;
        bool scrollSettled = true;
        Func<PageView, bool> needsVisibleRefinement;
        sealed class ScrollActivity { public int Direction = 1; }
        readonly ScrollActivity scrollActivity = new ScrollActivity();
        bool IsScrollSettled() { return scrollSettled; }
        bool IsZoomSettled() { return true; }
        bool VisiblePageNeedsRefinement(PageView view) { return view.NeedsRefinement; }
        bool PageNeedsWork(PageView view, bool settled) { return !view.RenderFailed && (!view.IsRendered || (settled && view.NeedsRefinement)); }
        void GetVisiblePageRange(out int first, out int last) { first = last = 1; }
        void WakeRenderer() { wakes++; }
        void ReleaseRetiredRenderSurfaces() { retiredRasters.Clear(); }
        void ReleasePageDetail(PageView view)
        {
            if (view.DetailRaster == null) return;
            cachedBitmapBytes -= MiB; view.BitmapBytes -= MiB;
            view.DetailRaster.Dispose(); view.DetailRaster = null;
        }
        void ReleasePageBitmap(PageView view)
        {
            ReleasePageDetail(view);
            cachedBitmapBytes -= view.BitmapBytes; view.BitmapBytes = 0;
            if (view.Raster != null) view.Raster.Dispose();
            view.Raster = null; cachedPages.Remove(view.Index);
        }
        void VirtualizePage(PageView view) { view.IsRealized = false; realizedPages.Remove(view.Index); ReleaseCleanAnnotations(view); }
        static void ReleaseThumbnail(ThumbnailItem item)
        {
            if (item.Raster != null) item.Raster.Dispose();
            item.Raster = null; item.IsRendered = false;
        }
        void Populate()
        {
            for (var i = 0; i < 4; i++)
            {
                var view = new PageView { Index = i, IsRealized = true, Dirty = i == 0, IsLoadingInk = i == 3, RenderFailed = true, DetailFailed = true };
                if (i != 3)
                {
                    view.Raster = new Raster(); view.BitmapBytes = 12 * MiB; view.IsRendered = true;
                    cachedBitmapBytes += view.BitmapBytes; cachedPages.Add(i);
                }
                realizedPages.Add(i); pageViews.Add(view);
            }
            thumbnailItems.Add(1, new ThumbnailItem());
        }
        public static void CheckController()
        {
            var page = new MainPage(); page.Populate();
            var foreground = page.pageViews[1].Raster;
            var dirtyStrokes = page.pageViews[0].Strokes;
            var pendingInkStrokes = page.pageViews[3].Strokes;
            var retired = new Raster(); page.retiredRasters.Retire(retired, 8 * MiB);
            page.StartMemoryMonitoring(); page.StartMemoryMonitoring();
            Require(page.Dispatcher.Pending == 1, "Repeated load scheduled redundant memory work.");
            Windows.System.MemoryManager.LimitChanging(64 * (ulong)MiB);
            Windows.System.MemoryManager.Increased();
            Require(page.Dispatcher.Pending == 1, "Memory event burst was not coalesced.");
            page.Dispatcher.Drain();
            Require(page.activeBitmapCacheBudget == 4 * MiB && page.memoryConstrained, "LimitChanging ignored NewLimit before the OS property caught up.");
            Require(page.pageViews[1].Raster == foreground && foreground.Releases == 0,
                "Pressure blanked the foreground before its smaller replacement was ready.");
            Require(page.pageViews[0].Raster == null && page.pageViews[2].Raster == null && retired.Releases == 1,
                "Pressure retained offscreen or retired images.");
            Require(page.realizedPages.Count == 1 && page.realizedPages[0] == 1 && !page.thumbnailItems[1].IsRendered,
                "Pressure retained offscreen presenters or claimed an evicted thumbnail was rendered.");
            Require(page.pageViews[0].Strokes == dirtyStrokes && dirtyStrokes.Count == 1 && page.pageViews[0].Highlights.Count == 1,
                "Pressure discarded unsaved strokes or highlights.");
            Require(page.pageViews[2].Strokes == null && page.pageViews[2].Highlights.Count == 0 && !page.pageViews[2].InkLoaded,
                "Clean offscreen annotations were not released for reload.");
            Require(page.pageViews[3].Strokes == pendingInkStrokes, "Pressure discarded an annotation load in progress.");

            Windows.System.MemoryManager.AppMemoryUsage = 10 * (ulong)MiB;
            Windows.System.MemoryManager.AppMemoryUsageLimit = 64 * (ulong)MiB;
            Windows.System.MemoryManager.Decreased(); page.Dispatcher.Drain();
            Require(page.activeBitmapCacheBudget == 8 * MiB && !page.memoryConstrained, "Low pressure failed to resume rendering within the device limit.");
            foreach (var view in page.pageViews)
                Require(!view.RenderFailed && !view.DetailFailed && !view.TextFailed, "A transient draw/text failure stayed skipped after memory pressure recovered.");
            Require(!page.thumbnailItems[1].RenderFailed, "A thumbnail failure stayed skipped after memory pressure recovered.");
            page.pageViews[3].RenderFailed = true;
            Windows.System.MemoryManager.Decreased(); page.Dispatcher.Drain();
            Require(page.pageViews[3].RenderFailed, "Repeated Low notifications retried a persistently failing page without a new recovery transition.");
            Windows.System.MemoryManager.LimitChanging(512 * (ulong)MiB);
            Windows.System.MemoryManager.Decreased(); page.Dispatcher.Drain();
            Require(page.activeBitmapCacheBudget == 64 * MiB, "A later limit change was lost in coalesced events.");

            // The window can unload/reload before a queued dispatcher callback runs.
            Windows.System.MemoryManager.Increased(); page.StopMemoryMonitoring();
            Windows.System.MemoryManager.AppMemoryUsageLimit = 512 * (ulong)MiB;
            page.StartMemoryMonitoring(); page.Dispatcher.Drain();
            Require(page.activeBitmapCacheBudget == 64 * MiB && page.Dispatcher.Pending == 0,
                "Stale memory callback corrupted a reloaded page.");
            page.StopMemoryMonitoring(); Windows.System.MemoryManager.Increased();
            Require(page.Dispatcher.Pending == 0, "Unloaded page retained a static memory event subscription.");

            page.ApplyMemorySnapshot(490 * (ulong)MiB, 512 * (ulong)MiB, PdfMemoryPressure.High);
            Require(page.pageViews[1].Raster == foreground, "Foreground was cleared by a later pressure event.");
            Windows.UI.Xaml.Window.Current.Visible = false;
            page.ApplyMemorySnapshot(490 * (ulong)MiB, 512 * (ulong)MiB, PdfMemoryPressure.High);
            Require(foreground.Releases == 1 && page.cachedBitmapBytes == 0 && page.realizedPages.Count == 0,
                "Hidden pressured window retained page surfaces/presenters.");
            Windows.UI.Xaml.Window.Current.Visible = true;
        }
        public static void CheckRecovery()
        {
            var page = new MainPage(); page.Populate();
            page.pageViews[0].Raster.Source = new Windows.UI.Xaml.Media.Imaging.SurfaceImageSource();
            var software = page.pageViews[1].Raster;
            page.pageViews[1].DetailRaster = new Raster { Source = new Windows.UI.Xaml.Media.Imaging.SurfaceImageSource() };
            page.pageViews[1].BitmapBytes += MiB; page.cachedBitmapBytes += MiB;
            page.ClearLostRenderSurfaces();
            Require(page.pageViews[0].Raster == null, "Lost native page surface was retained.");
            Require(page.pageViews[1].Raster == software && software.Releases == 0 && page.pageViews[1].DetailRaster == null,
                "Recovery discarded a valid software base or retained a lost native detail.");
            foreach (var view in page.pageViews)
                Require(!view.RenderFailed && !view.DetailFailed, "An uncached first-draw failure stayed permanently skipped after recovery.");
            Require(!page.thumbnailItems[1].RenderFailed && !page.thumbnailItems[1].IsRendered && page.wakes == 1,
                "Recovery did not reset thumbnails/wake the scheduler.");
            page.ClearLostRenderSurfaces();
            Require(software.Releases == 0, "Repeated recovery disposed the surviving software raster.");
        }
        public static void CheckTrim()
        {
            var page = new MainPage(); page.Populate();
            var visible = page.pageViews[1].Raster;
            page.activeBitmapCacheBudget = 20 * MiB;
            page.TrimBitmapCache(null, 0);
            Require(page.cachedBitmapBytes == 12 * MiB && page.pageViews[1].Raster == visible && visible.Releases == 0,
                "Budget-only trimming failed to evict offscreen pages while preserving the visible one.");
            page.TrimBitmapCache(page.pageViews[1], 24 * MiB);
            Require(visible.Releases == 0, "Incoming replacement caused a temporary blank foreground.");
        }
        public static void CheckScheduling()
        {
            var page = new MainPage(); page.Populate();
            foreach (var view in page.pageViews) { view.RenderFailed = false; view.IsRendered = false; }
            page.memoryConstrained = true; page.scrollSettled = false;
            Require(page.FindNextRenderIndex() == 1, "Memory pressure prevented a missing visible page from drawing during a gesture.");
            page.pageViews[1].IsRendered = true;
            Require(page.FindNextRenderIndex() == -1, "Pressure selected offscreen directional prefetch.");
            page.scrollSettled = true;
            Require(page.FindNextRenderIndex() == -1, "Pressure selected settled offscreen prefetch.");
            page.pageViews[1].NeedsRefinement = true;
            Require(page.FindNextRenderIndex() == 1, "Pressure prevented a visible resolution change using the smaller budget.");
            page.pageViews[1].NeedsRefinement = false; page.memoryConstrained = false; page.scrollSettled = false;
            Require(page.FindNextRenderIndex() == 2, "Normal memory did not resume directional prefetch.");
            page.scrollSettled = true;
            Require(page.FindNextRenderIndex() == 0, "Normal memory did not resume settled prefetch.");
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
class PdfMemoryRecoveryTests
{
    const ulong MiB = 1024 * 1024;
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var policy = new PdfMemoryBudget();
        policy.Update(100 * MiB, 1024 * MiB, PdfMemoryPressure.Low);
        Require(policy.Bytes == 64 * (long)MiB && !policy.Constrained, "Normal desktop budget changed unexpectedly.");
        policy.Update(100 * MiB, 256 * MiB, PdfMemoryPressure.Low);
        Require(policy.Bytes == 32 * (long)MiB, "Small-memory device was given the desktop budget.");
        policy.Update(940 * MiB, 1024 * MiB, PdfMemoryPressure.High);
        Require(policy.Bytes == 8 * (long)MiB && policy.Constrained, "High pressure did not constrain work.");
        policy.Update(750 * MiB, 1024 * MiB, PdfMemoryPressure.Medium);
        Require(policy.Bytes == 8 * (long)MiB && policy.Constrained, "Eviction immediately refilled the cache at Medium pressure.");
        policy.Update(750 * MiB, 1024 * MiB, PdfMemoryPressure.Low);
        Require(policy.Constrained, "Insufficient headroom bypassed pressure hysteresis.");
        policy.Update(500 * MiB, 1024 * MiB, PdfMemoryPressure.Low);
        Require(policy.Bytes == 64 * (long)MiB && !policy.Constrained, "Safely low usage did not recover the normal budget.");
        policy.Update(500 * MiB, 1024 * MiB, PdfMemoryPressure.Medium);
        Require(policy.Bytes == 32 * (long)MiB, "Medium pressure did not reduce the normal budget.");
        policy.Update(ulong.MaxValue, ulong.MaxValue, PdfMemoryPressure.Low);
        Require(policy.Bytes == 4 * (long)MiB && policy.Constrained, "UInt64 usage/limit overflow defeated pressure handling.");
        policy.Update(2 * MiB, MiB, PdfMemoryPressure.Low);
        Require(policy.Bytes == (long)MiB, "Critical low limit removed the readable foreground budget.");
        policy.Update(0, 0, PdfMemoryPressure.Low);
        Require(policy.Bytes == 64 * (long)MiB && !policy.Constrained, "Unavailable zero limit was treated as zero memory.");
        Windows.System.MemoryManager.AppMemoryUsage = 100 * MiB;
        Windows.System.MemoryManager.AppMemoryUsageLimit = 512 * MiB;
        Windows.System.MemoryManager.AppMemoryUsageLevel = Windows.System.AppMemoryUsageLevel.Low;
        LitePdfViewer.MainPage.CheckController();
        LitePdfViewer.MainPage.CheckRecovery();
        LitePdfViewer.MainPage.CheckTrim();
        LitePdfViewer.MainPage.CheckScheduling();
        Console.WriteLine("PASS: production memory policy/events, coalesced NewLimit, unload/reload, foreground/dirty preservation, cache eviction and uncached rendering failure recovery");
    }
}
