#pragma once

namespace PdfNative
{
    [Windows::Foundation::Metadata::WebHostHidden]
    public ref class PreviewDisplay sealed
    {
    public:
        static Windows::Foundation::Size TryGetWorkArea(Windows::UI::ViewManagement::ApplicationView^ view);
    };
}
