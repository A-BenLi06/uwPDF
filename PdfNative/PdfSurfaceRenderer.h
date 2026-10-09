#pragma once
#include "PdfRenderDevice.h"
#include <memory>

namespace PdfNative
{
    // The small C++/CX ABI keeps the VS2015/ARM32 app buildable. All rendering
    // below the ABI uses ordinary C++ and WRL; no managed pixel buffers exist.
    [Windows::Foundation::Metadata::WebHostHidden]
    public ref class PdfSurfaceRenderer sealed
    {
    public:
        PdfSurfaceRenderer();
        static Windows::Foundation::IAsyncOperation<PdfSurfaceRenderer^>^ CreateAsync();
        Windows::UI::Xaml::Media::Imaging::SurfaceImageSource^ CreateSurface(int width, int height);
        Windows::Foundation::IAsyncAction^ DrawAsync(
            Windows::UI::Xaml::Media::Imaging::SurfaceImageSource^ surface,
            Windows::Data::Pdf::PdfPage^ page, int width, int height,
            Windows::Foundation::Rect sourceRect);
        void CompleteDraw();
        static void ReleaseSurface(Windows::UI::Xaml::Media::Imaging::SurfaceImageSource^ surface);
        property bool IsHardwareAccelerated { bool get() { return renderDevice->IsHardwareAccelerated(); } }

    private:
        std::unique_ptr<PdfNativeCore::PdfRenderDevice> renderDevice;
        Microsoft::WRL::ComPtr<ISurfaceImageSourceNativeWithD2D> drawingSurface;
    };
}
