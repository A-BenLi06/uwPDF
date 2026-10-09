#include "PdfDocumentCore.h"
#include <windows.data.json.h>
#include <wrl/client.h>
#include <wrl/wrappers/corewrappers.h>
#include <roapi.h>
#include <cmath>
using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;
using namespace ABI::Windows::Data::Json;
namespace
{
    void Check(HRESULT code) { if (FAILED(code)) throw PdfNativeCore::PdfFailure(code, L"Invalid PDF annotations"); }
    unsigned int Count(IJsonArray* array)
    {
        if (!array) return 0;
        ComPtr<ABI::Windows::Foundation::Collections::IVector<IJsonValue*>> values;
        Check(array->QueryInterface(IID_PPV_ARGS(&values)));
        unsigned int count = 0; Check(values->get_Size(&count)); return count;
    }
    float Number(IJsonArray* array, unsigned int index)
    {
        double value = 0; Check(array->GetNumberAt(index, &value)); return static_cast<float>(value);
    }
    void Require(bool value) { if (!value) Check(E_INVALIDARG); }
}
std::vector<PdfNativeCore::ExportAnnotation> PdfNativeCore::ParseAnnotations(const wchar_t* json, unsigned int length)
{
    HString input; Check(input.Set(json, length));
    ComPtr<IJsonArrayStatics> statics;
    Check(RoGetActivationFactory(HStringReference(RuntimeClass_Windows_Data_Json_JsonArray).Get(), IID_PPV_ARGS(&statics)));
    ComPtr<IJsonArray> array; Check(statics->Parse(input.Get(), &array));
    std::vector<ExportAnnotation> result;
    for (unsigned int itemIndex = 0, size = Count(array.Get()); itemIndex < size; ++itemIndex)
    {
        ComPtr<IJsonObject> item; Check(array->GetObjectAt(itemIndex, &item));
        ComPtr<IJsonObjectWithDefaultValues> defaults; Check(item.As(&defaults));
        ExportAnnotation annotation = {};
        double value = 0; Check(item->GetNamedNumber(HStringReference(L"page").Get(), &value)); annotation.page = static_cast<int>(value);
        HString kind; Check(item->GetNamedString(HStringReference(L"kind").Get(), kind.GetAddressOf()));
        unsigned int kindLength = 0; auto kindText = kind.GetRawBuffer(&kindLength);
        annotation.ink = kindLength == 3 && std::wstring(kindText, kindLength) == L"ink";
        ComPtr<IJsonArray> color; Check(item->GetNamedArray(HStringReference(L"color").Get(), &color));
        Require(Count(color.Get()) == 3);
        for (unsigned int i = 0; i < 3; ++i) annotation.color[i] = Number(color.Get(), i);
        Check(item->GetNamedNumber(HStringReference(L"opacity").Get(), &value)); annotation.opacity = static_cast<float>(value);
        Check(defaults->GetNamedNumberOrDefault(HStringReference(L"width").Get(), .002, &value)); annotation.width = static_cast<float>(value);
        boolean flag = false;
        Check(defaults->GetNamedBooleanOrDefault(HStringReference(L"evenOdd").Get(), false, &flag)); annotation.evenOdd = !!flag;
        Check(defaults->GetNamedBooleanOrDefault(HStringReference(L"highlighter").Get(), false, &flag)); annotation.highlighter = !!flag;
        Check(defaults->GetNamedBooleanOrDefault(HStringReference(L"darken").Get(), false, &flag)); annotation.darken = !!flag;
        ComPtr<IJsonArray> outline; Check(defaults->GetNamedArrayOrDefault(HStringReference(L"outline").Get(), nullptr, &outline));
        for (unsigned int i = 0, count = Count(outline.Get()); i < count; ++i)
        {
            ComPtr<IJsonArray> commandArray; Check(outline->GetArrayAt(i, &commandArray));
            Require(Count(commandArray.Get()) > 0);
            HString name; Check(commandArray->GetStringAt(0, name.GetAddressOf()));
            unsigned int nameLength = 0; auto nameText = name.GetRawBuffer(&nameLength); Require(nameLength == 1);
            OutlineCommand command = {}; command.op = static_cast<char>(nameText[0]);
            auto coordinates = command.op == 'h' ? 0u : command.op == 'c' ? 6u : command.op == 'm' || command.op == 'l' ? 2u : 99u;
            Require(Count(commandArray.Get()) == coordinates + 1);
            for (unsigned int n = 0; n < coordinates; ++n) { command.coordinates[n] = Number(commandArray.Get(), n + 1); Require(std::isfinite(command.coordinates[n])); }
            annotation.outline.push_back(command);
        }
        ComPtr<IJsonArray> points; Check(item->GetNamedArray(HStringReference(annotation.ink ? L"points" : L"quads").Get(), &points));
        for (unsigned int i = 0, count = Count(points.Get()); i < count; ++i)
        {
            ComPtr<IJsonArray> p; Check(points->GetArrayAt(i, &p));
            if (annotation.ink)
            {
                Require(Count(p.Get()) == 2); annotation.points.push_back(fz_make_point(Number(p.Get(), 0), Number(p.Get(), 1)));
            }
            else
            {
                Require(Count(p.Get()) == 8);
                fz_quad q;
                q.ul = fz_make_point(Number(p.Get(), 0), Number(p.Get(), 1)); q.ur = fz_make_point(Number(p.Get(), 2), Number(p.Get(), 3));
                q.ll = fz_make_point(Number(p.Get(), 4), Number(p.Get(), 5)); q.lr = fz_make_point(Number(p.Get(), 6), Number(p.Get(), 7));
                annotation.quads.push_back(q);
            }
        }
        if (!annotation.points.empty() || !annotation.quads.empty()) result.push_back(std::move(annotation));
    }
    return result;
}
