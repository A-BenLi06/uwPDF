#pragma once
#include <wrl/client.h>
#include <d3d11.h>
#include <d2d1_1.h>
#include <windows.data.pdf.interop.h>
#include <windows.ui.xaml.media.dxinterop.h>

// Shared by the legacy ARM32 ABI and the C++/WinRT ABI. This layer has no
// language-projection types, asynchronous tasks or managed pixel buffers.
namespace PdfNativeCore
{
    class PdfRenderDevice final
    {
    public:
        HRESULT Initialize();
        HRESULT BindSurface(ISurfaceImageSourceNativeWithD2D* surface) const;
        HRESULT RenderPage(IUnknown* page, IDXGISurface* surface, POINT offset,
            unsigned int width, unsigned int height, const D2D1_RECT_F& sourceRect) const;
        bool IsHardwareAccelerated() const { return hardware; }
        ID3D11Device* Device() const { return device.Get(); }

    private:
        Microsoft::WRL::ComPtr<ID3D11Device> device;
        Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice;
        Microsoft::WRL::ComPtr<IPdfRendererNative> renderer;
        bool hardware = false;
    };
}
