#include "../PdfNative/PdfDocumentCore.h"
#include <algorithm>
#include <fstream>
#include <future>
#include <iostream>
#include <iterator>
#include <limits>
using namespace PdfNativeCore;
namespace
{
    void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
    template<class F> void Fails(F action, HRESULT expected = E_FAIL)
    {
        try { action(); }
        catch (PdfFailure const& error) { Require(error.code == expected, "Unexpected PDF failure code"); return; }
        throw std::runtime_error("Missing PDF failure");
    }
    enum class Fault { None, Size, Oversized, Seek, Read, NullRead, LargeRead, Resize, Write, ZeroWrite, LargeWrite, Flush, FalseFlush };
    struct MemoryStream final : PdfStream
    {
        std::vector<unsigned char> bytes;
        uint64_t position = 0;
        Fault fault = Fault::None;
        unsigned int readLimit = 65536, writeLimit = 65536;
        explicit MemoryStream(std::vector<unsigned char> data = {}) : bytes(std::move(data)) { }
        HRESULT Read(unsigned char** output, unsigned int* count) noexcept override
        {
            if (fault == Fault::Read) return E_ACCESSDENIED;
            *count = static_cast<unsigned>((std::min<uint64_t>)(position < bytes.size() ? bytes.size() - position : 0, readLimit));
            *output = *count ? bytes.data() + static_cast<size_t>(position) : nullptr;
            if (fault == Fault::NullRead) { *output = nullptr; *count = 1; }
            if (fault == Fault::LargeRead) *count = 65537;
            position += *count;
            return S_OK;
        }
        HRESULT Write(void const* data, unsigned count, unsigned* written) noexcept override
        {
            if (fault == Fault::Write) return E_ACCESSDENIED;
            if (fault == Fault::ZeroWrite) { *written = 0; return S_OK; }
            if (fault == Fault::LargeWrite) { *written = count + 1; return S_OK; }
            *written = (std::min)(count, writeLimit);
            try
            {
                bytes.resize(static_cast<size_t>(position) + *written);
                std::copy_n(static_cast<unsigned char const*>(data), *written, bytes.data() + static_cast<size_t>(position));
                position += *written;
                return S_OK;
            }
            catch (...) { return E_OUTOFMEMORY; }
        }
        HRESULT Seek(uint64_t value) noexcept override { if (fault == Fault::Seek) return E_ACCESSDENIED; position = value; return S_OK; }
        HRESULT Size(uint64_t* value) noexcept override
        {
            if (fault == Fault::Size) return E_ACCESSDENIED;
            *value = fault == Fault::Oversized ? UINT64_MAX : bytes.size(); return S_OK;
        }
        HRESULT Position(uint64_t* value) noexcept override { *value = position; return S_OK; }
        HRESULT Resize(uint64_t value) noexcept override
        {
            if (fault == Fault::Resize) return E_ACCESSDENIED;
            try { bytes.resize(static_cast<size_t>(value)); return S_OK; } catch (...) { return E_OUTOFMEMORY; }
        }
        HRESULT Flush(bool* flushed) noexcept override
        {
            if (fault == Fault::Flush) return E_ACCESSDENIED;
            *flushed = fault != Fault::FalseFlush; return S_OK;
        }
    };
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    try
    {
        std::ifstream file(argv[1], std::ios::binary);
        Require(file.good(), "Missing PDF fixture");
        std::vector<unsigned char> fixture((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        Fails([] { OpenDocument(nullptr); }, E_INVALIDARG);
        for (auto fault : {Fault::Size, Fault::Oversized, Fault::Seek, Fault::Read, Fault::NullRead, Fault::LargeRead})
        {
            auto input = std::make_shared<MemoryStream>(fixture); input->fault = fault;
            std::weak_ptr<PdfStream> weak = input;
            auto code = fault == Fault::Size || fault == Fault::Seek ? E_ACCESSDENIED : fault == Fault::Oversized ? E_INVALIDARG : E_FAIL;
            Fails([&] { OpenDocument(std::move(input)); }, code);
            Require(weak.expired(), "Failed input retains its stream");
        }
        auto invalid = std::make_shared<MemoryStream>(std::vector<unsigned char>{'n','o','t','p','d','f'});
        std::weak_ptr<PdfStream> invalidWeak = invalid;
        Fails([&] { OpenDocument(std::move(invalid)); });
        Require(invalidWeak.expired(), "Invalid PDF retains its stream");
        std::weak_ptr<PdfStream> retainedInput;
        {
            auto input = std::make_shared<MemoryStream>(fixture); input->readLimit = 17;
            retainedInput = input;
            auto state = OpenDocument(std::move(input));
            Require(!retainedInput.expired(), "Open document dropped its input stream");
            auto text = ReadPage(state, 0).text;
            Require(text.find(L"uwPDF") != std::wstring::npos, "Short input reads lost text");
            Fails([&] { ReadPage(state, UINT32_MAX); });
            Fails([&] { ReadPageSize(state, UINT32_MAX); });
            Require(ReadPage(state, 0).text == text, "Failed page read poisoned the context");
            std::vector<std::future<std::wstring>> jobs;
            for (unsigned i = 0; i < 16; ++i)
                jobs.push_back(std::async(std::launch::async, [state] { return ReadPage(state, 0).text; }));
            for (auto& job : jobs) Require(job.get() == text, "Concurrent text read differs");
            Fails([&] { WriteDocument(state, nullptr); }, E_INVALIDARG);
            for (auto fault : {Fault::Resize, Fault::Size, Fault::Seek, Fault::Write, Fault::ZeroWrite, Fault::LargeWrite, Fault::Flush, Fault::FalseFlush})
            {
                auto output = std::make_shared<MemoryStream>(); output->fault = fault;
                std::weak_ptr<PdfStream> weak = output;
                auto code = fault == Fault::Resize || fault == Fault::Size || fault == Fault::Seek ? E_ACCESSDENIED : E_FAIL;
                Fails([&] { WriteDocument(state, std::move(output)); }, code);
                Require(weak.expired(), "Failed output retains its stream");
            }
            auto output = std::make_shared<MemoryStream>(); output->writeLimit = 7;
            WriteDocument(state, output);
            auto written = OpenDocument(output);
            Require(ReadPage(written, 0).text == text, "Short output writes or recovery changed text");
            written.reset();
            std::weak_ptr<PdfStream> outputWeak = output;
            output.reset();
            Require(outputWeak.expired(), "Successful output retains its stream");
        }
        Require(retainedInput.expired(), "Document destruction retains its input stream");
        Fails([] { ReadPage({}, 0); }, RO_E_CLOSED);
        Fails([] { ReadPageSize({}, 0); }, RO_E_CLOSED);
        Fails([] { std::vector<ExportAnnotation> items; AppendAnnotations({}, items); }, RO_E_CLOSED);
        Fails([] { WriteDocument({}, {}); }, RO_E_CLOSED);
        std::cout << "PASS: actual document core, short reads/writes, concurrent reads, invalid input/page, 14 injected stream failures, recovery and stream lifetime\n";
        return 0;
    }
    catch (PdfFailure const& error) { std::wcerr << error.message << L" (" << std::hex << error.code << L")\n"; return 1; }
    catch (std::exception const& error) { std::cerr << error.what() << '\n'; return 1; }
}
