using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LitePdfViewer;

namespace Windows.Foundation
{
    public struct Point
    {
        public double X, Y;
        public Point(double x, double y) { Probe.PointCreated(); X=x; Y=y; }
    }
}
namespace Windows.Storage
{
    public sealed class FakeStream : IDisposable { public void Dispose() { } }
    public sealed class StorageFile
    {
        public Task<FakeStream> OpenReadAsync() { return Task.FromResult(new FakeStream()); }
    }
}
namespace PdfNative
{
    public sealed class PdfTextPage { public string Text; public float[] Coordinates; }
    public sealed class PdfTextDocument : IDisposable
    {
        public static PdfTextPage Page;
        public static int Reads;
        public static Task<PdfTextDocument> OpenAsync(Windows.Storage.FakeStream stream) { return Task.FromResult(new PdfTextDocument()); }
        public Task<PdfTextPage> ReadPageAsync(uint index) { Reads++; return Task.FromResult(Page); }
        public void Dispose() { }
    }
}
namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private sealed class PageView { public uint Index; public List<TextGlyph> Glyphs; public double LayoutWidth=600, LayoutHeight=800; }
        private struct TextQuad { }
        private sealed class SearchHit { public uint Page; public int First, Last; public List<TextQuad> Quads; }
        private readonly List<SearchHit> searchHits=new List<SearchHit>();
        private SearchHit activeSearchHit;
        private int searchHitIndex=-1, searchSkippedPages;
        private string indexedSearchQuery;
        private ulong indexedSearchToken;
        private bool searchIndexComplete;
        private static List<TextQuad> BuildTextQuads(List<TextGlyph> glyphs,int first,int last,double width,double height,CancellationToken cancellation)
        { return new List<TextQuad>(); }
        private void UpdateSearchNavigation() { }
        private void ClearSearchResults() { activeSearchHit=null; searchHits.Clear(); searchHitIndex=-1; searchIndexComplete=false; indexedSearchQuery=null; searchSkippedPages=0; }
        private sealed class TextBox { public string Text="ana"; }
        private sealed class StatusBox
        {
            private string text;
            public string Text
            {
                get { return text; }
                set { Tests.Assert(Thread.CurrentThread.ManagedThreadId==Probe.UiThread,"Search published off UI thread"); text=value; }
            }
        }
        private object document=new object();
        private PdfTextSource textSource=new PdfTextSource(new Windows.Storage.StorageFile());
        private int searchVersion;
        private bool searchBusy;
        private ulong activeRenderToken=1;
        private uint pageIndex;
        private PageView selectedTextPage;
        private int selectionAnchor, selectionEnd;
        private CancellationTokenSource searchCancellation=new CancellationTokenSource();
        private TextBox SearchTextBox=new TextBox();
        private StatusBox SearchStatus=new StatusBox();
        private List<PageView> pageViews=new List<PageView>();
        internal string SelectionText(List<TextGlyph> glyphs)
        {
            selectedTextPage=new PageView { Glyphs=glyphs }; selectionAnchor=0; selectionEnd=glyphs.Count-1;
            return SelectedText();
        }
        public int Shows, LastFirst, LastEnd;
        public uint LastPage;
        public bool Busy { get { return searchBusy; } }
        public string Status { get { return SearchStatus.Text; } }
        public void AddPage(string value)
        {
            var glyphs=new List<TextGlyph>();
            foreach (var ch in value) glyphs.Add(new TextGlyph { Text=ch.ToString() });
            pageViews.Add(new PageView { Index=(uint)pageViews.Count, Glyphs=glyphs });
        }
        public void Select(int page, int first, int last)
        {
            pageIndex=(uint)page; selectedTextPage=pageViews[page]; selectionAnchor=first; selectionEnd=last;
            searchHitIndex=searchHits.FindIndex(h=>h.Page==(uint)page && h.First==first && h.Last==last);
            activeSearchHit=searchHitIndex>=0 ? searchHits[searchHitIndex] : null;
        }
        public void Query(string value) { SearchTextBox.Text=value; }
        public void ReadingPage(int page) { pageIndex=(uint)page; }
        public Task Find(bool forward, bool restart) { return FindTextAsync(forward,restart); }
        public void Invalidate(bool fileSwitch)
        {
            if (fileSwitch) activeRenderToken++; else searchVersion++;
            searchBusy=false; SearchStatus.Text="new request";
        }
        private Task ShowSearchMatchAsync(SearchHit hit, List<TextGlyph> glyphs,
            int version, ulong token, CancellationToken cancellation)
        {
            Tests.Assert(Thread.CurrentThread.ManagedThreadId==Probe.UiThread,"Match published off UI thread");
            activeSearchHit=hit;
            hit.Quads=new List<TextQuad>();
            Shows++; LastPage=hit.Page; LastFirst=hit.First; LastEnd=hit.Last;
            return Task.FromResult(0);
        }
    }
}
internal sealed class UiContext : SynchronizationContext
{
    private readonly BlockingCollection<Action> callbacks=new BlockingCollection<Action>();
    public override void Post(SendOrPostCallback callback, object state) { callbacks.Add(() => callback(state)); }
    public void Pump(Task task)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.ElapsedMilliseconds>5000) throw new Exception("Text worker UI test timed out");
            Action callback;
            if (callbacks.TryTake(out callback,20)) callback();
        }
    }
}
// One occupied worker makes queueing deterministic, without depending on how
// fast a particular CPU can match a cached page. Each lease releases in finally.
internal sealed class WorkerLease : IDisposable
{
    private readonly ManualResetEventSlim release=new ManualResetEventSlim();
    private readonly Task work;
    public WorkerLease()
    {
        var started=new ManualResetEventSlim();
        work=Task.Run(() => { started.Set(); release.Wait(); });
        Tests.Assert(started.Wait(2000),"Failed to occupy worker");
    }
    public void Dispose() { release.Set(); Tests.Assert(work.Wait(2000),"Worker lease did not finish"); }
}
internal static class Probe
{
    public static int UiThread, PointCount;
    public static Action FirstPoint;
    public static void PointCreated()
    {
        Tests.Assert(Thread.CurrentThread.ManagedThreadId != UiThread,"Managed glyph geometry ran on UI thread");
        if (Interlocked.Increment(ref PointCount)==1 && FirstPoint != null) FirstPoint();
    }
}
internal sealed class CancelingGlyphs : IReadOnlyList<string>
{
    private readonly CancellationTokenSource cancel;
    public int LastRead;
    public CancelingGlyphs(CancellationTokenSource cancel) { this.cancel=cancel; }
    public int Count { get { return 100000; } }
    public string this[int index] { get { LastRead=index; if (index==512) cancel.Cancel(); return "x"; } }
    public IEnumerator<string> GetEnumerator() { throw new NotSupportedException(); }
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
}
internal static class Tests
{
    private static UiContext ui;
    public static void Assert(bool value,string message) { if (!value) throw new Exception(message); }
    private static void SetPage(string text)
    {
        var coords=new float[text.Length*9];
        for (var i=0;i<text.Length;i++)
        {
            var p=i*9;
            // Angled quad: bounds must use all four corners.
            coords[p]=i+2; coords[p+1]=3; coords[p+2]=i+4; coords[p+3]=1;
            coords[p+4]=i+1; coords[p+5]=5; coords[p+6]=i+3; coords[p+7]=6; coords[p+8]=45;
        }
        PdfNative.PdfTextDocument.Page=new PdfNative.PdfTextPage { Text=text, Coordinates=coords };
        Probe.PointCount=0; Probe.FirstPoint=null;
    }
    private static void Geometry()
    {
        SetPage("A中\ud83d\ude00 B");
        var source=new PdfTextSource(new Windows.Storage.StorageFile());
        Task<List<TextGlyph>> pending;
        using (new WorkerLease())
        {
            pending=source.ReadPageAsync(0);
            Assert(!pending.IsCompleted,"Text read did not yield while geometry worker was queued");
        }
        ui.Pump(pending); var glyphs=pending.GetAwaiter().GetResult();
        Assert(glyphs.Count==5 && glyphs[1].Text=="中" && glyphs[2].Text=="\ud83d\ude00" && glyphs[4].Text=="B","UTF-16 glyph ownership changed");
        Assert(glyphs[2].X==3 && glyphs[2].Y==1 && glyphs[2].Width==3 && glyphs[2].Height==5 && glyphs[2].Angle==45,"Angled bounds changed");
        Assert(glyphs[2].TopLeft.X==4 && glyphs[2].BottomRight.Y==6 && glyphs[4].X==6,"Quad coordinates or surrogate offset changed");
        source.Dispose();
    }
    private static void CancellationAndDisposal()
    {
        // Odd UTF-16 offsets followed by surrogate pairs must still check cancellation.
        var builder=new System.Text.StringBuilder("A");
        for (var i=0;i<10000;i++) builder.Append("\ud83d\ude00");
        SetPage(builder.ToString());
        var cancel=new CancellationTokenSource();
        var source=new PdfTextSource(new Windows.Storage.StorageFile());
        Probe.FirstPoint=() => cancel.Cancel();
        var pending=source.ReadPageAsync(0,cancel.Token); ui.Pump(pending);
        Assert(pending.IsCanceled && Probe.PointCount<=1024,"Cancellation continued expanding a whole page");
        // The semaphore must be released after cancellation, allowing retry.
        SetPage("retry");
        var retry=source.ReadPageAsync(0); ui.Pump(retry);
        Assert(retry.GetAwaiter().GetResult().Count==5,"Canceled read retained text gate");
        source.Dispose();

        SetPage("disposed"); source=new PdfTextSource(new Windows.Storage.StorageFile());
        using (new WorkerLease())
        {
            pending=source.ReadPageAsync(0);
            source.Dispose();
        }
        ui.Pump(pending);
        Assert(pending.IsFaulted && pending.Exception.InnerException is ObjectDisposedException,"Disposed source published queued geometry");
        cancel=new CancellationTokenSource();
        var segments=new CancelingGlyphs(cancel); PdfSearchMatch wrapped;
        try { PdfTextSearch.Find(segments,"missing",0,true,out wrapped,cancel.Token); throw new Exception("Search mapping ignored cancellation"); }
        catch (OperationCanceledException) { }
        Assert(segments.LastRead<1024,"Canceled search mapped the entire page");
    }
    private static void OcrFallback()
    {
        SetPage("native");
        var recognized=new List<TextGlyph> { new TextGlyph { Text="recognized word", FromOcr=true } };
        var calls=0;
        Func<uint,CancellationToken,Task<List<TextGlyph>>> fallback=(index,cancel) => { calls++; return Task.FromResult(recognized); };
        var source=new PdfTextSource(new Windows.Storage.StorageFile(),fallback);
        var pending=source.ReadPageAsync(0); ui.Pump(pending);
        Assert(pending.GetAwaiter().GetResult()[0].Text=="n" && calls==0,"Native text unexpectedly invoked OCR");
        source.Dispose();
        SetPage(""); source=new PdfTextSource(new Windows.Storage.StorageFile(),fallback);
        pending=source.ReadPageAsync(0); ui.Pump(pending);
        Assert(pending.GetAwaiter().GetResult()==recognized && calls==1,"Empty native text failed OCR fallback");
        source.Dispose();
        SetPage("native"); var reads=PdfNative.PdfTextDocument.Reads;
        source=new PdfTextSource(new Windows.Storage.StorageFile(),fallback,index => index==4);
        pending=source.ReadPageAsync(4); ui.Pump(pending);
        Assert(pending.GetAwaiter().GetResult()==recognized && PdfNative.PdfTextDocument.Reads==reads,"Explicit OCR override used native text instead");
        source.Dispose();
        var completion=new TaskCompletionSource<List<TextGlyph>>();
        source=new PdfTextSource(new Windows.Storage.StorageFile(),(index,cancel)=>completion.Task,index=>true);
        pending=source.ReadPageAsync(0);
        var dispose=source.DisposeAsync();
        Assert(!dispose.IsCompleted,"Disposal did not serialize with pending recognition");
        completion.SetResult(recognized); ui.Pump(pending); ui.Pump(dispose);
        Assert(pending.IsFaulted && pending.Exception.InnerException is ObjectDisposedException,"Disposed OCR source published stale recognized text");
        var cancellation=new CancellationTokenSource(); completion=new TaskCompletionSource<List<TextGlyph>>();
        source=new PdfTextSource(new Windows.Storage.StorageFile(),(index,cancel)=>completion.Task,index=>true);
        pending=source.ReadPageAsync(0,cancellation.Token); cancellation.Cancel(); completion.SetResult(recognized); ui.Pump(pending);
        Assert(pending.IsCanceled,"Canceled recognition was published");
        pending=source.ReadPageAsync(0); ui.Pump(pending);
        Assert(pending.GetAwaiter().GetResult()==recognized,"Canceled OCR retained the text gate");
        source.Dispose();
        var copy=new MainPage();
        var words=new List<TextGlyph> {
            new TextGlyph { Text="Windows", X=.05, Y=.2, Width=.1, Height=.02, FromOcr=true },
            new TextGlyph { Text=" " },
            new TextGlyph { Text="OCR", X=.17, Y=.2, Width=.05, Height=.02, FromOcr=true },
            new TextGlyph { Text=" " },
            new TextGlyph { Text="recognition", X=.25, Y=.2, Width=.2, Height=.02, FromOcr=true },
            new TextGlyph { Text="\n" },
            new TextGlyph { Text="Next", X=.05, Y=.25, Width=.1, Height=.02, FromOcr=true } };
        Assert(copy.SelectionText(words)=="Windows OCR recognition\nNext", "OCR copy invented line breaks around geometry-free separators");
    }
    private static void Search()
    {
        var reads=PdfNative.PdfTextDocument.Reads;
        var page=new MainPage(); page.AddPage("banana"); page.AddPage("other");
        Task find;
        using (new WorkerLease())
        {
            find=page.Find(true,true);
            Assert(!find.IsCompleted && page.Busy,"Cached search monopolized UI instead of yielding");
        }
        ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.Shows==1 && page.LastFirst==1 && page.LastEnd==3 && !page.Busy && page.Status=="第1/2个","Cached forward match/count changed");
        page.Select(0,3,5); find=page.Find(false,false); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.LastFirst==1 && page.LastEnd==3,"Backward overlapping match changed");
        page.Select(0,3,5); find=page.Find(true,false); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.LastPage==0 && page.LastFirst==1,"First-page wrap match changed");
        foreach (var fileSwitch in new[] { false,true })
        {
            page=new MainPage(); page.AddPage("banana");
            using (new WorkerLease())
            {
                find=page.Find(true,true); Assert(!find.IsCompleted,"Search did not queue");
                page.Invalidate(fileSwitch);
            }
            ui.Pump(find); find.GetAwaiter().GetResult();
            Assert(page.Shows==0 && page.Status=="new request","Stale query/file result overwrote the current UI");
        }
        page=new MainPage(); for (var i=0;i<30;i++) page.AddPage("no match");
        page.Query("missing"); find=page.Find(true,true); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.Shows==0 && page.Status=="没有匹配文字" && !page.Busy,"Cached no-match scan did not complete");
        Assert(reads==PdfNative.PdfTextDocument.Reads,"Cached searches unexpectedly reread native text");
        // A match positioned near the viewport top can leave the leading-edge
        // indicator on the prior page. Next/previous follow the indexed match.
        page=new MainPage(); page.AddPage("ana"); page.AddPage("ana"); page.AddPage("ana");
        page.ReadingPage(1);
        find=page.Find(true,true); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.Status=="第2/3个","Initial result did not use document order");
        page.ReadingPage(0);
        find=page.Find(true,false); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.LastPage==2,"Next match followed the previous visible page instead of selection");
        page.Select(1,0,2); page.ReadingPage(2);
        find=page.Find(false,false); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.LastPage==0,"Previous match followed the next visible page instead of selection");
        page.Select(1,0,2); page.ReadingPage(0);
        find=page.Find(true,true); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.LastPage==0,"Restart ignored the current reading page");
        page=new MainPage(); page.AddPage("ana ana ana");
        page.AddPage("ana ana ana ana ana ana ana ana ana ana ana ana ana ana");
        page.ReadingPage(1);
        find=page.Find(true,true); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.Status=="第4/17个","Global counter confused match ordinal with page number");
        for (int i=0;i<14;i++) { find=page.Find(true,false); ui.Pump(find); find.GetAwaiter().GetResult(); }
        Assert(page.Status=="第1/17个","Forward navigation did not wrap after the final result");
        find=page.Find(false,false); ui.Pump(find); find.GetAwaiter().GetResult();
        Assert(page.Status=="第17/17个","Backward navigation did not wrap to the final result");
    }
    public static int Main()
    {
        try
        {
            // This test executable owns its thread pool; no other process is affected.
            Assert(ThreadPool.SetMinThreads(1,1) && ThreadPool.SetMaxThreads(1,1),"Could not configure isolated worker pool");
            Probe.UiThread=Thread.CurrentThread.ManagedThreadId;
            ui=new UiContext(); SynchronizationContext.SetSynchronizationContext(ui);
            Geometry(); CancellationAndDisposal(); OcrFallback(); Search();
            Console.WriteLine("PASS: queued glyph/search workers, UTF-16/quad mapping, bounded cancellation, disposal, OCR fallback/override/cancellation/disposal, cached search/wrap and stale query/file rejection.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
