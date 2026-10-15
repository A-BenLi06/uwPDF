#pragma once
#include "PdfTextDocument.g.h"
#include "../PdfDocumentCore.h"
#include <winrt/Windows.Storage.Streams.h>
namespace winrt::PdfNative::Rendering::implementation
{
    struct PdfTextDocument : PdfTextDocumentT<PdfTextDocument>
    {
        explicit PdfTextDocument(std::shared_ptr<PdfNativeCore::TextDocumentState> value) : state(std::move(value)) { }
        static Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfTextDocument> OpenAsync(Windows::Storage::Streams::IRandomAccessStream stream);
        Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfTextPage> ReadPageAsync(uint32_t pageIndex);
        static Windows::Foundation::IAsyncAction ExportAsync(Windows::Storage::Streams::IRandomAccessStream input,
            Windows::Storage::Streams::IRandomAccessStream output, hstring annotations);
        void Close() { std::atomic_store(&state, std::shared_ptr<PdfNativeCore::TextDocumentState>()); }
    private:
        std::shared_ptr<PdfNativeCore::TextDocumentState> state;
    };
}
namespace winrt::PdfNative::Rendering::factory_implementation
{
    struct PdfTextDocument : PdfTextDocumentT<PdfTextDocument, implementation::PdfTextDocument> { };
}
