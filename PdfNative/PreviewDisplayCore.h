#pragma once
#include <windows.foundation.h>
#include <wrl/client.h>
#include <cmath>

namespace PdfNativeCore
{
    // Public UniversalApiContract 8 ABI prefixes, from the Windows SDK's
    // windows.ui.viewmanagement.h and windows.ui.windowmanagement.h. Querying
    // them at runtime keeps the 14393 minimum without loading a web engine.
    struct __declspec(uuid("db50c3a2-4094-5f47-8cb1-ea01ddafaa94")) DisplayRegionAbi : IInspectable
    {
        virtual HRESULT STDMETHODCALLTYPE get_DisplayMonitorDeviceId(HSTRING*) = 0;
        virtual HRESULT STDMETHODCALLTYPE get_IsVisible(boolean*) = 0;
        virtual HRESULT STDMETHODCALLTYPE get_WorkAreaOffset(ABI::Windows::Foundation::Point*) = 0;
        virtual HRESULT STDMETHODCALLTYPE get_WorkAreaSize(ABI::Windows::Foundation::Size*) = 0;
    };
    struct DisplayRegionsAbi : IInspectable
    {
        virtual HRESULT STDMETHODCALLTYPE GetAt(unsigned int, DisplayRegionAbi**) = 0;
        virtual HRESULT STDMETHODCALLTYPE get_Size(unsigned int*) = 0;
    };
    struct __declspec(uuid("9c6516f9-021a-5f01-93e5-9bdad2647574")) ApplicationView9Abi : IInspectable
    {
        virtual HRESULT STDMETHODCALLTYPE get_WindowingEnvironment(IInspectable**) = 0;
        virtual HRESULT STDMETHODCALLTYPE GetDisplayRegions(DisplayRegionsAbi**) = 0;
    };
    inline ABI::Windows::Foundation::Size TryGetWorkArea(IInspectable* view) noexcept
    {
        ABI::Windows::Foundation::Size unavailable = {};
        if (!view) return unavailable;
        Microsoft::WRL::ComPtr<ApplicationView9Abi> modernView;
        if (FAILED(view->QueryInterface(IID_PPV_ARGS(&modernView))) || !modernView) return unavailable;
        Microsoft::WRL::ComPtr<DisplayRegionsAbi> regions;
        if (FAILED(modernView->GetDisplayRegions(&regions)) || !regions) return unavailable;
        unsigned int count = 0;
        if (FAILED(regions->get_Size(&count)) || count != 1) return unavailable;
        // Do not guess a monitor when the view spans multiple display regions.
        // The legacy probe remains available for that case and older Windows.
        Microsoft::WRL::ComPtr<DisplayRegionAbi> region;
        if (FAILED(regions->GetAt(0, &region)) || !region) return unavailable;
        boolean visible = false;
        ABI::Windows::Foundation::Size size = {};
        if (FAILED(region->get_IsVisible(&visible)) || !visible || FAILED(region->get_WorkAreaSize(&size))) return unavailable;
        if (!std::isfinite(size.Width) || !std::isfinite(size.Height) || size.Width <= 0 || size.Height <= 0) return unavailable;
        return size;
    }
}
