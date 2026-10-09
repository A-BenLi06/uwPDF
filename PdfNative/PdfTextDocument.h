#pragma once
#include <memory>

namespace PdfNativeCore { struct TextDocumentState; }

namespace PdfNative
{

    [Windows::Foundation::Metadata::WebHostHidden]
    public ref class PdfTextPage sealed
    {
    public:
        property Platform::String^ Text;
        // Nine floats per UTF-16 code unit: four normalized quad points, angle.
        property Platform::Array<float>^ Coordinates;
    };

    [Windows::Foundation::Metadata::WebHostHidden]
    public ref class PdfTextDocument sealed
    {
    public:
        static Windows::Foundation::IAsyncOperation<PdfTextDocument^>^ OpenAsync(
            Windows::Storage::Streams::IRandomAccessStream^ stream);
        Windows::Foundation::IAsyncOperation<PdfTextPage^>^ ReadPageAsync(unsigned int pageIndex);
        static Windows::Foundation::IAsyncAction^ ExportAsync(
            Windows::Storage::Streams::IRandomAccessStream^ input,
            Windows::Storage::Streams::IRandomAccessStream^ output, Platform::String^ annotations);
        virtual ~PdfTextDocument();
    private:
        PdfTextDocument();
        std::shared_ptr<PdfNativeCore::TextDocumentState> state;
    };

    [Windows::Foundation::Metadata::WebHostHidden]
    public ref class PdfAnnotationWriter sealed
    {
    public:
        static Windows::Foundation::IAsyncOperation<PdfAnnotationWriter^>^ OpenAsync(
            Windows::Storage::Streams::IRandomAccessStream^ input);
        Windows::Foundation::IAsyncAction^ AppendAsync(Platform::String^ annotations);
        Windows::Foundation::IAsyncOperation<Windows::Foundation::Size>^ ReadPageSizeAsync(unsigned int pageIndex);
        Windows::Foundation::IAsyncAction^ WriteAsync(Windows::Storage::Streams::IRandomAccessStream^ output);
        virtual ~PdfAnnotationWriter();
    private:
        PdfAnnotationWriter();
        std::shared_ptr<PdfNativeCore::TextDocumentState> state;
    };
}
