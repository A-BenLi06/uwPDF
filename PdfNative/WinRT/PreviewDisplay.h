#pragma once
#include "PreviewDisplay.g.h"
#include <winrt/Windows.UI.ViewManagement.h>

namespace winrt::PdfNative::Rendering::implementation
{
    struct PreviewDisplay
    {
        static Windows::Foundation::Size TryGetWorkArea(Windows::UI::ViewManagement::ApplicationView const& view);
    };
}
namespace winrt::PdfNative::Rendering::factory_implementation
{
    struct PreviewDisplay : PreviewDisplayT<PreviewDisplay, implementation::PreviewDisplay> {};
}
