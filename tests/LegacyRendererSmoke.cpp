#include "../PdfNative/PdfSurfaceRenderer.h"
#include <roapi.h>
#include <ppltasks.h>
#include <iostream>

using namespace concurrency;

int wmain()
{
    if (FAILED(RoInitialize(RO_INIT_MULTITHREADED))) return 2;
    try
    {
        auto renderer = create_task(PdfNative::PdfSurfaceRenderer::CreateAsync()).get();
        std::cout << "Legacy async factory device: " << (renderer->IsHardwareAccelerated ? "hardware" : "WARP") << '\n';
        renderer->CompleteDraw();
        bool rejected = false;
        try { renderer->CreateSurface(0, 200); }
        catch (Platform::Exception^ error) { rejected = error->HResult == E_INVALIDARG; }
        if (!rejected) return 3;
        rejected = false;
        try { renderer->DrawAsync(nullptr, nullptr, 200, 200, Windows::Foundation::Rect()); }
        catch (Platform::Exception^ error) { rejected = error->HResult == E_INVALIDARG; }
        if (!rejected) return 4;
        renderer = nullptr;
        std::cout << "PASS: legacy async renderer factory and argument guards\n";
    }
    catch (Platform::Exception^ error)
    {
        std::wcerr << error->Message->Data() << L" (HRESULT " << std::hex << error->HResult << L")\n";
        return 1;
    }
    RoUninitialize();
    return 0;
}
