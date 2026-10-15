#pragma once
#include "InkOutlineCore.h"
#include <memory>

namespace PdfNative
{
    [Windows::Foundation::Metadata::WebHostHidden]
    public ref class InkOutlineExporter sealed
    {
    public:
        static Windows::Foundation::IAsyncOperation<InkOutlineExporter^>^ CreateAsync();
        Windows::Foundation::IAsyncOperation<Platform::String^>^ DescribeAsync(
            Windows::UI::Input::Inking::InkStroke^ stroke, double width, double height);
        virtual ~InkOutlineExporter();
    private:
        InkOutlineExporter();
        std::shared_ptr<PdfNativeCore::InkOutlineCore> core;
    };
}
