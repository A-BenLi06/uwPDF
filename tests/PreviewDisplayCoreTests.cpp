#include "../PdfNative/PreviewDisplayCore.h"
#include <iostream>
#include <limits>
#include <stdexcept>
using namespace PdfNativeCore;
using Microsoft::WRL::ComPtr;

#define INSPECTABLE_METHODS(Interface, Guid) \
    ULONG refs = 1; bool supports = true; bool nullQuery = false; \
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** value) override { \
        if (!value) return E_POINTER; *value = nullptr; \
        if (id == Guid) { if (!supports) return E_NOINTERFACE; if (nullQuery) return S_OK; } \
        else if (id != __uuidof(IUnknown) && id != __uuidof(IInspectable)) return E_NOINTERFACE; \
        *value = static_cast<Interface*>(this); AddRef(); return S_OK; } \
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; } \
    ULONG STDMETHODCALLTYPE Release() override { auto count = --refs; if (!count) delete this; return count; } \
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count, IID** ids) override { *count = 0; *ids = nullptr; return S_OK; } \
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* value) override { *value = nullptr; return S_OK; } \
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* value) override { *value = BaseTrust; return S_OK; }

struct Region : DisplayRegionAbi
{
    INSPECTABLE_METHODS(DisplayRegionAbi, __uuidof(DisplayRegionAbi));
    boolean visible = true;
    HRESULT visibleResult = S_OK, sizeResult = S_OK;
    ABI::Windows::Foundation::Size size = { 1376, 756 };
    HRESULT STDMETHODCALLTYPE get_DisplayMonitorDeviceId(HSTRING*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE get_IsVisible(boolean* value) override { *value = visible; return visibleResult; }
    HRESULT STDMETHODCALLTYPE get_WorkAreaOffset(ABI::Windows::Foundation::Point*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE get_WorkAreaSize(ABI::Windows::Foundation::Size* value) override { *value = size; return sizeResult; }
};
struct Regions : DisplayRegionsAbi
{
    INSPECTABLE_METHODS(DisplayRegionsAbi, __uuidof(IInspectable));
    ComPtr<Region> region;
    unsigned int count = 1, getCalls = 0;
    HRESULT countResult = S_OK, getResult = S_OK;
    bool nullRegion = false;
    HRESULT STDMETHODCALLTYPE GetAt(unsigned int index, DisplayRegionAbi** value) override
    {
        ++getCalls; *value = nullptr;
        if (FAILED(getResult)) return getResult;
        if (!nullRegion && index == 0) { *value = region.Get(); (*value)->AddRef(); }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Size(unsigned int* value) override { *value = count; return countResult; }
};
struct View : ApplicationView9Abi
{
    INSPECTABLE_METHODS(ApplicationView9Abi, __uuidof(ApplicationView9Abi));
    ComPtr<Regions> regions;
    HRESULT regionsResult = S_OK;
    bool nullRegions = false;
    HRESULT STDMETHODCALLTYPE get_WindowingEnvironment(IInspectable**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetDisplayRegions(DisplayRegionsAbi** value) override
    {
        *value = nullptr;
        if (FAILED(regionsResult)) return regionsResult;
        if (!nullRegions) { *value = regions.Get(); (*value)->AddRef(); }
        return S_OK;
    }
};
void Require(bool condition, char const* message) { if (!condition) throw std::runtime_error(message); }
int main()
{
    try
    {
        auto unavailable = TryGetWorkArea(nullptr);
        Require(unavailable.Width == 0 && unavailable.Height == 0, "Null view did not fall back.");
        for (int scenario = 0; scenario < 17; ++scenario)
        {
            ComPtr<Region> region; region.Attach(new Region());
            ComPtr<Regions> regions; regions.Attach(new Regions()); regions->region = region;
            ComPtr<View> view; view.Attach(new View()); view->regions = regions;
            switch (scenario)
            {
                case 1: view->supports = false; break;
                case 2: view->regionsResult = E_FAIL; break;
                case 3: view->nullRegions = true; break;
                case 4: regions->countResult = E_FAIL; break;
                case 5: regions->count = 0; break;
                case 6: regions->count = 2; break;
                case 7: regions->getResult = E_FAIL; break;
                case 8: regions->nullRegion = true; break;
                case 9: region->visibleResult = E_FAIL; break;
                case 10: region->visible = false; break;
                case 11: region->sizeResult = E_FAIL; break;
                case 12: region->size.Width = 0; break;
                case 13: region->size.Height = -1; break;
                case 14: region->size.Width = std::numeric_limits<float>::infinity(); break;
                case 15: region->size.Height = std::numeric_limits<float>::quiet_NaN(); break;
                case 16: view->nullQuery = true; break;
            }
            const auto viewRefs = view->refs, regionsRefs = regions->refs, regionRefs = region->refs;
            auto size = TryGetWorkArea(view.Get());
            Require(scenario == 0 ? size.Width == 1376 && size.Height == 756 : size.Width == 0 && size.Height == 0,
                "Wrong result for unavailable, ambiguous, hidden or malformed work area.");
            Require(view->refs == viewRefs && regions->refs == regionsRefs && region->refs == regionRefs, "Display query leaked an interface.");
            if (scenario == 5 || scenario == 6) Require(regions->getCalls == 0, "Ambiguous region query guessed a monitor.");
        }
        std::cout << "PASS: production display-region core, null/old view, API failures, ambiguous regions, visibility, dimensions and COM lifetime\n";
        return 0;
    }
    catch (std::exception const& error) { std::cerr << error.what() << '\n'; return 1; }
}
