#include "InkOutlineCore.h"
#include <wrl/implements.h>
#include <sstream>
#include <iomanip>
#include <locale>
#include <vector>
#include <array>
#include <cmath>
#include <new>
using namespace Microsoft::WRL;

namespace
{
    void Check(HRESULT value) { if (FAILED(value)) throw value; }
    struct PathCommand { char op; std::array<float, 6> points; unsigned int count; };
    class OutlineSink : public RuntimeClass<RuntimeClassFlags<ClassicCom>, ID2D1SimplifiedGeometrySink>
    {
        HRESULT failure = S_OK;
        void Append(char op, const float* points, unsigned int count) noexcept
        {
            if (FAILED(failure)) return;
            PathCommand command = {};
            command.op = op; command.count = count;
            for (unsigned int i = 0; i < count; ++i) command.points[i] = points[i];
            // COM geometry callbacks cannot propagate a C++ allocation exception.
            try { commands.push_back(command); }
            catch (...) { failure = E_OUTOFMEMORY; }
        }
    public:
        std::vector<PathCommand> commands;
        bool evenOdd = false;
        void STDMETHODCALLTYPE SetFillMode(D2D1_FILL_MODE mode) override { evenOdd = mode == D2D1_FILL_MODE_ALTERNATE; }
        void STDMETHODCALLTYPE SetSegmentFlags(D2D1_PATH_SEGMENT) override { }
        void STDMETHODCALLTYPE BeginFigure(D2D1_POINT_2F p, D2D1_FIGURE_BEGIN) override { const float points[] = { p.x, p.y }; Append('m', points, 2); }
        void STDMETHODCALLTYPE AddLines(const D2D1_POINT_2F* p, UINT32 count) override
        {
            for (UINT32 i = 0; i < count; ++i) { const float points[] = { p[i].x, p[i].y }; Append('l', points, 2); }
        }
        void STDMETHODCALLTYPE AddBeziers(const D2D1_BEZIER_SEGMENT* p, UINT32 count) override
        {
            for (UINT32 i = 0; i < count; ++i) { const float points[] = { p[i].point1.x, p[i].point1.y, p[i].point2.x, p[i].point2.y, p[i].point3.x, p[i].point3.y }; Append('c', points, 6); }
        }
        void STDMETHODCALLTYPE EndFigure(D2D1_FIGURE_END end) override
        {
            if (end == D2D1_FIGURE_END_CLOSED) Append('h', nullptr, 0);
        }
        HRESULT STDMETHODCALLTYPE Close() override { return failure; }
    };

    class InkCommands : public RuntimeClass<RuntimeClassFlags<ClassicCom>,
        ChainInterfaces<ID2D1CommandSink2, ID2D1CommandSink1, ID2D1CommandSink>>
    {
        ComPtr<ID2D1Factory1> factory;
        ComPtr<OutlineSink> outline;
        D2D1_MATRIX_3X2_F transform = D2D1::Matrix3x2F::Identity();
        D2D1_PRIMITIVE_BLEND primitiveBlend = D2D1_PRIMITIVE_BLEND_SOURCE_OVER;
        HRESULT ReadBrush(ID2D1Brush* brush)
        {
            ComPtr<ID2D1SolidColorBrush> solid;
            if (!brush || FAILED(brush->QueryInterface(IID_PPV_ARGS(&solid)))) return E_NOTIMPL;
            color = solid->GetColor();
            color.a *= solid->GetOpacity();
            darken = primitiveBlend == D2D1_PRIMITIVE_BLEND_MIN;
            return S_OK;
        }
    public:
        D2D1_COLOR_F color = D2D1::ColorF(0, 0, 0, 1);
        bool darken = false;
        InkCommands(ID2D1Factory1* f, OutlineSink* s) : factory(f), outline(s) { }
        HRESULT STDMETHODCALLTYPE BeginDraw() override { return S_OK; }
        HRESULT STDMETHODCALLTYPE EndDraw() override { return S_OK; }
        HRESULT STDMETHODCALLTYPE SetAntialiasMode(D2D1_ANTIALIAS_MODE) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE SetTags(D2D1_TAG, D2D1_TAG) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE SetTextRenderingParams(IDWriteRenderingParams*) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE SetTransform(const D2D1_MATRIX_3X2_F* matrix) override { transform = *matrix; return S_OK; }
        HRESULT STDMETHODCALLTYPE SetPrimitiveBlend(D2D1_PRIMITIVE_BLEND mode) override { primitiveBlend = mode; return S_OK; }
        HRESULT STDMETHODCALLTYPE SetPrimitiveBlend1(D2D1_PRIMITIVE_BLEND mode) override { primitiveBlend = mode; return S_OK; }
        HRESULT STDMETHODCALLTYPE SetUnitMode(D2D1_UNIT_MODE mode) override { return mode == D2D1_UNIT_MODE_DIPS ? S_OK : E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE Clear(const D2D1_COLOR_F*) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE DrawGlyphRun(D2D1_POINT_2F, const DWRITE_GLYPH_RUN*, const DWRITE_GLYPH_RUN_DESCRIPTION*, ID2D1Brush*, DWRITE_MEASURING_MODE) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE DrawLine(D2D1_POINT_2F a, D2D1_POINT_2F b, ID2D1Brush* brush, FLOAT width, ID2D1StrokeStyle* style) override
        {
            ComPtr<ID2D1PathGeometry> geometry;
            auto hr = factory->CreatePathGeometry(&geometry);
            if (FAILED(hr)) return hr;
            ComPtr<ID2D1GeometrySink> sink;
            if (FAILED(hr = geometry->Open(&sink))) return hr;
            sink->BeginFigure(a, D2D1_FIGURE_BEGIN_HOLLOW); sink->AddLine(b); sink->EndFigure(D2D1_FIGURE_END_OPEN);
            if (FAILED(hr = sink->Close())) return hr;
            return DrawGeometry(geometry.Get(), brush, width, style);
        }
        HRESULT STDMETHODCALLTYPE DrawGeometry(ID2D1Geometry* geometry, ID2D1Brush* brush, FLOAT width, ID2D1StrokeStyle* style) override
        {
            auto hr = ReadBrush(brush);
            return FAILED(hr) ? hr : geometry->Widen(width, style, &transform, 0.1f, outline.Get());
        }
        HRESULT STDMETHODCALLTYPE DrawRectangle(const D2D1_RECT_F* rect, ID2D1Brush* brush, FLOAT width, ID2D1StrokeStyle* style) override
        {
            ComPtr<ID2D1RectangleGeometry> geometry;
            auto hr = factory->CreateRectangleGeometry(rect, &geometry);
            return FAILED(hr) ? hr : DrawGeometry(geometry.Get(), brush, width, style);
        }
        HRESULT STDMETHODCALLTYPE DrawBitmap(ID2D1Bitmap*, const D2D1_RECT_F*, FLOAT, D2D1_INTERPOLATION_MODE, const D2D1_RECT_F*, const D2D1_MATRIX_4X4_F*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE DrawImage(ID2D1Image*, const D2D1_POINT_2F*, const D2D1_RECT_F*, D2D1_INTERPOLATION_MODE, D2D1_COMPOSITE_MODE) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE DrawGdiMetafile(ID2D1GdiMetafile*, const D2D1_POINT_2F*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE DrawGdiMetafile(ID2D1GdiMetafile*, const D2D1_RECT_F*, const D2D1_RECT_F*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE FillMesh(ID2D1Mesh*, ID2D1Brush*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE FillOpacityMask(ID2D1Bitmap*, ID2D1Brush*, const D2D1_RECT_F*, const D2D1_RECT_F*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE FillGeometry(ID2D1Geometry* geometry, ID2D1Brush* brush, ID2D1Brush* opacity) override
        {
            if (opacity) return E_NOTIMPL;
            auto hr = ReadBrush(brush);
            return FAILED(hr) ? hr : geometry->Simplify(D2D1_GEOMETRY_SIMPLIFICATION_OPTION_CUBICS_AND_LINES, &transform, 0.1f, outline.Get());
        }
        HRESULT STDMETHODCALLTYPE FillRectangle(const D2D1_RECT_F* rect, ID2D1Brush* brush) override
        {
            ComPtr<ID2D1RectangleGeometry> geometry;
            auto hr = factory->CreateRectangleGeometry(rect, &geometry);
            return FAILED(hr) ? hr : FillGeometry(geometry.Get(), brush, nullptr);
        }
        HRESULT STDMETHODCALLTYPE PushAxisAlignedClip(const D2D1_RECT_F*, D2D1_ANTIALIAS_MODE) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE PopAxisAlignedClip() override { return S_OK; }
        HRESULT STDMETHODCALLTYPE PushLayer(const D2D1_LAYER_PARAMETERS1*, ID2D1Layer*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE PopLayer() override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE DrawInk(ID2D1Ink* ink, ID2D1Brush* brush, ID2D1InkStyle* style) override
        {
            auto hr = ReadBrush(brush);
            return FAILED(hr) ? hr : ink->StreamAsGeometry(style, &transform, 0.1f, outline.Get());
        }
        HRESULT STDMETHODCALLTYPE DrawGradientMesh(ID2D1GradientMesh*) override { return E_NOTIMPL; }
    };
}


HRESULT PdfNativeCore::InkOutlineCore::Initialize() noexcept
{
    try
    {
        std::lock_guard<std::mutex> lock(gate);
        if (closed) return RO_E_CLOSED;
        if (initialized) return S_OK;
        context.Reset(); device.Reset(); factory.Reset();
        Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED, factory.GetAddressOf()));
        Check(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, nullptr));
        ComPtr<IDXGIDevice> dxgi;
        Check(device.As(&dxgi));
        ComPtr<ID2D1Device> d2dDevice;
        Check(factory->CreateDevice(dxgi.Get(), &d2dDevice));
        ComPtr<ID2D1DeviceContext> baseContext;
        Check(d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &baseContext));
        Check(baseContext.As(&context));
        initialized = true;
        return S_OK;
    }
    catch (HRESULT error) { return error; }
    catch (std::bad_alloc const&) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

HRESULT PdfNativeCore::InkOutlineCore::Describe(IUnknown* strokes, double width, double height, std::wstring* result) noexcept
{
    if (!strokes || !result || !std::isfinite(width) || !std::isfinite(height) || width <= 0 || height <= 0) return E_INVALIDARG;
    try
    {
        std::lock_guard<std::mutex> lock(gate);
        if (closed || !initialized) return RO_E_CLOSED;
        // Draw caches native stroke state. Each export owns a short-lived
        // snapshot, so retire its renderer here, on the drawing thread while
        // that snapshot is still alive. Reusing it across worker requests and
        // releasing it during Close can race the cached strokes' destruction.
        // The expensive D3D/D2D device and context remain shared and serialized.
        ComPtr<IInkD2DRenderer> inkRenderer;
        Check(CoCreateInstance(__uuidof(InkD2DRenderer), nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&inkRenderer)));
        ComPtr<ID2D1CommandList> list;
        Check(context->CreateCommandList(&list));
        context->SetTarget(list.Get());
        context->SetTransform(D2D1::Matrix3x2F::Identity());
        context->BeginDraw();
        auto drawn = inkRenderer->Draw(context.Get(), strokes, FALSE);
        auto ended = context->EndDraw();
        context->SetTarget(nullptr);
        Check(drawn); Check(ended); Check(list->Close());
        auto outline = Make<OutlineSink>();
        if (!outline) return E_OUTOFMEMORY;
        auto commands = Make<InkCommands>(factory.Get(), outline.Get());
        if (!commands) return E_OUTOFMEMORY;
        Check(list->Stream(commands.Get()));
        Check(outline->Close());
        if (outline->commands.empty()) return E_FAIL;
        std::wostringstream json;
        json.imbue(std::locale::classic());
        json << std::setprecision(9) << L"{\"outline\":[";
        bool first = true;
        for (auto& command : outline->commands)
        {
            if (!first) json << L',';
            first = false;
            json << L"[\"" << static_cast<wchar_t>(command.op) << L"\"";
            for (size_t i = 0; i < command.count; ++i) json << L',' << command.points[i] / (i % 2 ? height : width);
            json << L']';
        }
        auto color = commands->color;
        json << L"],\"evenOdd\":" << (outline->evenOdd ? L"true" : L"false")
            << L",\"darken\":" << (commands->darken ? L"true" : L"false")
            << L",\"color\":[" << color.r << L',' << color.g << L',' << color.b << L"],\"opacity\":" << color.a << L'}';

        *result = json.str();
        return S_OK;
    }
    catch (HRESULT error) { return error; }
    catch (std::bad_alloc const&) { return E_OUTOFMEMORY; }
    catch (...) { return E_FAIL; }
}

void PdfNativeCore::InkOutlineCore::Close() noexcept
{
    std::lock_guard<std::mutex> lock(gate);
    closed = true;
    context.Reset(); device.Reset(); factory.Reset();
}
