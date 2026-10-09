#include "InkOutlineExporter.h"
#include <collection.h>
#include <ppltasks.h>
#include <cmath>
using namespace Windows::Foundation;
using namespace Windows::UI::Input::Inking;
using namespace concurrency;
namespace { void Check(HRESULT value) { if (FAILED(value)) throw Platform::Exception::CreateException(value); } }

PdfNative::InkOutlineExporter::InkOutlineExporter() : core(std::make_shared<PdfNativeCore::InkOutlineCore>()) { Check(core->Initialize()); }
PdfNative::InkOutlineExporter::~InkOutlineExporter()
{
    auto lifetime = std::atomic_load(&core);
    if (lifetime) lifetime->Close();
    std::atomic_store(&core, std::shared_ptr<PdfNativeCore::InkOutlineCore>());
}
IAsyncOperation<PdfNative::InkOutlineExporter^>^ PdfNative::InkOutlineExporter::CreateAsync()
{
    return create_async([]() { return ref new InkOutlineExporter(); });
}
IAsyncOperation<Platform::String^>^ PdfNative::InkOutlineExporter::DescribeAsync(InkStroke^ stroke, double width, double height)
{
    if (!stroke || !std::isfinite(width) || !std::isfinite(height) || width <= 0 || height <= 0)
        throw ref new Platform::InvalidArgumentException();
    // C++/CX Close destroys native members even when a ref-counted caller is
    // still alive. Queue a lease on the core, never a later member access.
    auto lifetime = std::atomic_load(&core);
    if (!lifetime) throw ref new Platform::ObjectDisposedException(L"InkOutlineExporter");
    InkOutlineExporter^ owner = this;
    auto copy = stroke->Clone();
    return create_async([owner, lifetime, copy, width, height]()
    {
        auto strokes = ref new Platform::Collections::Vector<InkStroke^>();
        strokes->Append(copy);
        std::wstring result;
        Check(lifetime->Describe(reinterpret_cast<IUnknown*>(strokes), width, height, &result));
        return ref new Platform::String(result.c_str());
    });
}
