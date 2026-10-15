#pragma once
#include "PdfSurfaceRenderer.g.h"
#include "../PdfRenderDevice.h"
#include <mutex>

namespace winrt::PdfNative::Rendering::implementation
{
    struct PdfSurfaceRenderer : PdfSurfaceRendererT<PdfSurfaceRenderer>
    {
        PdfSurfaceRenderer();
        static Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfSurfaceRenderer> CreateAsync();
        bool IsHardwareAccelerated() const;
        Windows::UI::Xaml::Media::Imaging::SurfaceImageSource CreateSurface(int32_t width, int32_t height);
        Windows::Foundation::IAsyncAction DrawAsync(
            Windows::UI::Xaml::Media::Imaging::SurfaceImageSource surface,
            Windows::Data::Pdf::PdfPage page, int32_t width, int32_t height,
            Windows::Foundation::Rect sourceRect);
        void CompleteDraw();
        static void ReleaseSurface(Windows::UI::Xaml::Media::Imaging::SurfaceImageSource const& surface);

    private:
        enum class DrawPhase { Idle, Rendering, Ready };
        PdfNativeCore::PdfRenderDevice renderDevice;
        std::mutex drawMutex;
        DrawPhase phase = DrawPhase::Idle;
        Microsoft::WRL::ComPtr<ISurfaceImageSourceNativeWithD2D> drawingSurface;
    };
}

namespace winrt::PdfNative::Rendering::factory_implementation
{
    struct PdfSurfaceRenderer : PdfSurfaceRendererT<PdfSurfaceRenderer, implementation::PdfSurfaceRenderer> {};
}
