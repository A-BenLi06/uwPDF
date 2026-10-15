#define NOMINMAX
#include <windows.h>
#include <roapi.h>
#include <psapi.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.data.pdf.interop.h>
#include <wrl.h>
#include <ppltasks.h>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <algorithm>
#include <cmath>
#include <memory>
#include "../PdfNative/PdfRenderDevice.h"

using namespace Microsoft::WRL;
using namespace concurrency;
using namespace Windows::Storage;
using namespace Windows::Data::Pdf;

namespace
{
    void Check(HRESULT result)
    {
        if (FAILED(result)) throw Platform::Exception::CreateException(result);
    }
    double Milliseconds()
    {
        LARGE_INTEGER ticks, frequency;
        QueryPerformanceCounter(&ticks);
        QueryPerformanceFrequency(&frequency);
        return ticks.QuadPart * 1000.0 / frequency.QuadPart;
    }
    PROCESS_MEMORY_COUNTERS_EX Memory()
    {
        PROCESS_MEMORY_COUNTERS_EX counters = {};
        counters.cb = sizeof(counters);
        Check(GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters)) ? S_OK : HRESULT_FROM_WIN32(GetLastError()));
        return counters;
    }
    struct CloseEvent
    {
        void operator()(void* handle) const { CloseHandle(handle); }
    };
}

// Engine benchmark only: no XAML controls, input, scheduler or app frame timing.
// Render completion includes an explicit GPU fence, not just command submission.
int wmain(int argc, wchar_t** argv)
{
    if (argc != 5) return 2;
    Check(RoInitialize(RO_INIT_MULTITHREADED));
    try
    {
        const auto width = std::max(1, _wtoi(argv[3]));
        const auto requestedSamples = std::max(1, _wtoi(argv[4]));
        const auto baseline = Memory();
        auto start = Milliseconds();
        auto file = create_task(StorageFile::GetFileFromPathAsync(ref new Platform::String(argv[1]))).get();
        auto document = create_task(PdfDocument::LoadFromFileAsync(file)).get();
        const auto loadMs = Milliseconds() - start;
        if (!document->PageCount) return 3;

        PdfNativeCore::PdfRenderDevice renderDevice;
        Check(renderDevice.Initialize());
        ComPtr<ID3D11Device> device(renderDevice.Device());
        ComPtr<ID3D11DeviceContext> context;
        device->GetImmediateContext(&context);
        const bool hardware = renderDevice.IsHardwareAccelerated();
        ComPtr<IDXGIDevice2> completionDevice;
        Check(device.As(&completionDevice));
        std::unique_ptr<void, CloseEvent> completionEvent(CreateEvent(nullptr, FALSE, FALSE, nullptr));
        if (!completionEvent) Check(HRESULT_FROM_WIN32(GetLastError()));
        ComPtr<IDXGISurface> surface;
        unsigned int surfaceHeight = 0;

        std::ofstream report(argv[2]);
        if (!report) return 4;
        report << std::setprecision(9) << "{\"scope\":\"Shared production Windows.Data.Pdf render core; not app input/frame latency\","
            << "\"hardware\":" << (hardware ? "true" : "false") << ",\"page_count\":" << document->PageCount
            << ",\"width\":" << width << ",\"load_ms\":" << loadMs
            << ",\"baseline_private_bytes\":" << baseline.PrivateUsage << ",\"samples\":[";
        const auto samples = std::min(static_cast<unsigned int>(requestedSamples), document->PageCount);
        bool first = true;
        for (unsigned int phase = 0; phase < 4; ++phase)
        {
            for (unsigned int i = 0; i < samples; ++i)
            {
                const auto index = phase == 0 ? i : phase == 1 ? document->PageCount - 1 - i : (i * 97) % document->PageCount;
                start = Milliseconds();
                auto page = document->GetPage(index);
                const auto size = page->Size;
                const auto pageMs = Milliseconds() - start;
                if (!(size.Width > 0 && size.Height > 0)) throw ref new Platform::FailureException(L"Invalid PDF page size.");
                const auto height = static_cast<unsigned int>(std::max(1.0, std::floor(static_cast<double>(width) * size.Height / size.Width)));
                if (!surface || surfaceHeight != height)
                {
                    D3D11_TEXTURE2D_DESC description = {};
                    description.Width = width; description.Height = height;
                    description.MipLevels = description.ArraySize = description.SampleDesc.Count = 1;
                    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
                    description.Usage = D3D11_USAGE_DEFAULT;
                    description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
                    ComPtr<ID3D11Texture2D> texture;
                    Check(device->CreateTexture2D(&description, nullptr, &texture));
                    surface.Reset();
                    Check(texture.As(&surface));
                    surfaceHeight = height;
                }
                POINT offset = {};
                D2D1_RECT_F sourceRect = {};
                start = Milliseconds();
                Check(renderDevice.RenderPage(reinterpret_cast<IUnknown*>(page), surface.Get(), offset, width, height, sourceRect));
                const auto renderMs = Milliseconds() - start;
                context->Flush();
                // An event avoids Sleep(1)'s coarse system-timer rounding, which
                // would contaminate a short render with artificial ~16-ms steps.
                Check(completionDevice->EnqueueSetEvent(completionEvent.get()));
                const auto waitResult = WaitForSingleObject(completionEvent.get(), 10000);
                if (waitResult != WAIT_OBJECT_0) throw ref new Platform::FailureException(L"GPU completion wait failed or timed out.");
                Check(device->GetDeviceRemovedReason());
                const auto completeMs = Milliseconds() - start;
                const auto memory = Memory();
                if (!first) report << ',';
                first = false;
                report << "{\"phase\":" << phase << ",\"page\":" << index << ",\"get_page_ms\":" << pageMs
                    << ",\"render_ms\":" << renderMs << ",\"gpu_complete_ms\":" << completeMs
                    << ",\"private_bytes\":" << memory.PrivateUsage << ",\"working_set_bytes\":" << memory.WorkingSetSize << '}';
                delete page;
            }
        }
        const auto finalMemory = Memory();
        report << "],\"final_private_bytes\":" << finalMemory.PrivateUsage
            << ",\"peak_working_set_bytes\":" << finalMemory.PeakWorkingSetSize << '}';
        report.close();
        if (!report) return 5;
        std::cout << "PASS: " << samples * 4 << " engine renders, " << width << "px, " << (hardware ? "hardware" : "WARP") << '\n';
        RoUninitialize();
        return 0;
    }
    catch (Platform::Exception^ error)
    {
        std::wcerr << error->Message->Data() << L" (HRESULT " << std::hex << error->HResult << L")\n";
        RoUninitialize();
        return 1;
    }
}
