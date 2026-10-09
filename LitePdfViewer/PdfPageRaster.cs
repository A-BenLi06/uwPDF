using System;
using System.Diagnostics;
using System.Threading.Tasks;
#if CPPWINRT_RENDERER
using PdfSurfaceRenderer = PdfNative.Rendering.PdfSurfaceRenderer;
#else
using PdfSurfaceRenderer = PdfNative.PdfSurfaceRenderer;
#endif
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace LitePdfViewer
{
    internal sealed class PdfPageRaster : IDisposable
    {
        public ImageSource Source;
        public SoftwareBitmap Pixels;
        public long Bytes;
        public void Dispose()
        {
            ReleaseSource(Source);
            Source = null;
            if (Pixels != null) { Pixels.Dispose(); Pixels = null; }
        }
        public static void ReleaseSource(ImageSource source)
        {
            var surface = source as SurfaceImageSource;
            if (surface != null)
            {
                try { PdfSurfaceRenderer.ReleaseSurface(surface); }
                catch (Exception ex) { Debug.WriteLine("Surface release: " + ex.Message); }
            }
            var bitmap = source as SoftwareBitmapSource;
            if (bitmap != null) bitmap.Dispose();
        }
    }

    // Surface creation and EndDraw belong to the XAML thread. Device setup and
    // the synchronous native PDF draw run on workers.
    internal sealed class PdfRasterBackend
    {
        private PdfSurfaceRenderer renderer;
        private int deviceGeneration;
        private bool nativeUnavailable;
        public event Action DeviceLost;
        public void ResetDevice() { renderer = null; nativeUnavailable = false; deviceGeneration++; }
        public async Task<PdfPageRaster> RenderAsync(PdfPage page, uint width, uint height, Rect sourceRect)
        {
            SurfaceImageSource surface = null;
            var timer = Stopwatch.StartNew();
            var generation = deviceGeneration;
            var surfaceCreated = false;
            var completionFailed = false;
            if (!nativeUnavailable)
            {
                try
                {
                    if (renderer == null)
                    {
                        var created = await PdfSurfaceRenderer.CreateAsync();
                        if (generation != deviceGeneration) throw new OperationCanceledException("PDF device was reset during initialization.");
                        renderer = created;
                        Debug.WriteLine("PDF device: " + (renderer.IsHardwareAccelerated ? "hardware" : "WARP"));
                    }
                    var currentRenderer = renderer;
                    surface = currentRenderer.CreateSurface((int)width, (int)height);
                    surfaceCreated = true;
                    try { await currentRenderer.DrawAsync(surface, page, (int)width, (int)height, sourceRect); }
                    finally
                    {
                        try { currentRenderer.CompleteDraw(); }
                        catch { completionFailed = true; throw; }
                    }
                    if (generation != deviceGeneration) throw new OperationCanceledException("PDF device was reset during rendering.");
                    Debug.WriteLine("PDF surface: " + width + "x" + height + " " + timer.ElapsedMilliseconds + "ms");
                    return new PdfPageRaster { Source = surface, Bytes = (long)width * height * 4 };
                }
                catch (OperationCanceledException)
                {
                    PdfPageRaster.ReleaseSource(surface);
                    throw;
                }
                catch (Exception ex)
                {
                    PdfPageRaster.ReleaseSource(surface);
                    // A failure from an obsolete device must not disable/reset its
                    // replacement or publish a fallback into the new generation.
                    if (generation != deviceGeneration)
                        throw new OperationCanceledException("PDF device changed during a failed draw.", ex);
                    var error = unchecked((uint)ex.HResult);
                    if (error == 0x887A0005 || error == 0x887A0007 || error == 0x8899000C)
                    {
                        ResetDevice();
                        var handler = DeviceLost;
                        if (handler != null) handler();
                    }
                    else if (error != 0x8007000E && (!surfaceCreated || completionFailed))
                    {
                        // A device/interop setup rejection is independent of this
                        // PDF page. Avoid retrying its expensive setup on every page
                        // and thumbnail. Temporary allocation pressure is retryable;
                        // resume/device recovery permits retrying an interop rejection.
                        renderer = null;
                        nativeUnavailable = true;
                    }
                    // A page-specific drawing failure keeps the working device for
                    // other pages, rather than repeatedly initializing it on the UI.
                    Debug.WriteLine("PDF surface fallback: " + ex.Message);
                }
            }

            generation = deviceGeneration;
            SoftwareBitmap pixels = null;
            SoftwareBitmapSource source = null;
            try
            {
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await page.RenderToStreamAsync(stream, new PdfPageRenderOptions {
                        DestinationWidth = width, DestinationHeight = height, SourceRect = sourceRect,
                        BitmapEncoderId = BitmapEncoder.BmpEncoderId });
                    if (generation != deviceGeneration) throw new OperationCanceledException("PDF device was reset during fallback rendering.");
                    stream.Seek(0);
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    if (generation != deviceGeneration) throw new OperationCanceledException("PDF device was reset while opening the bitmap decoder.");
                    pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    if (generation != deviceGeneration) throw new OperationCanceledException("PDF device was reset during bitmap decoding.");
                    source = new SoftwareBitmapSource();
                    await source.SetBitmapAsync(pixels);
                    if (generation != deviceGeneration) throw new OperationCanceledException("PDF device was reset during rendering.");
                    return new PdfPageRaster { Source = source, Pixels = pixels, Bytes = (long)width * height * 8 };
                }
            }
            catch
            {
                if (source != null) source.Dispose();
                if (pixels != null) pixels.Dispose();
                throw;
            }
        }
    }
}
