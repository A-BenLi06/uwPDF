#include "PdfSurfaceRenderer.h"
#include <ppltasks.h>

using namespace Microsoft::WRL;
using namespace Windows::UI::Xaml::Media::Imaging;
using namespace Windows::Foundation;
using namespace Windows::Data::Pdf;
using namespace concurrency;

namespace
{
    void Check(HRESULT result)
    {
        if (FAILED(result)) throw Platform::Exception::CreateException(result);
    }
    ComPtr<ISurfaceImageSourceNativeWithD2D> SurfaceNative(SurfaceImageSource^ surface)
    {
        if (surface == nullptr) throw ref new Platform::InvalidArgumentException();
        ComPtr<ISurfaceImageSourceNativeWithD2D> native;
        Check(reinterpret_cast<IUnknown*>(surface)->QueryInterface(IID_PPV_ARGS(&native)));
        return native;
    }
}

PdfNative::PdfSurfaceRenderer::PdfSurfaceRenderer()
    : renderDevice(new PdfNativeCore::PdfRenderDevice())
{
    Check(renderDevice->Initialize());
}

IAsyncOperation<PdfNative::PdfSurfaceRenderer^>^ PdfNative::PdfSurfaceRenderer::CreateAsync()
{
    // Device/renderer initialization needs no XAML objects and can be expensive
    // on a cold driver. Surface creation and EndDraw still belong to the UI.
    return create_async([] { return ref new PdfSurfaceRenderer(); });
}

SurfaceImageSource^ PdfNative::PdfSurfaceRenderer::CreateSurface(int width, int height)
{
    if (width < 1 || height < 1) throw ref new Platform::InvalidArgumentException();
    auto surface = ref new SurfaceImageSource(width, height, true);
    Check(renderDevice->BindSurface(SurfaceNative(surface).Get()));
    return surface;
}

IAsyncAction^ PdfNative::PdfSurfaceRenderer::DrawAsync(SurfaceImageSource^ surface,
    PdfPage^ page, int width, int height, Rect sourceRect)
{
    if (page == nullptr || width < 1 || height < 1) throw ref new Platform::InvalidArgumentException();
    if (drawingSurface) throw ref new Platform::FailureException(L"Complete the previous draw first.");
    auto native = SurfaceNative(surface);
    ComPtr<IUnknown> nativePage(reinterpret_cast<IUnknown*>(page));
    PdfSurfaceRenderer^ lifetime = this;
    return create_async([lifetime, native, nativePage, width, height, sourceRect]()
    {
        RECT update = { 0, 0, width, height };
        POINT offset = {};
        ComPtr<IDXGISurface> target;
        Check(native->BeginDraw(update, IID_PPV_ARGS(&target), &offset));
        // CompleteDraw runs on the UI thread even if PDF rendering throws.
        lifetime->drawingSurface = native;
        const auto rectangle = D2D1::RectF(sourceRect.X, sourceRect.Y,
            sourceRect.X + sourceRect.Width, sourceRect.Y + sourceRect.Height);
        Check(lifetime->renderDevice->RenderPage(nativePage.Get(), target.Get(), offset, width, height, rectangle));
    });
}

void PdfNative::PdfSurfaceRenderer::CompleteDraw()
{
    // Microsoft requires EndDraw on the UI thread; the C# caller awaits the
    // worker and calls this in finally on its captured XAML synchronization context.
    auto surface = drawingSurface;
    drawingSurface.Reset();
    if (surface) Check(surface->EndDraw());
}

void PdfNative::PdfSurfaceRenderer::ReleaseSurface(SurfaceImageSource^ surface)
{
    if (surface != nullptr) Check(SurfaceNative(surface)->SetDevice(nullptr));
}
