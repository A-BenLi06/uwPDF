// Test doubles for platform calls. The test compiles the production backend;
// these do not model GPU output, XAML presentation or Windows threading.
using System;
using System.Threading.Tasks;

static class RasterBackendStubs
{
    public static int FactoryCalls, SurfaceCalls, DrawCalls, CompleteCalls, ReleasedSurfaces;
    public static int FallbackCalls, DecodeCalls, SourcesCreated, LiveSources, LivePixels;
    public static Func<Task<PdfNative.PdfSurfaceRenderer>> Factory;
    public static Func<Windows.UI.Xaml.Media.Imaging.SurfaceImageSource> Surface;
    public static Func<Task> Draw, Render, Upload;
    public static Action Complete;
    public static Func<Task<Windows.Graphics.Imaging.SoftwareBitmap>> Decode;
    public static Func<Task<Windows.Graphics.Imaging.BitmapDecoder>> OpenDecoder;

    public static void Reset()
    {
        if (LiveSources != 0 || LivePixels != 0) throw new Exception("A previous raster test leaked a bitmap.");
        FactoryCalls = SurfaceCalls = DrawCalls = CompleteCalls = ReleasedSurfaces = 0;
        FallbackCalls = DecodeCalls = SourcesCreated = 0;
        Factory = null; Surface = null; Draw = Render = Upload = null; Complete = null; Decode = null; OpenDecoder = null;
    }
    public static Task<T> Fail<T>(int result)
    {
        var task = new TaskCompletionSource<T>();
        task.SetException(new NativeError(result));
        return task.Task;
    }
    public sealed class NativeError : Exception
    {
        public NativeError(int result) { HResult = result; }
    }
}

namespace PdfNative
{
    public class PdfSurfaceRenderer
    {
        public static Task<PdfSurfaceRenderer> CreateAsync()
        {
            RasterBackendStubs.FactoryCalls++;
            return RasterBackendStubs.Factory == null ? Task.FromResult(new PdfSurfaceRenderer()) : RasterBackendStubs.Factory();
        }
        public bool IsHardwareAccelerated { get { return true; } }
        public Windows.UI.Xaml.Media.Imaging.SurfaceImageSource CreateSurface(int width, int height)
        {
            RasterBackendStubs.SurfaceCalls++;
            return RasterBackendStubs.Surface == null ? new Windows.UI.Xaml.Media.Imaging.SurfaceImageSource() : RasterBackendStubs.Surface();
        }
        public Task DrawAsync(Windows.UI.Xaml.Media.Imaging.SurfaceImageSource surface,
            Windows.Data.Pdf.PdfPage page, int width, int height, Windows.Foundation.Rect region)
        {
            RasterBackendStubs.DrawCalls++;
            return RasterBackendStubs.Draw == null ? Task.FromResult(0) : RasterBackendStubs.Draw();
        }
        public void CompleteDraw()
        {
            RasterBackendStubs.CompleteCalls++;
            if (RasterBackendStubs.Complete != null) RasterBackendStubs.Complete();
        }
        public static void ReleaseSurface(Windows.UI.Xaml.Media.Imaging.SurfaceImageSource surface)
        {
            if (surface.Released) throw new Exception("Surface was released twice.");
            surface.Released = true;
            RasterBackendStubs.ReleasedSurfaces++;
        }
    }
}

namespace Windows.Foundation { public struct Rect {} }
namespace Windows.Storage.Streams
{
    public sealed class InMemoryRandomAccessStream : IDisposable
    {
        public void Seek(ulong offset) {}
        public void Dispose() {}
    }
}
namespace Windows.Data.Pdf
{
    public sealed class PdfPage
    {
        public Task RenderToStreamAsync(Windows.Storage.Streams.InMemoryRandomAccessStream stream, PdfPageRenderOptions options)
        {
            RasterBackendStubs.FallbackCalls++;
            return RasterBackendStubs.Render == null ? Task.FromResult(0) : RasterBackendStubs.Render();
        }
    }
    public sealed class PdfPageRenderOptions
    {
        public uint DestinationWidth, DestinationHeight;
        public Windows.Foundation.Rect SourceRect;
        public Guid BitmapEncoderId;
    }
}
namespace Windows.Graphics.Imaging
{
    public enum BitmapPixelFormat { Bgra8 }
    public enum BitmapAlphaMode { Premultiplied }
    public static class BitmapEncoder { public static Guid BmpEncoderId = Guid.Empty; }
    public sealed class SoftwareBitmap : IDisposable
    {
        private bool disposed;
        public SoftwareBitmap() { RasterBackendStubs.LivePixels++; }
        public void Dispose()
        {
            if (disposed) throw new Exception("Pixels were disposed twice.");
            disposed = true;
            RasterBackendStubs.LivePixels--;
        }
    }
    public sealed class BitmapDecoder
    {
        public static Task<BitmapDecoder> CreateAsync(Windows.Storage.Streams.InMemoryRandomAccessStream stream)
        {
            return RasterBackendStubs.OpenDecoder == null ? Task.FromResult(new BitmapDecoder()) : RasterBackendStubs.OpenDecoder();
        }
        public Task<SoftwareBitmap> GetSoftwareBitmapAsync(BitmapPixelFormat format, BitmapAlphaMode alpha)
        {
            RasterBackendStubs.DecodeCalls++;
            return RasterBackendStubs.Decode == null ? Task.FromResult(new SoftwareBitmap()) : RasterBackendStubs.Decode();
        }
    }
}
namespace Windows.UI.Xaml.Media { public abstract class ImageSource {} }
namespace Windows.UI.Xaml.Media.Imaging
{
    public sealed class SurfaceImageSource : Windows.UI.Xaml.Media.ImageSource { public bool Released; }
    public sealed class SoftwareBitmapSource : Windows.UI.Xaml.Media.ImageSource, IDisposable
    {
        private bool disposed;
        public SoftwareBitmapSource() { RasterBackendStubs.SourcesCreated++; RasterBackendStubs.LiveSources++; }
        public Task SetBitmapAsync(Windows.Graphics.Imaging.SoftwareBitmap bitmap)
        {
            return RasterBackendStubs.Upload == null ? Task.FromResult(0) : RasterBackendStubs.Upload();
        }
        public void Dispose()
        {
            if (disposed) throw new Exception("Image source was disposed twice.");
            disposed = true;
            RasterBackendStubs.LiveSources--;
        }
    }
}
