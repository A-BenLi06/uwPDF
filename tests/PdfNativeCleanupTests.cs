using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using PdfNative;

// Only platform I/O and native work are doubled. The caller methods are copied
// from production by Test-NativeCleanup.ps1 and run on one UI-context thread.
namespace Windows.Foundation
{
    public struct Point { public double X, Y; public Point(double x, double y) { X=x; Y=y; } }
}
namespace Windows.Storage
{
    public enum FileAccessMode { ReadWrite }
    public sealed class FakeStream : IDisposable
    {
        public bool Closed;
        public Action BeforeClose;
        public void Dispose() { if (BeforeClose != null) BeforeClose(); Closed=true; }
    }
    public sealed class StorageFile
    {
        public string Path="source.pdf";
        public string DisplayName="source";
        public FakeStream Input=new FakeStream();
        public Task<FakeStream> OpenReadAsync() { return Task.FromResult(Input); }
        public Task<FakeStream> OpenAsync(FileAccessMode mode) { return Task.FromResult(new FakeStream()); }
        public Task CopyAndReplaceAsync(StorageFile other) { return Task.FromResult(0); }
        public Task DeleteAsync() { return Task.FromResult(0); }
    }
    public sealed class StorageFolder
    {
        public Task<StorageFile> CreateFileAsync(string name) { return Task.FromResult(new StorageFile { Path=name }); }
    }
    public sealed class ApplicationData
    {
        public static ApplicationData Current=new ApplicationData();
        public StorageFolder TemporaryFolder=new StorageFolder();
    }
}
namespace Windows.Storage.Pickers
{
    public enum PickerLocationId { DocumentsLibrary }
    public sealed class FileSavePicker
    {
        public PickerLocationId SuggestedStartLocation;
        public string SuggestedFileName;
        public System.Collections.Generic.Dictionary<string,string[]> FileTypeChoices=new System.Collections.Generic.Dictionary<string,string[]>();
        public Task<StorageFile> PickSaveFileAsync()
        {
            Tests.Assert(Tests.Native != null && Tests.Native.Closed, "Picker ran before native cleanup completed");
            Tests.PickerCount++;
            return Task.FromResult(new StorageFile { Path="copy.pdf" });
        }
    }
}
namespace Windows.UI.Xaml { public sealed class RoutedEventArgs : EventArgs { } }
namespace Windows.UI.Xaml.Controls
{
    public sealed class Button { public bool IsEnabled=true; }
    public static class ToolTipService { public static void SetToolTip(Button button, string text) { } }
    public sealed class ContentDialog
    {
        public string Title, Content, PrimaryButtonText;
        public Task ShowAsync() { Tests.Error=Content; return Task.FromResult(0); }
    }
}
namespace PdfNative
{
    public sealed class CloseProbe
    {
        public bool Closed;
        public int CloseCount;
        public void Close()
        {
            CloseCount++;
            var allowed=new ManualResetEventSlim();
            // A heartbeat posted from native teardown must be serviced by UI
            // before teardown can finish. Synchronous UI disposal deadlocks.
            Tests.Ui.Post(_ => allowed.Set(), null);
            if (!allowed.Wait(1000)) throw new InvalidOperationException("Native teardown blocked the UI heartbeat");
            Tests.Assert(Thread.CurrentThread.ManagedThreadId != Tests.UiThread, "Native stores released on UI thread");
            Closed=true;
        }
    }
    public sealed class PdfAnnotationWriter : IDisposable
    {
        public static Task<PdfAnnotationWriter> OpenAsync(FakeStream input)
        {
            Tests.Native=new CloseProbe();
            input.BeforeClose=() => Tests.Assert(Tests.Native.Closed, "Input closed before writer cleanup finished");
            return Task.FromResult(new PdfAnnotationWriter());
        }
        public Task WriteAsync(FakeStream output)
        {
            if (Tests.Mode=="write-failure") throw new InvalidOperationException("write failure");
            return Task.FromResult(0);
        }
        public void Dispose() { Tests.Native.Close(); }
    }
    public sealed class PdfTextPage { public string Text=""; public float[] Coordinates=new float[0]; }
    public sealed class PdfTextDocument : IDisposable
    {
        public static TaskCompletionSource<PdfTextDocument> Opening;
        public static Task<PdfTextDocument> OpenAsync(FakeStream input)
        {
            input.BeforeClose=() => Tests.Assert(Tests.Native.Closed, "Abandoned input closed before native cleanup");
            return Opening.Task;
        }
        public Task<PdfTextPage> ReadPageAsync(uint index) { throw new InvalidOperationException("Abandoned document read"); }
        public void Dispose() { Tests.Native.Close(); }
    }
}
namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private object document=new object();
        private StorageFile currentFile=new StorageFile();
        private bool exportBusy;
        private ulong activeRenderToken=1;
        private Windows.UI.Xaml.Controls.Button ExportButton=new Windows.UI.Xaml.Controls.Button();
        private Task AppendExportAnnotationsAsync(PdfAnnotationWriter writer, ulong token)
        {
            if (Tests.Mode=="append-failure") throw new InvalidOperationException("append failure");
            if (Tests.Mode=="canceled") throw new OperationCanceledException();
            return Task.FromResult(0);
        }
        public bool Busy { get { return exportBusy; } }
        public bool InputClosed { get { return currentFile.Input.Closed; } }
        public bool ExportEnabled { get { return ExportButton.IsEnabled; } }
        public void Export() { ExportButton_Click(null,new Windows.UI.Xaml.RoutedEventArgs()); }
    }
}
internal sealed class UiContext : SynchronizationContext
{
    private readonly BlockingCollection<Action> callbacks=new BlockingCollection<Action>();
    public override void Post(SendOrPostCallback callback, object state) { callbacks.Add(() => callback(state)); }
    public void PumpUntil(Func<bool> done)
    {
        var timeout=System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (timeout.ElapsedMilliseconds>5000) throw new Exception("UI cleanup test timed out");
            Action callback;
            if (callbacks.TryTake(out callback, 20)) callback();
        }
    }
}
internal static class Tests
{
    public static UiContext Ui;
    public static int UiThread, PickerCount;
    public static string Mode, Error;
    public static CloseProbe Native;
    public static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            Ui=new UiContext(); UiThread=Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(Ui);
            foreach (var mode in new[] { "success", "append-failure", "write-failure", "canceled" })
            {
                Mode=mode; Error=null; Native=null; PickerCount=0;
                var page=new LitePdfViewer.MainPage(); page.Export();
                Ui.PumpUntil(() => !page.Busy);
                Assert(Native != null && Native.Closed && Native.CloseCount==1, "Writer cleanup did not complete exactly once: "+mode);
                Assert(page.InputClosed && page.ExportEnabled, "Export did not release input/reset UI: "+mode);
                Assert(PickerCount==(mode=="success" ? 1 : 0), "Unexpected picker after "+mode);
                Assert((mode=="success" || mode=="canceled") ? Error==null : Error==mode.Replace('-',' '), "Wrong export error: "+Error);
            }
            Native=new CloseProbe();
            PdfTextDocument.Opening=new TaskCompletionSource<PdfTextDocument>();
            var source=new LitePdfViewer.PdfTextSource(new StorageFile());
            var pending=source.ReadPageAsync(0);
            source.Dispose();
            PdfTextDocument.Opening.SetResult(new PdfTextDocument());
            Ui.PumpUntil(() => pending.IsCompleted);
            Assert(pending.IsFaulted && pending.Exception.InnerException is ObjectDisposedException, "Late open did not report disposal");
            Assert(Native.Closed && Native.CloseCount==1, "Late-open native document leaked");
            Console.WriteLine("PASS: UI heartbeat during writer cleanup (success/failure/cancellation), cleanup ordering and abandoned text open.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
