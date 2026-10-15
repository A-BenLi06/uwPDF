#include "PdfSurfaceRenderer.h"
#include <winrt/Windows.Data.Pdf.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>

namespace
{
    Microsoft::WRL::ComPtr<ISurfaceImageSourceNativeWithD2D> SurfaceNative(
        winrt::Windows::UI::Xaml::Media::Imaging::SurfaceImageSource const& surface)
    {
        if (!surface) throw winrt::hresult_invalid_argument();
        Microsoft::WRL::ComPtr<ISurfaceImageSourceNativeWithD2D> native;
        winrt::check_hresult(winrt::get_unknown(surface)->QueryInterface(IID_PPV_ARGS(&native)));
        return native;
    }
}

namespace winrt::PdfNative::Rendering::implementation
{
    PdfSurfaceRenderer::PdfSurfaceRenderer()
    {
        check_hresult(renderDevice.Initialize());
    }

    Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfSurfaceRenderer> PdfSurfaceRenderer::CreateAsync()
    {
        co_await resume_background();
        co_return winrt::make<PdfSurfaceRenderer>();
    }

    bool PdfSurfaceRenderer::IsHardwareAccelerated() const
    {
        return renderDevice.IsHardwareAccelerated();
    }

    Windows::UI::Xaml::Media::Imaging::SurfaceImageSource PdfSurfaceRenderer::CreateSurface(int32_t width, int32_t height)
    {
        if (width < 1 || height < 1) throw hresult_invalid_argument();
        Windows::UI::Xaml::Media::Imaging::SurfaceImageSource surface(width, height, true);
        check_hresult(renderDevice.BindSurface(SurfaceNative(surface).Get()));
        return surface;
    }

    Windows::Foundation::IAsyncAction PdfSurfaceRenderer::DrawAsync(
        Windows::UI::Xaml::Media::Imaging::SurfaceImageSource surface,
        Windows::Data::Pdf::PdfPage page, int32_t width, int32_t height,
        Windows::Foundation::Rect sourceRect)
    {
        // The coroutine owns the renderer, page and surface across suspension.
        // BeginDraw/RenderPage run on a worker; CompleteDraw remains a UI call.
        auto lifetime = get_strong();
        if (!page || width < 1 || height < 1) throw hresult_invalid_argument();
        auto native = SurfaceNative(surface);
        {
            std::lock_guard<std::mutex> lock(drawMutex);
            if (phase != DrawPhase::Idle) throw hresult_illegal_method_call(L"Complete the previous draw first.");
            phase = DrawPhase::Rendering;
        }
        try
        {
            co_await resume_background();
            RECT update = { 0, 0, width, height };
            POINT offset = {};
            Microsoft::WRL::ComPtr<IDXGISurface> target;
            check_hresult(native->BeginDraw(update, IID_PPV_ARGS(&target), &offset));
            {
                std::lock_guard<std::mutex> lock(drawMutex);
                drawingSurface = native;
            }
            const auto rectangle = D2D1::RectF(sourceRect.X, sourceRect.Y,
                sourceRect.X + sourceRect.Width, sourceRect.Y + sourceRect.Height);
            check_hresult(renderDevice.RenderPage(winrt::get_unknown(page), target.Get(), offset, width, height, rectangle));
        }
        catch (...)
        {
            std::lock_guard<std::mutex> lock(drawMutex);
            phase = drawingSurface ? DrawPhase::Ready : DrawPhase::Idle;
            throw;
        }
        std::lock_guard<std::mutex> lock(drawMutex);
        phase = DrawPhase::Ready;
    }

    void PdfSurfaceRenderer::CompleteDraw()
    {
        Microsoft::WRL::ComPtr<ISurfaceImageSourceNativeWithD2D> surface;
        {
            std::lock_guard<std::mutex> lock(drawMutex);
            if (phase == DrawPhase::Rendering)
                throw hresult_illegal_method_call(L"Await DrawAsync before completing the draw.");
            surface = std::move(drawingSurface);
            phase = DrawPhase::Idle;
        }
        if (surface) check_hresult(surface->EndDraw());
    }

    void PdfSurfaceRenderer::ReleaseSurface(Windows::UI::Xaml::Media::Imaging::SurfaceImageSource const& surface)
    {
        if (surface) check_hresult(SurfaceNative(surface)->SetDevice(nullptr));
    }
}
