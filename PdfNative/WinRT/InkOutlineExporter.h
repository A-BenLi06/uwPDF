#pragma once
#include "InkOutlineExporter.g.h"
#include "../InkOutlineCore.h"
#include <winrt/Windows.UI.Input.Inking.h>
#include <winrt/Windows.Foundation.Collections.h>

namespace winrt::PdfNative::Rendering::implementation
{
    struct InkOutlineExporter : InkOutlineExporterT<InkOutlineExporter>
    {
        InkOutlineExporter();
        static Windows::Foundation::IAsyncOperation<winrt::PdfNative::Rendering::InkOutlineExporter> CreateAsync();
        Windows::Foundation::IAsyncOperation<hstring> DescribeAsync(
            Windows::UI::Input::Inking::InkStroke stroke, double width, double height);
        void Close();
    private:
        PdfNativeCore::InkOutlineCore core;
    };
}
namespace winrt::PdfNative::Rendering::factory_implementation
{
    struct InkOutlineExporter : InkOutlineExporterT<InkOutlineExporter, implementation::InkOutlineExporter> {};
}
