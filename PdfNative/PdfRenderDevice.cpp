#include "PdfRenderDevice.h"
#include <d3d10.h>

HRESULT PdfNativeCore::PdfRenderDevice::Initialize()
{
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0, D3D_FEATURE_LEVEL_9_3,
        D3D_FEATURE_LEVEL_9_2, D3D_FEATURE_LEVEL_9_1 };
    auto result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, ARRAYSIZE(levels), D3D11_SDK_VERSION,
        &device, nullptr, nullptr);
    hardware = SUCCEEDED(result);
    if (!hardware)
    {
        result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, ARRAYSIZE(levels), D3D11_SDK_VERSION,
            &device, nullptr, nullptr);
        if (FAILED(result)) return result;
    }

    // XAML BeginDraw and the PDF worker share the device and immediate context.
    Microsoft::WRL::ComPtr<ID3D10Multithread> multithread;
    result = device.As(&multithread);
    if (FAILED(result)) return result;
    multithread->SetMultithreadProtected(TRUE);
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgi;
    result = device.As(&dxgi);
    if (FAILED(result)) return result;
    D2D1_CREATION_PROPERTIES properties = {};
    properties.threadingMode = D2D1_THREADING_MODE_MULTI_THREADED;
    result = D2D1CreateDevice(dxgi.Get(), &properties, &d2dDevice);
    if (FAILED(result)) return result;
    return PdfCreateRenderer(dxgi.Get(), &renderer);
}

HRESULT PdfNativeCore::PdfRenderDevice::BindSurface(ISurfaceImageSourceNativeWithD2D* surface) const
{
    if (!surface) return E_INVALIDARG;
    if (!d2dDevice) return E_UNEXPECTED;
    return surface->SetDevice(d2dDevice.Get());
}

HRESULT PdfNativeCore::PdfRenderDevice::RenderPage(IUnknown* page, IDXGISurface* surface,
    POINT offset, unsigned int width, unsigned int height, const D2D1_RECT_F& sourceRect) const
{
    if (!page || !surface || !width || !height) return E_INVALIDARG;
    if (!renderer) return E_UNEXPECTED;
    auto parameters = PdfRenderParams();
    parameters.DestinationWidth = width;
    parameters.DestinationHeight = height;
    if (sourceRect.right > sourceRect.left && sourceRect.bottom > sourceRect.top)
        parameters.SourceRect = sourceRect;
    return renderer->RenderPageToSurface(page, surface, offset, &parameters);
}
