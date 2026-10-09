#include <windows.h>
#include <roapi.h>
#undef GetCurrentTime
#include <winrt/Windows.Data.Pdf.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/PdfNative.Rendering.h>
#include "../PdfNative/PdfRenderDevice.h"
#include <algorithm>
#include <iostream>
#include <memory>

namespace
{
    void Require(bool condition, char const* message)
    {
        if (!condition) throw std::runtime_error(message);
    }

    template<typename F> void InvalidArgument(F&& action)
    {
        try { action(); }
        catch (winrt::hresult_error const& error)
        {
            Require(error.code() == E_INVALIDARG, "Expected E_INVALIDARG across the WinRT ABI.");
            return;
        }
        throw std::runtime_error("Invalid arguments were accepted.");
    }

    void CheckComponent(HMODULE module)
    {
        using GetFactory = HRESULT(WINAPI*)(HSTRING, void**);
        auto getFactory = reinterpret_cast<GetFactory>(GetProcAddress(module, "DllGetActivationFactory"));
        Require(getFactory != nullptr, "Missing DLL activation export.");
        winrt::Windows::Foundation::IActivationFactory factory{ nullptr };
        winrt::hstring className(L"PdfNative.Rendering.PdfSurfaceRenderer");
        winrt::check_hresult(getFactory(static_cast<HSTRING>(winrt::get_abi(className)), winrt::put_abi(factory)));
        auto renderer = factory.ActivateInstance<winrt::PdfNative::Rendering::PdfSurfaceRenderer>();
        std::cout << "DLL activation and DirectX device: " << (renderer.IsHardwareAccelerated() ? "hardware" : "WARP") << '\n';
        renderer.CompleteDraw();
        InvalidArgument([&] { renderer.CreateSurface(0, 200); });
        InvalidArgument([&] { renderer.CreateSurface(200, -1); });
        InvalidArgument([&] { renderer.DrawAsync(nullptr, nullptr, 200, 200, {}).get(); });
        factory.as<winrt::PdfNative::Rendering::IPdfSurfaceRendererStatics>().ReleaseSurface(nullptr);
        {
            auto operation = factory.as<winrt::PdfNative::Rendering::IPdfSurfaceRendererStatics>().CreateAsync();
            auto asyncRenderer = operation.get();
            asyncRenderer.CompleteDraw();
            auto asyncWeak = winrt::make_weak(asyncRenderer);
            asyncRenderer = nullptr;
            operation = nullptr;
            Require(!asyncWeak.get(), "The async factory retained its result after release.");
        }
        auto weak = winrt::make_weak(renderer);
        auto failedAction = renderer.DrawAsync(nullptr, nullptr, 0, 0, {});
        InvalidArgument([&] { failedAction.get(); });
        renderer = nullptr;
        Require(!weak.get(), "A completed failed coroutine retained the renderer.");
    }

    void CheckPixels(wchar_t const* inputPath)
    {
        // Production core, not a duplicate rendering implementation. This is a
        // headless image check; XAML SurfaceImageSource presentation needs UI QA.
        auto file = winrt::Windows::Storage::StorageFile::GetFileFromPathAsync(inputPath).get();
        auto document = winrt::Windows::Data::Pdf::PdfDocument::LoadFromFileAsync(file).get();
        Require(document.PageCount() > 0, "The input PDF has no pages.");
        auto page = document.GetPage(0);
        PdfNativeCore::PdfRenderDevice core;
        winrt::check_hresult(core.Initialize());
        Require(core.BindSurface(nullptr) == E_INVALIDARG, "Core null-surface validation failed.");
        const auto size = page.Size();
        Require(size.Width > 0 && size.Height > 0, "The input page has invalid dimensions.");
        const unsigned int width = 384;
        const auto height = static_cast<unsigned int>(std::max(1.0f, width * size.Height / size.Width));
        D3D11_TEXTURE2D_DESC description = {};
        description.Width = width; description.Height = height;
        description.MipLevels = description.ArraySize = description.SampleDesc.Count = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;
        winrt::check_hresult(core.Device()->CreateTexture2D(&description, nullptr, &texture));
        Microsoft::WRL::ComPtr<IDXGISurface> surface;
        winrt::check_hresult(texture.As(&surface));
        winrt::check_hresult(core.RenderPage(winrt::get_unknown(page), surface.Get(), {}, width, height, {}));
        description.BindFlags = 0;
        description.Usage = D3D11_USAGE_STAGING;
        description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> staging;
        winrt::check_hresult(core.Device()->CreateTexture2D(&description, nullptr, &staging));
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
        core.Device()->GetImmediateContext(&context);
        context->CopyResource(staging.Get(), texture.Get());
        D3D11_MAPPED_SUBRESOURCE mapped = {};
        winrt::check_hresult(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped));
        unsigned int coloredPixels = 0;
        unsigned int minimumLightness = 765, maximumLightness = 0;
        for (unsigned int y = 0; y < height; ++y)
        {
            auto row = static_cast<unsigned char*>(mapped.pData) + y * mapped.RowPitch;
            for (unsigned int x = 0; x < width; ++x)
            {
                if (row[x * 4 + 3] == 0) continue;
                const unsigned int lightness = row[x * 4] + row[x * 4 + 1] + row[x * 4 + 2];
                minimumLightness = std::min(minimumLightness, lightness);
                maximumLightness = std::max(maximumLightness, lightness);
                if (row[x * 4] < 240 || row[x * 4 + 1] < 240 || row[x * 4 + 2] < 240) ++coloredPixels;
            }
        }
        context->Unmap(staging.Get(), 0);
        Require(coloredPixels > width, "The production renderer returned a blank image.");
        Require(maximumLightness > minimumLightness + 64, "The production renderer returned a uniform image.");
        page.Close();
        std::cout << "Production DirectX pixels: " << coloredPixels << " nonwhite pixels\n";
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) return 2;
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    try
    {
        auto module = LoadLibraryExW(argv[1], nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        winrt::check_bool(module != nullptr);
        struct FreeModule { void operator()(HINSTANCE__* handle) const { FreeLibrary(handle); } };
        std::unique_ptr<HINSTANCE__, FreeModule> moduleLifetime(module);
        CheckComponent(module);
        using CanUnload = HRESULT(WINAPI*)();
        auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(module, "DllCanUnloadNow"));
        Require(canUnload && canUnload() == S_OK, "The idle component cannot unload after its objects were released.");
        CheckPixels(argv[2]);
        std::cout << "PASS: C++/WinRT activation, validation, coroutine lifetime, unload and shared-core raster\n";
    }
    catch (winrt::hresult_error const& error)
    {
        std::wcerr << error.message().c_str() << L" (HRESULT " << std::hex << error.code().value << L")\n";
        return 1;
    }
    catch (std::exception const& error) { std::cerr << error.what() << '\n'; return 1; }
    return 0;
}
