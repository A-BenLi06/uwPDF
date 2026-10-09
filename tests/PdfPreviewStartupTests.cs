using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Data.Pdf;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace Windows.Foundation
{
    public struct Size { public double Width, Height; public Size(double width, double height) { Width = width; Height = height; } }
    public struct Rect { public double Width, Height; }
    public delegate void TypedEventHandler<T, U>(T sender, U args);
}
namespace Windows.UI.Core { public enum CoreDispatcherPriority { Low } }
namespace Windows.UI.Xaml { public enum Visibility { Visible, Collapsed } public enum FocusState { Programmatic } }
namespace Windows.Storage
{
    public sealed class StorageFile
    {
        public string Name;
        public bool LoadFails;
        public Size PageSize = new Size(800, 1066.6666667);
    }
}
namespace Windows.Data.Pdf
{
    public sealed class PdfDocument
    {
        public StorageFile File;
        public uint PageCount { get { return 2; } }
        public static Task<PdfDocument> LoadFromFileAsync(StorageFile file)
        {
            if (file.LoadFails) { var failed = new TaskCompletionSource<PdfDocument>(); failed.SetException(new Exception("invalid PDF")); return failed.Task; }
            return Task.FromResult(new PdfDocument { File = file });
        }
        public PdfPage GetPage(uint index) { return new PdfPage { Size = File.PageSize }; }
    }
    public sealed class PdfPage : IDisposable { public Size Size; public void Dispose() { } }
}
namespace Windows.UI.ViewManagement
{
    public sealed class ApplicationView
    {
        public static ApplicationView Current = new ApplicationView();
        public readonly List<Size> Resizes = new List<Size>();
        public bool ThrowOnResize;
        public static ApplicationView GetForCurrentView() { return Current; }
        public bool TryResizeView(Size size)
        {
            if (ThrowOnResize) throw new Exception("resize unavailable");
            Resizes.Add(size); return true;
        }
    }
}
namespace PdfNative
{
    public static class PreviewDisplay
    {
        public static Size Area;
        public static bool Throw;
        public static Size TryGetWorkArea(ApplicationView view) { if (Throw) throw new Exception("display unavailable"); return Area; }
    }
}
namespace Windows.UI.Xaml.Controls
{
    public enum WebViewExecutionMode { SeparateThread }
    public sealed class Window
    {
        public static Window Current = new Window();
        public Rect Bounds = new Rect { Width = 900, Height = 700 };
    }
    public sealed class Grid { public readonly ProbeCollection Children = new ProbeCollection(); }
    public sealed class ProbeCollection : Collection<WebView>
    {
        public int MaximumCount;
        protected override void InsertItem(int index, WebView item) { base.InsertItem(index, item); MaximumCount = Math.Max(MaximumCount, Count); }
    }
    public sealed class WebViewNavigationCompletedEventArgs { public bool IsSuccess; }
    public sealed class WebView
    {
        public static readonly List<WebView> Created = new List<WebView>();
        public static bool ThrowOnBlank;
        public double Width, Height, Opacity;
        public bool IsHitTestVisible;
        public double ScreenWidth = 1440, ScreenHeight = 1000;
        public event TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> NavigationCompleted;
        public int HandlerCount { get { return NavigationCompleted == null ? 0 : NavigationCompleted.GetInvocationList().Length; } }
        public WebView(WebViewExecutionMode mode) { Created.Add(this); }
        public void NavigateToString(string html) { if (html == "<html></html>" && ThrowOnBlank) throw new Exception("cleanup navigation unavailable"); }
        public void FinishNavigation(bool success = true)
        {
            var callback = NavigationCompleted;
            if (callback != null) callback(this, new WebViewNavigationCompletedEventArgs { IsSuccess = success });
        }
        public Task<string> InvokeScriptAsync(string name, string[] args) { return Task.FromResult(ScreenWidth + "," + ScreenHeight); }
    }
}
namespace Windows.Data.Json
{
    public sealed class JsonObject
    {
        private double width, height;
        public static JsonObject Parse(string text) { var parts = text.Split(','); return new JsonObject { width = double.Parse(parts[0]), height = double.Parse(parts[1]) }; }
        public double GetNamedNumber(string name) { return name == "w" ? width : height; }
    }
}
namespace LitePdfViewer
{
    public sealed class DummyElement { public string Text; public Visibility Visibility; public bool? IsChecked; }
    public sealed class DummyDispatcher
    {
        public readonly Queue<TaskCompletionSource<bool>> Gates = new Queue<TaskCompletionSource<bool>>();
        public Task RunAsync(Windows.UI.Core.CoreDispatcherPriority priority, Action action) { action(); return Gates.Count == 0 ? Task.FromResult(true) : Gates.Dequeue().Task; }
    }
    public sealed class PdfTextSource { public PdfTextSource(StorageFile file) { } }
    public sealed partial class MainPage
    {
        private ulong activeRenderToken;
        private PdfDocument document;
        private StorageFile currentFile;
        private object annotationFolder;
        private readonly List<object> pageViews = new List<object>();
        private uint pageIndex;
        private Size firstPageSize;
        private bool thumbsRequested, userSized, previewSizeInitialized, applyingPreviewSize;
        private ulong applyingPreviewSizeToken;
        private Task<Size> pendingWorkAreaRead;
        private bool? thumbnailPreference;
        private PdfTextSource textSource;
        private readonly DummyElement ThumbsToggle = new DummyElement();
        private readonly DummyElement FileNameText = new DummyElement();
        private readonly DummyElement EmptyState = new DummyElement();
        public readonly Grid Host = new Grid();
        public object Content { get { return Host; } }
        public readonly DummyDispatcher Dispatcher = new DummyDispatcher();
        public readonly List<ulong> WorkerTokens = new List<ulong>();
        public readonly List<ulong> GeometryTokens = new List<ulong>();
        public readonly List<string> RenderFiles = new List<string>();
        public readonly Dictionary<string, TaskCompletionSource<bool>> RenderGates = new Dictionary<string, TaskCompletionSource<bool>>();
        public int FitCalls, FocusCalls, Updates, ReadingPage;
        public bool Applying { get { return applyingPreviewSize; } }
        public bool Initialized { get { return previewSizeInitialized; } }
        public bool HasDocument { get { return document != null; } }
        public bool ErrorVisible { get { return EmptyState.Visibility == Visibility.Visible; } }
        public string FileTitle { get { return FileNameText.Text; } }
        public bool UserSized { get { return userSized; } set { userSized = value; } }
        public Task Open(StorageFile file) { return LoadDocumentAsync(file); }
        public Task SizeFor(ulong token) { activeRenderToken = token; firstPageSize = new Size(600, 800); return ApplyPreviewWindowSizeAsync(token, true); }
        private void ResetSearch() { }
        private void ResetPageViews() { pageViews.Clear(); }
        private void ResetThumbnails() { }
        private void CreatePagePlaceholders(PdfDocument pdf) { for (int i = 0; i < pdf.PageCount; ++i) pageViews.Add(new object()); }
        private void SetThumbsVisible(bool value, bool animated) { }
        private void UpdateUi() { ++Updates; }
        private void FitToWindow(bool animated) { ++FitCalls; ReadingPage = 0; }
        private void ScheduleRelayout() { }
        private bool Focus(FocusState state) { ++FocusCalls; return true; }
        private Task RenderPageCoreAsync(uint page, ulong token)
        {
            var name = currentFile.Name; RenderFiles.Add(name);
            TaskCompletionSource<bool> gate;
            return RenderGates.TryGetValue(name, out gate) ? gate.Task : Task.FromResult(true);
        }
        private Task RunRenderLoopAsync(ulong token) { WorkerTokens.Add(token); return Task.FromResult(true); }
        private Task ProbeAspectsAsync(ulong token) { GeometryTokens.Add(token); return Task.FromResult(true); }
    }
}

internal sealed class UiContext : SynchronizationContext, IDisposable
{
    private readonly Queue<Action> queue = new Queue<Action>();
    private readonly AutoResetEvent signal = new AutoResetEvent(false);
    private bool disposed;
    public override void Post(SendOrPostCallback callback, object state)
    {
        lock (queue)
        {
            // Superseded sizing operations can outlive a completed load. A
            // finished test context drops those callbacks like a closed view.
            if (disposed) return;
            queue.Enqueue(() => callback(state)); signal.Set();
        }
    }
    public void Run(Func<Task> test)
    {
        var previous = Current; SetSynchronizationContext(this);
        try
        {
            var task = test(); var until = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted)
            {
                Action next = null; lock (queue) { if (queue.Count != 0) next = queue.Dequeue(); }
                if (next != null) next(); else signal.WaitOne(10);
                if (DateTime.UtcNow > until) throw new Exception("Test stalled behind asynchronous work.");
            }
            task.GetAwaiter().GetResult();
        }
        finally { SetSynchronizationContext(previous); }
    }
    public void Dispose() { lock (queue) { disposed = true; queue.Clear(); signal.Dispose(); } }
}
internal static class PdfPreviewStartupTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static StorageFile File(string name) { return new StorageFile { Name = name }; }
    private static void Reset(bool native = false)
    {
        ApplicationView.Current = new ApplicationView(); PdfNative.PreviewDisplay.Area = native ? new Size(1440, 1000) : new Size();
        PdfNative.PreviewDisplay.Throw = false; WebView.Created.Clear(); WebView.ThrowOnBlank = false;
    }
    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (!condition()) { if (DateTime.UtcNow > until) throw new Exception("Expected continuation did not run."); await Task.Delay(1); }
    }
    private static async Task CheckNativeSizingDoesNotBlockPaint()
    {
        Reset(true); var page = new LitePdfViewer.MainPage(); var dispatch = new TaskCompletionSource<bool>(); page.Dispatcher.Gates.Enqueue(dispatch);
        var open = page.Open(File("native"));
        Check(page.RenderFiles.Count == 1 && page.WorkerTokens.Count == 1 && !open.IsCompleted,
            "First page and visible worker waited for window sizing.");
        dispatch.SetResult(true); await open;
    }
    private static async Task CheckSharedLegacyProbe()
    {
        Reset(); var page = new LitePdfViewer.MainPage(); var a = page.Open(File("A"));
        Check(page.RenderFiles.Count == 1 && page.WorkerTokens.Count == 1 && !a.IsCompleted,
            "Legacy work-area probing blocked first paint or visible workers.");
        var b = page.Open(File("B"));
        Check(WebView.Created.Count == 1 && page.Host.Children.MaximumCount == 1, "Rapid file switches created overlapping browser probes.");
        WebView.Created[0].FinishNavigation(); await Task.WhenAll(a, b);
        Check(ApplicationView.Current.Resizes.Count == 1 && page.FileTitle == "B" && page.HasDocument,
            "A stale file resized or reset the replacement document.");
        Check(page.Host.Children.Count == 0 && WebView.Created[0].HandlerCount == 0, "Probe retained its control or event handler.");
    }
    private static async Task CheckStaleFirstPageAndReadingPosition()
    {
        Reset(); var page = new LitePdfViewer.MainPage(); var render = new TaskCompletionSource<bool>(); page.RenderGates["A"] = render;
        var a = page.Open(File("A")); page.ReadingPage = 8;
        render.SetResult(true); await Until(() => page.WorkerTokens.Count == 1);
        Check(page.ReadingPage == 8 && page.FitCalls == 1, "First-page completion reset an existing reading position.");
        WebView.Created[0].FinishNavigation(); await a;

        Reset(true); page = new LitePdfViewer.MainPage(); render = new TaskCompletionSource<bool>(); page.RenderGates["old"] = render;
        a = page.Open(File("old")); var b = page.Open(File("new")); var updates = page.Updates; var focuses = page.FocusCalls;
        render.SetResult(true); await a;
        Check(page.WorkerTokens.Count == 1 && page.WorkerTokens[0] == 2 && page.GeometryTokens.Count == 1,
            "Stale first-page completion started old render/geometry workers.");
        Check(page.Updates == updates && page.FocusCalls == focuses, "Stale first-page completion modified replacement UI.");
        await b;
    }
    private static async Task CheckManualSizeAndOwnership()
    {
        Reset(); var page = new LitePdfViewer.MainPage(); var open = page.Open(File("manual"));
        Check(page.Initialized && !page.Applying, "Manual resize cannot be recognized during a pending work-area query.");
        page.UserSized = true; WebView.Created[0].FinishNavigation(); await open;
        Check(ApplicationView.Current.Resizes.Count == 0 && page.UserSized, "Late sizing overwrote a manual window size.");

        Reset(true); page = new LitePdfViewer.MainPage(); var gateA = new TaskCompletionSource<bool>(); var gateB = new TaskCompletionSource<bool>();
        page.Dispatcher.Gates.Enqueue(gateA); page.Dispatcher.Gates.Enqueue(gateB);
        var a = page.SizeFor(1); var b = page.SizeFor(2); gateA.SetResult(true); await a;
        Check(page.Applying && !b.IsCompleted, "An old resize continuation cleared a newer request's event suppression.");
        gateB.SetResult(true); await b; Check(!page.Applying, "Resize suppression remained after the latest request.");
    }
    private static async Task CheckFailureIsolationAndFreshProbe()
    {
        Reset(true); ApplicationView.Current.ThrowOnResize = true; var page = new LitePdfViewer.MainPage(); await page.Open(File("resize-error"));
        Check(page.HasDocument && !page.ErrorVisible && !page.Applying && page.WorkerTokens.Count == 1, "A sizing failure rejected a readable PDF.");
        Reset(); PdfNative.PreviewDisplay.Throw = true; page = new LitePdfViewer.MainPage(); await page.Open(File("display-error"));
        Check(page.HasDocument && !page.ErrorVisible && page.RenderFiles.Count == 1, "A display query failure rejected a readable PDF.");

        Reset(); WebView.ThrowOnBlank = true; page = new LitePdfViewer.MainPage();
        var a = page.Open(new StorageFile { Name = "first-monitor", PageSize = new Size(1866.6666667, 400) });
        var oldProbe = WebView.Created[0]; oldProbe.ScreenWidth = 1280; oldProbe.FinishNavigation(); await a;
        Check(page.HasDocument && page.Host.Children.Count == 0 && oldProbe.HandlerCount == 0, "Cleanup navigation failure retained a probe or rejected the PDF.");
        var firstWidth = ApplicationView.Current.Resizes[0].Width;
        var b = page.Open(new StorageFile { Name = "second-monitor", PageSize = new Size(1866.6666667, 400) });
        Check(WebView.Created.Count == 2 && page.Host.Children.MaximumCount == 1, "A completed probe was cached across monitor changes.");
        WebView.Created[1].ScreenWidth = 1800; WebView.Created[1].FinishNavigation(); await b;
        Check(ApplicationView.Current.Resizes[1].Width > firstWidth, "The later file reused stale monitor dimensions.");
    }
    public static int Main()
    {
        try
        {
            foreach (Func<Task> test in new Func<Task>[] { CheckNativeSizingDoesNotBlockPaint, CheckSharedLegacyProbe, CheckStaleFirstPageAndReadingPosition, CheckManualSizeAndOwnership, CheckFailureIsolationAndFreshProbe })
                using (var context = new UiContext()) { Console.WriteLine(test.Method.Name); context.Run(test); }
            Console.WriteLine("PASS: production preview startup, first paint/worker independence, stale-file/resize races, manual size, single probe and cleanup"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
