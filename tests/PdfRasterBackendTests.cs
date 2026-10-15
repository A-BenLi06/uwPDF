using System;
using System.Threading.Tasks;
using LitePdfViewer;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;

class PdfRasterBackendTests
{
    const int Rejected = unchecked((int)0x80004002), FailedPage = unchecked((int)0x80004005);
    const int DeviceRemoved = unchecked((int)0x887A0005);
    const int OutOfMemory = unchecked((int)0x8007000E);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static Task<PdfPageRaster> Render(PdfRasterBackend backend) { return backend.RenderAsync(new PdfPage(), 100, 200, new Windows.Foundation.Rect()); }
    static async Task Cancelled(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { return; }
        throw new Exception("Obsolete raster operation was accepted.");
    }
    static async Task ExpectNative(PdfRasterBackend backend)
    {
        using (var raster = await Render(backend))
            Require(raster.Source is SurfaceImageSource, "Native rendering did not recover.");
    }

    static async Task Run()
    {
        RasterBackendStubs.Reset();
        var backend = new PdfRasterBackend();
        RasterBackendStubs.Factory = () => RasterBackendStubs.Fail<PdfNative.PdfSurfaceRenderer>(Rejected);
        for (var i = 0; i < 8; i++)
            using (var raster = await Render(backend)) Require(raster.Source is SoftwareBitmapSource, "Setup rejection lost the bitmap fallback.");
        Require(RasterBackendStubs.FactoryCalls == 1 && RasterBackendStubs.FallbackCalls == 8, "Rejected device setup was repeated for every page.");
        backend.ResetDevice(); RasterBackendStubs.Factory = null;
        await ExpectNative(backend);
        Require(RasterBackendStubs.FactoryCalls == 2, "Explicit recovery did not retry setup.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Surface = () => { throw new RasterBackendStubs.NativeError(Rejected); };
        for (var i = 0; i < 8; i++) using (var raster = await Render(backend)) {}
        Require(RasterBackendStubs.FactoryCalls == 1 && RasterBackendStubs.SurfaceCalls == 1, "Rejected interop setup was repeated for every page.");
        backend.ResetDevice(); RasterBackendStubs.Surface = null;
        await ExpectNative(backend);

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Draw = () => RasterBackendStubs.DrawCalls == 1 ? RasterBackendStubs.Fail<bool>(FailedPage) : Task.FromResult(true);
        using (var raster = await Render(backend)) Require(raster.Source is SoftwareBitmapSource, "Failed page did not fall back.");
        await ExpectNative(backend);
        Require(RasterBackendStubs.FactoryCalls == 1 && RasterBackendStubs.CompleteCalls == 2, "A failed page destroyed the working device or skipped EndDraw.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Complete = () => { throw new RasterBackendStubs.NativeError(Rejected); };
        for (var i = 0; i < 2; i++) using (var raster = await Render(backend)) {}
        Require(RasterBackendStubs.FactoryCalls == 1 && RasterBackendStubs.CompleteCalls == 1, "Repeated EndDraw rejection did not use the stable fallback.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Surface = () => { if (RasterBackendStubs.SurfaceCalls == 1) throw new RasterBackendStubs.NativeError(OutOfMemory); return new SurfaceImageSource(); };
        using (var raster = await Render(backend)) Require(raster.Source is SoftwareBitmapSource, "Allocation pressure lost fallback.");
        await ExpectNative(backend);
        Require(RasterBackendStubs.FactoryCalls == 1, "Temporary surface allocation failure permanently disabled/recreated the device.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Factory = () => RasterBackendStubs.FactoryCalls == 1 ? RasterBackendStubs.Fail<PdfNative.PdfSurfaceRenderer>(OutOfMemory) : Task.FromResult(new PdfNative.PdfSurfaceRenderer());
        using (var raster = await Render(backend)) {}
        await ExpectNative(backend);
        Require(RasterBackendStubs.FactoryCalls == 2, "Temporary initialization allocation failure permanently disabled native rendering.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        var lost = 0; backend.DeviceLost += () => lost++;
        RasterBackendStubs.Draw = () => RasterBackendStubs.DrawCalls == 1 ? RasterBackendStubs.Fail<bool>(DeviceRemoved) : Task.FromResult(true);
        using (var raster = await Render(backend)) {}
        await ExpectNative(backend);
        Require(lost == 1 && RasterBackendStubs.FactoryCalls == 2, "Device loss did not invalidate/recreate the device exactly once.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        var creation = new TaskCompletionSource<PdfNative.PdfSurfaceRenderer>();
        RasterBackendStubs.Factory = () => creation.Task;
        var pending = Render(backend);
        Require(!pending.IsCompleted && RasterBackendStubs.SurfaceCalls == 0, "Pending async initialization was not awaited.");
        backend.ResetDevice(); creation.SetResult(new PdfNative.PdfSurfaceRenderer());
        await Cancelled(pending);
        Require(RasterBackendStubs.SurfaceCalls == 0 && RasterBackendStubs.FallbackCalls == 0, "Obsolete initialization allocated/published a surface.");
        RasterBackendStubs.Factory = null; await ExpectNative(backend);

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        creation = new TaskCompletionSource<PdfNative.PdfSurfaceRenderer>();
        RasterBackendStubs.Factory = () => creation.Task;
        pending = Render(backend); backend.ResetDevice();
        creation.SetException(new RasterBackendStubs.NativeError(Rejected));
        await Cancelled(pending);
        RasterBackendStubs.Factory = null; await ExpectNative(backend);
        Require(RasterBackendStubs.FallbackCalls == 0, "Stale setup failure poisoned the replacement generation.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        lost = 0; backend.DeviceLost += () => lost++;
        var drawing = new TaskCompletionSource<bool>();
        RasterBackendStubs.Draw = () => drawing.Task;
        pending = Render(backend); backend.ResetDevice();
        drawing.SetException(new RasterBackendStubs.NativeError(DeviceRemoved));
        await Cancelled(pending);
        Require(lost == 0 && RasterBackendStubs.FallbackCalls == 0 && RasterBackendStubs.ReleasedSurfaces == 1, "Stale device failure reset the replacement or leaked its surface.");
        RasterBackendStubs.Draw = null; await ExpectNative(backend);

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Factory = () => RasterBackendStubs.Fail<PdfNative.PdfSurfaceRenderer>(Rejected);
        var rendering = new TaskCompletionSource<bool>(); RasterBackendStubs.Render = () => rendering.Task;
        pending = Render(backend); backend.ResetDevice(); rendering.SetResult(true);
        await Cancelled(pending);
        Require(RasterBackendStubs.DecodeCalls == 0, "Obsolete fallback unnecessarily decoded/uploaded bitmap data.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Factory = () => RasterBackendStubs.Fail<PdfNative.PdfSurfaceRenderer>(Rejected);
        var opening = new TaskCompletionSource<BitmapDecoder>(); RasterBackendStubs.OpenDecoder = () => opening.Task;
        pending = Render(backend); backend.ResetDevice(); opening.SetResult(new BitmapDecoder());
        await Cancelled(pending);
        Require(RasterBackendStubs.DecodeCalls == 0, "Obsolete decoder initialization proceeded to pixel allocation.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Factory = () => RasterBackendStubs.Fail<PdfNative.PdfSurfaceRenderer>(Rejected);
        var decoding = new TaskCompletionSource<SoftwareBitmap>(); RasterBackendStubs.Decode = () => decoding.Task;
        pending = Render(backend); backend.ResetDevice(); decoding.SetResult(new SoftwareBitmap());
        await Cancelled(pending);
        Require(RasterBackendStubs.LivePixels == 0 && RasterBackendStubs.SourcesCreated == 0, "Obsolete decoded pixels leaked or were uploaded.");

        RasterBackendStubs.Reset(); backend = new PdfRasterBackend();
        RasterBackendStubs.Factory = () => RasterBackendStubs.Fail<PdfNative.PdfSurfaceRenderer>(Rejected);
        RasterBackendStubs.Upload = () => RasterBackendStubs.Fail<bool>(FailedPage);
        try { using (var raster = await Render(backend)) {} throw new Exception("Failed upload was accepted."); }
        catch (RasterBackendStubs.NativeError) {}
        Require(RasterBackendStubs.LivePixels == 0 && RasterBackendStubs.LiveSources == 0, "Failed bitmap upload leaked partial allocations.");
        RasterBackendStubs.Reset();
        Console.WriteLine("PASS: production raster backend retry policy, async setup, device generations and partial-allocation cleanup");
    }
    static int Main() { Run().GetAwaiter().GetResult(); return 0; }
}
