#include "PdfTextDocument.h"
#include "PdfTextPage.h"
#include "PdfStreamWinRT.h"
using namespace PdfNativeCore;
namespace winrt::PdfNative::Rendering::implementation
{
    Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfTextDocument> PdfTextDocument::OpenAsync(Windows::Storage::Streams::IRandomAccessStream stream)
    {
        if (!stream) throw hresult_invalid_argument();
        auto input = std::make_shared<WinRTPdfStream>(stream.CloneStream());
        co_await resume_background();
        co_return TranslateWinRT([&] { return winrt::make<PdfTextDocument>(OpenDocument(input)); });
    }
    Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfTextPage> PdfTextDocument::ReadPageAsync(uint32_t pageIndex)
    {
        auto owner = get_strong();
        auto lifetime = std::atomic_load(&state);
        if (!lifetime) throw hresult_error(RO_E_CLOSED);
        co_await resume_background();
        co_return TranslateWinRT([&] { return winrt::make<PdfTextPage>(ReadPage(lifetime, pageIndex)); });
    }
    Windows::Foundation::IAsyncAction PdfTextDocument::ExportAsync(Windows::Storage::Streams::IRandomAccessStream input,
        Windows::Storage::Streams::IRandomAccessStream output, hstring annotations)
    {
        if (!input || !output) throw hresult_invalid_argument();
        auto source = std::make_shared<WinRTPdfStream>(input.CloneStream());
        auto destination = std::make_shared<WinRTPdfStream>(output.CloneStream());
        co_await resume_background();
        TranslateWinRT([&] { auto items = ParseAnnotations(annotations.c_str(), annotations.size());
            auto state = OpenDocument(source); AppendAnnotations(state, items); WriteDocument(state, destination); });
    }
}
