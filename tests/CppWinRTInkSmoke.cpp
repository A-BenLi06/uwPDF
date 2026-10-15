#include <Unknwn.h>
#include <activation.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Input.Inking.h>
#include <winrt/Windows.UI.ViewManagement.h>
#include <winrt/PdfNative.Rendering.h>
#include <fstream>
#include <iostream>
#include <memory>
#include <vector>
#include <limits>

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Foundation::Numerics;
using namespace Windows::UI::Input::Inking;
namespace
{
    void Require(bool value, char const* message) { if (!value) throw std::runtime_error(message); }
    template<typename F> void ExpectError(F action, HRESULT expected)
    {
        try { action(); }
        catch (hresult_error const& error) { Require(error.code() == expected, "Unexpected HRESULT."); return; }
        throw std::runtime_error("Expected an HRESULT failure.");
    }
    InkStroke Stroke(int kind)
    {
        InkStrokeBuilder builder;
        InkDrawingAttributes attributes;
        attributes.Size({ kind == 2 ? 20.f : 12.f, kind == 2 ? 8.f : 12.f });
        attributes.FitToCurve(true); attributes.IgnorePressure(kind != 0);
        attributes.PenTip(kind == 2 ? PenTipShape::Rectangle : PenTipShape::Circle);
        attributes.DrawAsHighlighter(kind == 2);
        if (kind == 2) attributes.PenTipTransform({ .8660254f, .5f, -.5f, .8660254f, 0, 0 });
        attributes.Color({ static_cast<uint8_t>(kind == 2 ? 128 : 255), static_cast<uint8_t>(kind == 2 ? 255 : 0),
            static_cast<uint8_t>(kind == 2 ? 255 : 80), static_cast<uint8_t>(kind == 2 ? 0 : 255) });
        builder.SetDefaultDrawingAttributes(attributes);
        std::vector<InkPoint> points = {
            InkPoint({30.f, 80.f + kind * 100}, .1f), InkPoint({80.f, 20.f + kind * 100}, .4f),
            InkPoint({140.f, 90.f + kind * 100}, 1.f), InkPoint({200.f, 40.f + kind * 100}, .7f),
            InkPoint({260.f, 80.f + kind * 100}, .2f)
        };
        return builder.CreateStrokeFromInkPoints(points, kind == 1 ? float3x2{ .9f, .1f, -.1f, .9f, 20, 10 } : float3x2::identity());
    }
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) return 2;
    init_apartment(apartment_type::multi_threaded);
    try
    {
        auto module = LoadLibraryExW(argv[1], nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        check_bool(module != nullptr);
        struct FreeModule { void operator()(HINSTANCE__* handle) const { FreeLibrary(handle); } };
        std::unique_ptr<HINSTANCE__, FreeModule> moduleLifetime(module);
        using GetFactory = HRESULT(WINAPI*)(HSTRING, ::IActivationFactory**);
        auto getFactory = reinterpret_cast<GetFactory>(GetProcAddress(module, "DllGetActivationFactory"));
        Require(getFactory != nullptr, "Missing activation export.");
        {
            Windows::Foundation::IActivationFactory factory{ nullptr };
            hstring name(L"PdfNative.Rendering.InkOutlineExporter");
            check_hresult(getFactory(static_cast<HSTRING>(get_abi(name)), reinterpret_cast<::IActivationFactory**>(put_abi(factory))));
            auto statics = factory.as<PdfNative::Rendering::IInkOutlineExporterStatics>();
            auto creation = statics.CreateAsync(); auto exporter = creation.get(); creation = nullptr;
            std::ofstream output(argv[2]); output << '[';
            for (int kind = 0; kind < 3; ++kind)
            {
                auto stroke = Stroke(kind);
                auto json = exporter.DescribeAsync(stroke, 300, 400).get();
                if (kind) output << ',';
                output << to_string(json);
                auto pending = exporter.DescribeAsync(stroke, 300, 400);
                stroke.PointTransform({1, 0, 0, 1, 100, 50});
                auto modified = stroke.DrawingAttributes(); modified.Size({ 50, 50 }); modified.Color({ 255, 255, 0, 0 }); stroke.DrawingAttributes(modified);
                Require(pending.get() == json, "A suspended export observed edits after its stroke snapshot.");
                std::cout << "PASS: C++/WinRT Windows Ink kind " << kind << ", " << json.size() << " JSON units and snapshot\n";
            }
            output << ']'; output.close(); Require(!output.fail(), "Cannot write ink outlines.");
            auto stroke = Stroke(0);
            ExpectError([&] { exporter.DescribeAsync(nullptr, 300, 400).get(); }, E_INVALIDARG);
            ExpectError([&] { exporter.DescribeAsync(stroke, 0, 400).get(); }, E_INVALIDARG);
            ExpectError([&] { exporter.DescribeAsync(stroke, 300, -1).get(); }, E_INVALIDARG);
            ExpectError([&] { exporter.DescribeAsync(stroke, std::numeric_limits<double>::quiet_NaN(), 400).get(); }, E_INVALIDARG);
            ExpectError([&] { exporter.DescribeAsync(stroke, 300, std::numeric_limits<double>::infinity()).get(); }, E_INVALIDARG);
            std::vector<IAsyncOperation<hstring>> pending;
            for (int i = 0; i < 16; ++i) pending.push_back(exporter.DescribeAsync(stroke, 300, 400));
            exporter.Close(); exporter.Close();
            for (auto const& operation : pending)
            {
                try { Require(!operation.get().empty(), "A completed outline was empty."); }
                catch (hresult_error const& error) { Require(error.code() == RO_E_CLOSED, "Close race returned an unexpected failure."); }
            }
            ExpectError([&] { exporter.DescribeAsync(stroke, 300, 400).get(); }, RO_E_CLOSED);
            pending.clear(); exporter = nullptr;

            creation = statics.CreateAsync(); exporter = creation.get(); creation = nullptr;
            auto weak = make_weak(exporter);
            auto alive = exporter.DescribeAsync(stroke, 300, 400); exporter = nullptr;
            Require(!alive.get().empty(), "Dropping the caller released a pending exporter."); alive = nullptr;
            Require(!weak.get(), "Completed export retained the native exporter.");
            creation = statics.CreateAsync(); exporter = creation.get(); creation = nullptr;
            weak = make_weak(exporter);
            auto failed = exporter.DescribeAsync(nullptr, 300, 400);
            ExpectError([&] { failed.get(); }, E_INVALIDARG); exporter = nullptr;
            Require(!weak.get(), "Failed export coroutine retained the native exporter.");
        }
        {
            Windows::Foundation::IActivationFactory factory{ nullptr };
            hstring name(L"PdfNative.Rendering.PreviewDisplay");
            check_hresult(getFactory(static_cast<HSTRING>(get_abi(name)), reinterpret_cast<::IActivationFactory**>(put_abi(factory))));
            auto area = factory.as<PdfNative::Rendering::IPreviewDisplayStatics>().TryGetWorkArea(nullptr);
            Require(area.Width == 0 && area.Height == 0, "Null display view must request the compatibility fallback.");
            std::cout << "PASS: actual display bridge activation and null-view fallback\n";
        }
        using CanUnload = HRESULT(WINAPI*)();
        auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(module, "DllCanUnloadNow"));
        Require(canUnload && canUnload() == S_OK, "Released ink component cannot unload.");
        std::cout << "PASS: actual ink DLL activation, snapshots, argument errors, concurrent close, coroutine lifetime and unload\n";
        return 0;
    }
    catch (hresult_error const& error) { std::wcerr << error.message().c_str() << L" (HRESULT " << std::hex << error.code().value << L")\n"; return 1; }
    catch (std::exception const& error) { std::cerr << error.what() << '\n'; return 1; }
}
