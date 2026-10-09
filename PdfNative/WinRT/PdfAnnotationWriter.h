#pragma once
#include "PdfAnnotationWriter.g.h"
#include "../PdfDocumentCore.h"
#include <winrt/Windows.Storage.Streams.h>
namespace winrt::PdfNative::Rendering::implementation
{
    struct PdfAnnotationWriter : PdfAnnotationWriterT<PdfAnnotationWriter>
    {
        explicit PdfAnnotationWriter(std::shared_ptr<PdfNativeCore::TextDocumentState> value) : state(std::move(value)) { }
        static Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::PdfAnnotationWriter> OpenAsync(Windows::Storage::Streams::IRandomAccessStream stream);
        Windows::Foundation::IAsyncAction AppendAsync(hstring annotations);
        Windows::Foundation::IAsyncOperation<Windows::Foundation::Size> ReadPageSizeAsync(uint32_t pageIndex);
        Windows::Foundation::IAsyncAction WriteAsync(Windows::Storage::Streams::IRandomAccessStream stream);
        void Close() { std::atomic_store(&state, std::shared_ptr<PdfNativeCore::TextDocumentState>()); }
    private:
        std::shared_ptr<PdfNativeCore::TextDocumentState> state;
    };
}
namespace winrt::PdfNative::Rendering::factory_implementation
{
    struct PdfAnnotationWriter : PdfAnnotationWriterT<PdfAnnotationWriter, implementation::PdfAnnotationWriter> { };
}
