#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <mupdf/fitz.h>
#include <memory>
#include <string>
#include <vector>
#include <utility>
#include <exception>

namespace PdfNativeCore
{
    struct PdfFailure : std::exception
    {
        HRESULT code;
        std::wstring message;
        PdfFailure(HRESULT value, std::wstring text) : code(value), message(std::move(text)) { }
        const char* what() const noexcept override { return "PDF operation failed"; }
    };
    // An adapter owns a caller-created stream clone. Buffers stay valid until
    // the next Read; callbacks return HRESULT rather than crossing MuPDF's
    // setjmp/longjmp boundary with a projection or C++ exception.
    struct PdfStream
    {
        virtual ~PdfStream() { }
        virtual HRESULT Read(unsigned char** bytes, unsigned int* count) noexcept = 0;
        virtual HRESULT Write(const void* bytes, unsigned int count, unsigned int* written) noexcept = 0;
        virtual HRESULT Seek(uint64_t position) noexcept = 0;
        virtual HRESULT Size(uint64_t* size) noexcept = 0;
        virtual HRESULT Position(uint64_t* position) noexcept = 0;
        virtual HRESULT Resize(uint64_t size) noexcept = 0;
        virtual HRESULT Flush(bool* flushed) noexcept = 0;
    };
    struct OutlineCommand { char op; float coordinates[6]; };
    struct ExportAnnotation
    {
        int page;
        bool ink;
        float color[3], opacity, width;
        std::vector<fz_quad> quads;
        std::vector<fz_point> points;
        std::vector<OutlineCommand> outline;
        bool evenOdd, highlighter, darken;
    };
    struct TextPageData { std::wstring text; std::vector<float> coordinates; };
    struct TextDocumentState;
    std::shared_ptr<TextDocumentState> OpenDocument(std::shared_ptr<PdfStream> input);
    TextPageData ReadPage(const std::shared_ptr<TextDocumentState>& state, unsigned int index);
    std::pair<float, float> ReadPageSize(const std::shared_ptr<TextDocumentState>& state, unsigned int index);
    std::vector<ExportAnnotation> ParseAnnotations(const wchar_t* json, unsigned int length);
    void AppendAnnotations(const std::shared_ptr<TextDocumentState>& state, std::vector<ExportAnnotation>& items);
    void WriteDocument(const std::shared_ptr<TextDocumentState>& state, std::shared_ptr<PdfStream> output);
}
