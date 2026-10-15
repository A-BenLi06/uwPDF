#include "PreviewDisplay.h"
#include "PreviewDisplayCore.h"
Windows::Foundation::Size PdfNative::PreviewDisplay::TryGetWorkArea(Windows::UI::ViewManagement::ApplicationView^ view)
{
    auto size = PdfNativeCore::TryGetWorkArea(reinterpret_cast<IInspectable*>(view));
    return Windows::Foundation::Size(size.Width, size.Height);
}
