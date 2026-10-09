#include "PdfAnnotationWriter.h"
#include "PdfStreamWinRT.h"
using namespace PdfNativeCore;
namespace winrt::PdfNative::Rendering::implementation
{
    Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfAnnotationWriter> PdfAnnotationWriter::OpenAsync(Windows::Storage::Streams::IRandomAccessStream stream)
    {
        if (!stream) throw hresult_invalid_argument();
        auto input = std::make_shared<WinRTPdfStream>(stream.CloneStream());
        co_await resume_background();
        co_return TranslateWinRT([&] { return winrt::make<PdfAnnotationWriter>(OpenDocument(input)); });
    }
    Windows::Foundation::IAsyncAction PdfAnnotationWriter::AppendAsync(hstring annotations)
    {
        auto owner = get_strong();
        auto lifetime = std::atomic_load(&state);
        if (!lifetime) throw hresult_error(RO_E_CLOSED);
        co_await resume_background();
        TranslateWinRT([&] { auto items = ParseAnnotations(annotations.c_str(), annotations.size()); AppendAnnotations(lifetime, items); });
    }
    Windows::Foundation::IAsyncOperation<Windows::Foundation::Size> PdfAnnotationWriter::ReadPageSizeAsync(uint32_t pageIndex)
    {
        auto owner = get_strong();
        auto lifetime = std::atomic_load(&state);
        if (!lifetime) throw hresult_error(RO_E_CLOSED);
        co_await resume_background();
        co_return TranslateWinRT([&] { auto size = ReadPageSize(lifetime, pageIndex); return Windows::Foundation::Size{size.first, size.second}; });
    }
    Windows::Foundation::IAsyncAction PdfAnnotationWriter::WriteAsync(Windows::Storage::Streams::IRandomAccessStream stream)
    {
        auto owner = get_strong();
        if (!stream) throw hresult_invalid_argument();
        auto lifetime = std::atomic_load(&state);
        if (!lifetime) throw hresult_error(RO_E_CLOSED);
        auto destination = std::make_shared<WinRTPdfStream>(stream.CloneStream());
        co_await resume_background();
        TranslateWinRT([&] { WriteDocument(lifetime, destination); });
    }
}
