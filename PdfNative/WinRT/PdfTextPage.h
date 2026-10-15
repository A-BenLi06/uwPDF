#pragma once
#include "PdfTextPage.g.h"
#include "../PdfDocumentCore.h"
namespace winrt::PdfNative::Rendering::implementation
{
    struct PdfTextPage : PdfTextPageT<PdfTextPage>
    {
        PdfTextPage() = default;
        explicit PdfTextPage(PdfNativeCore::TextPageData data) : text(data.text), coordinates(std::move(data.coordinates)) { }
        hstring Text() const { return text; }
        void Text(hstring const& value) { text = value; }
        com_array<float> Coordinates() const { return {coordinates.begin(), coordinates.end()}; }
        void Coordinates(array_view<float const> value) { coordinates.assign(value.begin(), value.end()); }
    private:
        hstring text;
        std::vector<float> coordinates;
    };
}
namespace winrt::PdfNative::Rendering::factory_implementation
{
    struct PdfTextPage : PdfTextPageT<PdfTextPage, implementation::PdfTextPage> { };
}
