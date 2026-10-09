#include "PreviewDisplay.h"
#include "../PreviewDisplayCore.h"
namespace winrt::PdfNative::Rendering::implementation
{
    Windows::Foundation::Size PreviewDisplay::TryGetWorkArea(Windows::UI::ViewManagement::ApplicationView const& view)
    {
        auto size = PdfNativeCore::TryGetWorkArea(reinterpret_cast<::IInspectable*>(winrt::get_abi(view)));
        return { size.Width, size.Height };
    }
}
