#include "InkOutlineExporter.h"
#include <cmath>

namespace winrt::PdfNative::Rendering::implementation
{
    InkOutlineExporter::InkOutlineExporter() { check_hresult(core.Initialize()); }

    Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::InkOutlineExporter> InkOutlineExporter::CreateAsync()
    {
        co_await resume_background();
        co_return winrt::make<InkOutlineExporter>();
    }

    Windows::Foundation::IAsyncOperation<hstring> InkOutlineExporter::DescribeAsync(
        Windows::UI::Input::Inking::InkStroke stroke, double width, double height)
    {
        // Clone on the caller thread before suspension. Edits to live ink must
        // not change the exported stroke; own this object through completion.
        auto lifetime = get_strong();
        if (!stroke || !std::isfinite(width) || !std::isfinite(height) || width <= 0 || height <= 0)
            throw hresult_invalid_argument();
        auto copy = stroke.Clone();
        co_await resume_background();
        auto strokes = single_threaded_vector<Windows::UI::Input::Inking::InkStroke>();
        strokes.Append(copy);
        std::wstring result;
        check_hresult(lifetime->core.Describe(winrt::get_unknown(strokes), width, height, &result));
        co_return hstring(result);
    }

    void InkOutlineExporter::Close() { core.Close(); }
}
