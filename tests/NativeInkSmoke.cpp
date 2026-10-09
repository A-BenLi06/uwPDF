#include "../PdfNative/InkOutlineExporter.h"
#include <collection.h>
#include <WindowsNumerics.h>
#include <roapi.h>
#include <ppltasks.h>
#include <fstream>
#include <iostream>
#include <vector>

using namespace concurrency;
using namespace Windows::UI::Input::Inking;
using namespace Windows::Foundation;
using namespace Windows::Foundation::Numerics;

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2 || FAILED(RoInitialize(RO_INIT_MULTITHREADED))) return 2;
    struct ApartmentLifetime { ~ApartmentLifetime() { RoUninitialize(); } } apartment;
    std::cout << std::unitbuf;
    try
    {
        auto exporter = create_task(PdfNative::InkOutlineExporter::CreateAsync()).get();
        InkStroke^ lastStroke = nullptr;
        std::ofstream output(argv[1]);
        output << '[';
        for (int kind = 0; kind < 3; ++kind)
        {
            auto builder = ref new InkStrokeBuilder();
            auto attributes = ref new InkDrawingAttributes();
            attributes->Size = Size(kind == 2 ? 20 : 12, kind == 2 ? 8 : 12);
            attributes->FitToCurve = true;
            attributes->IgnorePressure = kind != 0;
            attributes->PenTip = kind == 2 ? PenTipShape::Rectangle : PenTipShape::Circle;
            attributes->DrawAsHighlighter = kind == 2;
            if (kind == 2) attributes->PenTipTransform = float3x2(0.8660254f, 0.5f, -0.5f, 0.8660254f, 0, 0);
            Windows::UI::Color color;
            color.A = kind == 2 ? 128 : 255; color.R = kind == 2 ? 255 : 0; color.G = kind == 2 ? 255 : 80; color.B = kind == 2 ? 0 : 255;
            attributes->Color = color;
            builder->SetDefaultDrawingAttributes(attributes);
            auto points = ref new Platform::Collections::Vector<InkPoint^>();
            points->Append(ref new InkPoint(Point(30, 80 + kind * 100), 0.1f));
            points->Append(ref new InkPoint(Point(80, 20 + kind * 100), 0.4f));
            points->Append(ref new InkPoint(Point(140, 90 + kind * 100), 1.0f));
            points->Append(ref new InkPoint(Point(200, 40 + kind * 100), 0.7f));
            points->Append(ref new InkPoint(Point(260, 80 + kind * 100), 0.2f));
            auto stroke = builder->CreateStrokeFromInkPoints(points, kind == 1
                ? float3x2(0.9f, 0.1f, -0.1f, 0.9f, 20, 10) : float3x2::identity());
            auto json = create_task(exporter->DescribeAsync(stroke, 300, 400)).get();
            lastStroke = stroke;
            if (kind) output << ',';
            // Outline JSON is deliberately ASCII, so conversion is lossless.
            for (unsigned int i = 0; i < json->Length(); ++i) output << static_cast<char>(json->Data()[i]);
            std::cout << "PASS: Windows Ink outline kind " << kind << ", " << json->Length() << " JSON units\n";
        }
        output << ']';
        if (!output) return 3;
        std::vector<task<Platform::String^>> pending;
        for (int i = 0; i < 16; ++i) pending.push_back(create_task(exporter->DescribeAsync(lastStroke, 300, 400)));
        std::cout << "Closing with 16 pending legacy exports\n";
        delete exporter;
        std::cout << "Legacy exporter closed\n";
        for (auto& operation : pending)
        {
            try
            {
                if (!operation.get()->Length()) throw ref new Platform::FailureException(L"Empty pending legacy outline");
            }
            catch (Platform::Exception^ error)
            {
                if (error->HResult != RO_E_CLOSED) throw;
            }
        }
        std::cout << "PASS: legacy concurrent export/close\n";
        return 0;
    }
    catch (Platform::Exception^ ex)
    {
        std::wcerr << ex->Message->Data() << L" (" << std::hex << ex->HResult << L")\n";
        return 1;
    }
}
