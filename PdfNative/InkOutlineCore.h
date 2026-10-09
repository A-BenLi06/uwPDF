#pragma once
#include <d3d11.h>
#include <d2d1_3.h>
#include <inkrenderer.h>
#include <wrl/client.h>
#include <mutex>
#include <string>

namespace PdfNativeCore
{
    // Projection-independent Windows Ink outline capture, shared by both bridges.
    class InkOutlineCore
    {
    public:
        HRESULT Initialize() noexcept;
        HRESULT Describe(IUnknown* strokes, double width, double height, std::wstring* result) noexcept;
        void Close() noexcept;
    private:
        Microsoft::WRL::ComPtr<ID2D1Factory1> factory;
        Microsoft::WRL::ComPtr<ID3D11Device> device;
        Microsoft::WRL::ComPtr<ID2D1DeviceContext2> context;
        std::mutex gate;
        bool initialized = false;
        bool closed = false;
    };
}
