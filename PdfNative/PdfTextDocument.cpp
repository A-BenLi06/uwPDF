#include "PdfTextDocument.h"
#include "PdfStreamCx.h"
using namespace concurrency;
using namespace Windows::Foundation;
using namespace Windows::Storage::Streams;
using namespace PdfNativeCore;

PdfNative::PdfTextDocument::PdfTextDocument() { }
PdfNative::PdfTextDocument::~PdfTextDocument() { std::atomic_store(&state, std::shared_ptr<TextDocumentState>()); }
IAsyncOperation<PdfNative::PdfTextDocument^>^ PdfNative::PdfTextDocument::OpenAsync(IRandomAccessStream^ stream)
{
    if (!stream) throw ref new Platform::InvalidArgumentException();
    auto input = std::make_shared<CxPdfStream>(stream->CloneStream());
    return create_async([input]() { return TranslateCx([&]() {
        auto result = ref new PdfTextDocument(); result->state = OpenDocument(input); return result;
    }); });
}
IAsyncOperation<PdfNative::PdfTextPage^>^ PdfNative::PdfTextDocument::ReadPageAsync(unsigned int pageIndex)
{
    auto lifetime = std::atomic_load(&state);
    if (!lifetime) throw ref new Platform::ObjectDisposedException(L"PdfTextDocument");
    PdfTextDocument^ owner = this;
    return create_async([owner, lifetime, pageIndex]() { return TranslateCx([&]() {
        auto data = ReadPage(lifetime, pageIndex);
        auto result = ref new PdfTextPage();
        result->Text = ref new Platform::String(data.text.data(), static_cast<unsigned int>(data.text.size()));
        result->Coordinates = ref new Platform::Array<float>(data.coordinates.data(), static_cast<unsigned int>(data.coordinates.size()));
        return result;
    }); });
}
PdfNative::PdfAnnotationWriter::PdfAnnotationWriter() { }
PdfNative::PdfAnnotationWriter::~PdfAnnotationWriter() { std::atomic_store(&state, std::shared_ptr<TextDocumentState>()); }
IAsyncOperation<PdfNative::PdfAnnotationWriter^>^ PdfNative::PdfAnnotationWriter::OpenAsync(IRandomAccessStream^ stream)
{
    if (!stream) throw ref new Platform::InvalidArgumentException();
    auto input = std::make_shared<CxPdfStream>(stream->CloneStream());
    return create_async([input]() { return TranslateCx([&]() {
        auto result = ref new PdfAnnotationWriter(); result->state = OpenDocument(input); return result;
    }); });
}
IAsyncAction^ PdfNative::PdfAnnotationWriter::AppendAsync(Platform::String^ annotations)
{
    if (!annotations) throw ref new Platform::InvalidArgumentException();
    auto lifetime = std::atomic_load(&state);
    if (!lifetime) throw ref new Platform::ObjectDisposedException(L"PdfAnnotationWriter");
    PdfAnnotationWriter^ owner = this;
    return create_async([owner, lifetime, annotations]() { TranslateCx([&]() {
        auto items = ParseAnnotations(annotations->Data(), annotations->Length()); AppendAnnotations(lifetime, items);
    }); });
}
IAsyncOperation<Size>^ PdfNative::PdfAnnotationWriter::ReadPageSizeAsync(unsigned int pageIndex)
{
    auto lifetime = std::atomic_load(&state);
    if (!lifetime) throw ref new Platform::ObjectDisposedException(L"PdfAnnotationWriter");
    PdfAnnotationWriter^ owner = this;
    return create_async([owner, lifetime, pageIndex]() { return TranslateCx([&]() {
        auto size = ReadPageSize(lifetime, pageIndex); return Size(size.first, size.second);
    }); });
}
IAsyncAction^ PdfNative::PdfAnnotationWriter::WriteAsync(IRandomAccessStream^ stream)
{
    if (!stream) throw ref new Platform::InvalidArgumentException();
    auto lifetime = std::atomic_load(&state);
    if (!lifetime) throw ref new Platform::ObjectDisposedException(L"PdfAnnotationWriter");
    PdfAnnotationWriter^ owner = this;
    auto destination = std::make_shared<CxPdfStream>(stream->CloneStream());
    return create_async([owner, lifetime, destination]() { TranslateCx([&]() { WriteDocument(lifetime, destination); }); });
}
IAsyncAction^ PdfNative::PdfTextDocument::ExportAsync(IRandomAccessStream^ input, IRandomAccessStream^ output, Platform::String^ annotations)
{
    if (!input || !output || !annotations) throw ref new Platform::InvalidArgumentException();
    auto source = std::make_shared<CxPdfStream>(input->CloneStream());
    auto destination = std::make_shared<CxPdfStream>(output->CloneStream());
    return create_async([source, destination, annotations]() { TranslateCx([&]() {
        auto items = ParseAnnotations(annotations->Data(), annotations->Length()); auto state = OpenDocument(source);
        AppendAnnotations(state, items); WriteDocument(state, destination);
    }); });
}
