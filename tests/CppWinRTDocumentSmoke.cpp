#include <Unknwn.h>
#include <activation.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Data.Pdf.h>
#include <winrt/PdfNative.Rendering.h>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <memory>
#include <cmath>
using namespace winrt;
using namespace Windows::Storage;
namespace
{
    void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
    template<class F> void ExpectError(F action, HRESULT code)
    {
        try { action(); } catch (hresult_error const& error) { Require(error.code() == code, "Unexpected HRESULT"); return; }
        throw std::runtime_error("Missing expected failure");
    }
    template<class F> void ExpectFailure(F action)
    {
        try { action(); } catch (hresult_error const&) { return; }
        throw std::runtime_error("Missing expected failure");
    }
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 5 && argc != 6 && argc != 7 && argc != 8) return 2;
    init_apartment(apartment_type::multi_threaded);
    struct ApartmentLifetime { ~ApartmentLifetime() { uninit_apartment(); } } apartment;
    try
    {
        auto module = LoadLibraryExW(argv[1], nullptr, LOAD_WITH_ALTERED_SEARCH_PATH); check_bool(module != nullptr);
        struct FreeModule { void operator()(HINSTANCE__* handle) const { FreeLibrary(handle); } };
        std::unique_ptr<HINSTANCE__, FreeModule> moduleLifetime(module);
        using GetFactory = HRESULT(WINAPI*)(HSTRING, ::IActivationFactory**);
        auto getFactory = reinterpret_cast<GetFactory>(GetProcAddress(module, "DllGetActivationFactory"));
        Require(getFactory != nullptr, "Missing DLL activation export");
        {
            auto factory = [&](const wchar_t* name) {
                Windows::Foundation::IActivationFactory result{nullptr}; hstring id(name);
                check_hresult(getFactory(static_cast<HSTRING>(get_abi(id)), reinterpret_cast<::IActivationFactory**>(put_abi(result)))); return result;
            };
            auto statics = factory(L"PdfNative.Rendering.PdfTextDocument").as<PdfNative::Rendering::IPdfTextDocumentStatics>();
            auto writerStatics = factory(L"PdfNative.Rendering.PdfAnnotationWriter").as<PdfNative::Rendering::IPdfAnnotationWriterStatics>();
            auto emptyPage = factory(L"PdfNative.Rendering.PdfTextPage").ActivateInstance<PdfNative::Rendering::PdfTextPage>();
            emptyPage.Text(L"DTO"); emptyPage.Coordinates({1.f, 2.f, 3.f});
            Require(emptyPage.Text() == L"DTO" && emptyPage.Coordinates().size() == 3, "Text DTO array/property ABI failure"); emptyPage = nullptr;
            ExpectError([&] { statics.OpenAsync(nullptr).get(); }, E_INVALIDARG);
            ExpectError([&] { writerStatics.OpenAsync(nullptr).get(); }, E_INVALIDARG);
            Windows::Storage::Streams::InMemoryRandomAccessStream invalid;
            ExpectError([&] { statics.OpenAsync(invalid).get(); }, E_FAIL);
            ExpectError([&] { writerStatics.OpenAsync(invalid).get(); }, E_FAIL); invalid.Close();
            ExpectError([&] { statics.ExportAsync(nullptr, nullptr, L"[]").get(); }, E_INVALIDARG);
            auto file = StorageFile::GetFileFromPathAsync(argv[2]).get();
            auto input = file.OpenReadAsync().get(); input.Seek(input.Size() > 16 ? 16 : 0);
            auto creation = statics.OpenAsync(input); input.Close(); input = nullptr;
            auto document = creation.get(); creation = nullptr;
            auto index = static_cast<uint32_t>(_wtoi(argv[3]));
            auto page = document.ReadPageAsync(index).get(); auto text = page.Text(); auto values = page.Coordinates();
            Require(std::wstring(text.c_str(), text.size()).find(argv[4]) != std::wstring::npos, "Expected text missing");
            Require(values.size() == text.size() * 9, "UTF-16 coordinate count differs");
            for (auto value : values) Require(std::isfinite(value), "Invalid text quad");
            ExpectError([&] { document.ReadPageAsync(UINT32_MAX).get(); }, E_FAIL);
            Require(document.ReadPageAsync(index).get().Text() == text, "Failed read poisoned the document");
            std::wcout << L"PASS: actual document DLL, " << text.size() << L" UTF-16 units, " << values.size() << L" coordinates\n";
            if (argc == 6)
            {
                std::ofstream report(argv[5]); report << "{\"text\":\"";
                for (auto unit : text) report << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<unsigned int>(unit);
                report << "\",\"coordinates\":[" << std::dec << std::setprecision(9);
                for (size_t i = 0; i < values.size(); ++i) { if (i) report << ','; report << values[i]; }
                report << "]}"; report.close(); Require(!report.fail(), "Cannot write geometry report");
            }
            auto pending = document.ReadPageAsync(index); auto weakDocument = make_weak(document); document.Close(); document.Close();
            ExpectError([&] { document.ReadPageAsync(index).get(); }, RO_E_CLOSED); document = nullptr; page = nullptr;
            Require(pending.get().Text() == text, "Queued page read did not survive owner release"); pending = nullptr;
            Require(!weakDocument.get(), "Completed operation retains the document");
            if (argc == 7 || argc == 8)
            {
                std::wstring path(argv[5]); auto separator = path.find_last_of(L"/\\"); Require(separator != std::wstring::npos, "Output requires an absolute path");
                auto folder = StorageFolder::GetFolderFromPathAsync(path.substr(0, separator)).get();
                auto exported = folder.CreateFileAsync(path.substr(separator + 1), CreationCollisionOption::ReplaceExisting).get();
                auto jsonFile = StorageFile::GetFileFromPathAsync(argv[6]).get(); auto json = FileIO::ReadTextAsync(jsonFile).get();
                auto source = file.OpenReadAsync().get(); auto destination = exported.OpenAsync(FileAccessMode::ReadWrite).get();
                if (argc == 8)
                {
                    auto opened = writerStatics.OpenAsync(source); source.Close(); source = nullptr;
                    auto writer = opened.get(); opened = nullptr;
                    ExpectError([&] { writer.ReadPageSizeAsync(UINT32_MAX).get(); }, E_FAIL);
                    ExpectError([&] { writer.WriteAsync(nullptr).get(); }, E_INVALIDARG);
                    ExpectFailure([&] { writer.AppendAsync(L"invalid json").get(); });
                    writer.AppendAsync(L"[]").get();
                    auto items = Windows::Data::Json::JsonArray::Parse(json);
                    auto systemDocument = Windows::Data::Pdf::PdfDocument::LoadFromFileAsync(file).get();
                    for (auto const& item : items)
                    {
                        auto pageIndex = static_cast<uint32_t>(item.GetObject().GetNamedNumber(L"page"));
                        auto native = writer.ReadPageSizeAsync(pageIndex).get(); auto systemPage = systemDocument.GetPage(pageIndex); auto system = systemPage.Size(); systemPage.Close();
                        Require(native.Width > 0 && native.Height > 0 && std::abs(native.Width * 96.0 / 72.0 - system.Width) < .05 &&
                            std::abs(native.Height * 96.0 / 72.0 - system.Height) < .05, "Writer rotated CropBox differs from Windows");
                        Windows::Data::Json::JsonArray single; single.Append(item); writer.AppendAsync(single.Stringify()).get();
                    }
                    auto write = writer.WriteAsync(destination); destination.Close(); destination = nullptr; write.get(); write = nullptr;
                    auto queuedSize = writer.ReadPageSizeAsync(0); auto weakWriter = make_weak(writer); writer.Close(); writer.Close();
                    ExpectError([&] { writer.ReadPageSizeAsync(0).get(); }, RO_E_CLOSED);
                    ExpectError([&] { writer.AppendAsync(L"[]").get(); }, RO_E_CLOSED);
                    auto unusedOutput = exported.OpenAsync(FileAccessMode::ReadWrite).get();
                    ExpectError([&] { writer.WriteAsync(unusedOutput).get(); }, RO_E_CLOSED); unusedOutput.Close();
                    writer = nullptr;
                    Require(queuedSize.get().Width > 0, "Queued writer size failed after owner release"); queuedSize = nullptr;
                    Require(!weakWriter.get(), "Completed operation retains the writer");
                    std::wcout << L"PASS: actual writer DLL, batch annotations, rotated CropBox and queued close\n";
                }
                else { auto exportAction = statics.ExportAsync(source, destination, json);
                    source.Close(); source = nullptr; destination.Close(); destination = nullptr; exportAction.get(); exportAction = nullptr; }
                auto verifyInput = exported.OpenReadAsync().get(); auto verify = statics.OpenAsync(verifyInput).get();
                auto verifyPage = verify.ReadPageAsync(index).get(); Require(verifyPage.Text() == text, "Export changed PDF text");
                verify.Close(); verifyInput.Close(); verify = nullptr; verifyInput = nullptr; verifyPage = nullptr;
                std::wcout << L"PASS: actual exported PDF preserves page text\n";
            }
        }
        using CanUnload = HRESULT(WINAPI*)(); auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(module, "DllCanUnloadNow"));
        Require(canUnload && canUnload() == S_OK, "Document component retains objects after release");
        std::cout << "PASS: document/DTO/writer factories, stream cloning, queued close, errors and DLL unload\n";
        return 0;
    }
    catch (hresult_error const& error) { std::wcerr << error.message().c_str() << L" (" << std::hex << error.code().value << L")\n"; return 1; }
    catch (std::exception const& error) { std::cerr << error.what() << '\n'; return 1; }
}
