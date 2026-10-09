#include "../PdfNative/PdfTextDocument.h"
#include <windows.h>
#include <roapi.h>
#include <ppltasks.h>
#include <fstream>
#include <iostream>
#include <string>
#include <iomanip>
#include <cmath>

using namespace concurrency;
using namespace Windows::Storage;

// Runs the production WinRT stream adapter and text extraction without a UI.
int wmain(int argc, wchar_t** argv)
{
    if (argc != 4 && argc != 5 && argc != 6 && argc != 7) return 2;
    auto initialized = RoInitialize(RO_INIT_MULTITHREADED);
    if (FAILED(initialized)) return 3;
    struct ApartmentLifetime { ~ApartmentLifetime() { RoUninitialize(); } } apartment;
    try
    {
        auto file = create_task(StorageFile::GetFileFromPathAsync(ref new Platform::String(argv[1]))).get();
        auto input = create_task(file->OpenReadAsync()).get();
        // Callers may have inspected the header before passing us the stream.
        input->Seek(input->Size > 16 ? 16 : 0);
        auto document = create_task(PdfNative::PdfTextDocument::OpenAsync(input)).get();
        // Closing the caller's stream must leave the engine's clone usable.
        delete input;
        auto page = create_task(document->ReadPageAsync(static_cast<unsigned int>(_wtoi(argv[2])))).get();
        std::wstring text(page->Text->Data(), page->Text->Length());
        if (text.find(argv[3]) == std::wstring::npos) { std::wcerr << L"Expected text missing: " << text; return 4; }
        if (page->Coordinates->Length != text.size() * 9) return 5;
        for (unsigned int i = 0; i < page->Coordinates->Length; ++i)
            if (!std::isfinite(page->Coordinates[i])) return 6;
        std::wcout << L"PASS: " << text.size() << L" UTF-16 units, " << page->Coordinates->Length << L" coordinates\n";
        if (argc == 5)
        {
            std::ofstream report(argv[4]);
            report << "{\"text\":\"";
            for (auto unit : text) report << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<unsigned int>(unit);
            report << "\",\"coordinates\":[" << std::dec << std::setprecision(9);
            for (unsigned int i = 0; i < page->Coordinates->Length; ++i) { if (i) report << ','; report << page->Coordinates[i]; }
            report << "]}";
            if (!report) return 8;
        }
        // Closing a document must not invalidate a read already queued on a worker.
        auto pendingRead = create_task(document->ReadPageAsync(static_cast<unsigned int>(_wtoi(argv[2]))));
        delete document;
        auto pendingPage = pendingRead.get();
        if (std::wstring(pendingPage->Text->Data(), pendingPage->Text->Length()) != text) return 9;
        if (argc == 6 || argc == 7)
        {
            std::wstring path(argv[4]);
            auto separator = path.find_last_of(L"/\\");
            auto folder = create_task(StorageFolder::GetFolderFromPathAsync(ref new Platform::String(path.substr(0, separator).c_str()))).get();
            auto exported = create_task(folder->CreateFileAsync(ref new Platform::String(path.substr(separator + 1).c_str()), CreationCollisionOption::ReplaceExisting)).get();
            auto jsonFile = create_task(StorageFile::GetFileFromPathAsync(ref new Platform::String(argv[5]))).get();
            auto json = create_task(FileIO::ReadTextAsync(jsonFile)).get();
            auto source = create_task(file->OpenReadAsync()).get();
            auto destination = create_task(exported->OpenAsync(FileAccessMode::ReadWrite)).get();
            if (argc == 7)
            {
                auto writer = create_task(PdfNative::PdfAnnotationWriter::OpenAsync(source)).get();
                delete source;
                auto items = Windows::Data::Json::JsonArray::Parse(json);
                auto systemDocument = create_task(Windows::Data::Pdf::PdfDocument::LoadFromFileAsync(file)).get();
                for (unsigned int i = 0; i < items->Size; ++i)
                {
                    const auto index = static_cast<unsigned int>(items->GetAt(i)->GetObject()->GetNamedNumber(L"page"));
                    const auto nativeSize = create_task(writer->ReadPageSizeAsync(index)).get();
                    auto systemPage = systemDocument->GetPage(index);
                    const auto systemSize = systemPage->Size;
                    delete systemPage;
                    if (!(nativeSize.Width > 0 && nativeSize.Height > 0) ||
                        std::abs(nativeSize.Width * 96.0 / 72.0 - systemSize.Width) > .05 ||
                        std::abs(nativeSize.Height * 96.0 / 72.0 - systemSize.Height) > .05)
                    {
                        std::wcerr << L"Page size mismatch: native " << nativeSize.Width << L"x" << nativeSize.Height
                            << L", Windows " << systemSize.Width << L"x" << systemSize.Height << L"\n";
                        return 10;
                    }
                    auto single = ref new Windows::Data::Json::JsonArray();
                    single->Append(items->GetAt(i));
                    create_task(writer->AppendAsync(single->Stringify())).get();
                }
                create_task(writer->WriteAsync(destination)).get();
                auto pendingSize = create_task(writer->ReadPageSizeAsync(0));
                delete writer;
                auto retainedSize = pendingSize.get();
                if (!(retainedSize.Width > 0 && retainedSize.Height > 0)) return 11;
                std::wcout << L"PASS: writer page sizes match Windows rotated CropBox; queued size survives close\n";
            }
            else
            {
                create_task(PdfNative::PdfTextDocument::ExportAsync(source, destination, json)).get();
                delete source;
            }
            delete destination;
            auto verifyInput = create_task(exported->OpenReadAsync()).get();
            auto verify = create_task(PdfNative::PdfTextDocument::OpenAsync(verifyInput)).get();
            auto verifyPage = create_task(verify->ReadPageAsync(_wtoi(argv[2]))).get();
            std::wstring verifyText(verifyPage->Text->Data(), verifyPage->Text->Length());
            if (verifyText != text) return 7;
            delete verify; delete verifyInput;
            std::wcout << L"PASS: exported PDF preserves page text\n";
        }
        return 0;
    }
    catch (Platform::Exception^ error)
    {
        std::wcerr << error->Message->Data() << L" (" << std::hex << error->HResult << L")\n";
        return 1;
    }
}
