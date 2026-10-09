#include "PdfDocumentCore.h"
#include <mupdf/pdf.h>
#include <mutex>
#include <algorithm>
#include <cmath>
#include <limits>
using namespace PdfNativeCore;
namespace
{
    void Check(HRESULT code, const wchar_t* message)
    {
        if (FAILED(code)) throw PdfFailure(code, message);
    }
    struct StreamState
    {
        std::shared_ptr<PdfStream> stream;
        uint64_t length = 0;
        explicit StreamState(std::shared_ptr<PdfStream> value) : stream(std::move(value))
        {
            if (!stream) throw PdfFailure(E_INVALIDARG, L"Missing PDF stream");
            Check(stream->Size(&length), L"Cannot read PDF stream size");
            if (length > static_cast<uint64_t>((std::numeric_limits<int64_t>::max)()))
                throw PdfFailure(E_INVALIDARG, L"PDF stream size exceeds seek range");
            Check(stream->Seek(0), L"Cannot seek PDF stream");
        }
    };
    int NextStream(fz_context* ctx, fz_stream* stream, size_t)
    {
        auto state = static_cast<StreamState*>(stream->state);
        unsigned char* bytes = nullptr;
        unsigned int count = 0;
        if (FAILED(state->stream->Read(&bytes, &count)) || count > 65536 || (count && !bytes))
            fz_throw(ctx, FZ_ERROR_SYSTEM, "PDF file read failed");
        if (!count) return EOF;
        stream->rp = bytes;
        stream->wp = bytes + count;
        stream->pos += count;
        return *stream->rp++;
    }
    void SeekStream(fz_context* ctx, fz_stream* stream, int64_t offset, int whence)
    {
        auto state = static_cast<StreamState*>(stream->state);
        int64_t base = whence == SEEK_END ? static_cast<int64_t>(state->length) : whence == SEEK_CUR ? fz_tell(ctx, stream) : 0;
        if ((offset > 0 && base > (std::numeric_limits<int64_t>::max)() - offset) ||
            (offset < 0 && base < (std::numeric_limits<int64_t>::min)() - offset))
            fz_throw(ctx, FZ_ERROR_ARGUMENT, "Invalid PDF seek");
        auto position = base + offset;
        if (position < 0 || FAILED(state->stream->Seek(static_cast<uint64_t>(position))))
            fz_throw(ctx, FZ_ERROR_SYSTEM, "PDF file seek failed");
        stream->pos = position;
        stream->rp = stream->wp = nullptr;
    }
    void DropStream(fz_context*, void* state) { delete static_cast<StreamState*>(state); }
    PdfFailure PdfError(fz_context* ctx)
    {
        // Consume the native error when translating it to the binding exception.
        // Otherwise context destruction reports the already handled failure again.
        auto message = fz_convert_error(ctx, nullptr);
        fz_warn(ctx, "PDF operation failed: %s", message);
        auto count = MultiByteToWideChar(CP_UTF8, 0, message, -1, nullptr, 0);
        std::wstring text(count > 0 ? count : 1, L'\0');
        if (count > 0) MultiByteToWideChar(CP_UTF8, 0, message, -1, &text[0], count);
        if (!text.empty() && text.back() == L'\0') text.pop_back();
        return PdfFailure(E_FAIL, std::move(text));
    }
}
struct PdfNativeCore::TextDocumentState
{
    std::mutex gate;
    std::mutex locks[FZ_LOCK_MAX];
    fz_locks_context lockCallbacks;
    fz_context* context = nullptr;
    fz_document* document = nullptr;
    TextDocumentState()
    {
        lockCallbacks.user = this;
        lockCallbacks.lock = [](void* user, int index) { static_cast<TextDocumentState*>(user)->locks[index].lock(); };
        lockCallbacks.unlock = [](void* user, int index) { static_cast<TextDocumentState*>(user)->locks[index].unlock(); };
        context = fz_new_context(nullptr, &lockCallbacks, 16 * 1024 * 1024);
        if (!context) throw std::bad_alloc();
    }
    ~TextDocumentState()
    {
        if (context) { fz_drop_document(context, document); fz_drop_context(context); }
    }
};


namespace
{
    void OpenInput(TextDocumentState* state, StreamState* input)
    {
        auto ctx = state->context;
        fz_stream* stream = nullptr;
        fz_var(stream); fz_var(input);
        fz_try(ctx)
        {
            fz_register_document_handlers(ctx);
            // fz_new_stream consumes state on both success and allocation failure.
            auto transferred = input;
            input = nullptr;
            stream = fz_new_stream(ctx, transferred, NextStream, DropStream);
            stream->seek = SeekStream;
            state->document = fz_open_document_with_stream(ctx, "application/pdf", stream);
            if (fz_needs_password(ctx, state->document))
                fz_throw(ctx, FZ_ERROR_ARGUMENT, "Password-protected PDF text is unavailable");
        }
        fz_always(ctx) { delete input; fz_drop_stream(ctx, stream); }
        fz_catch(ctx) { throw PdfError(ctx); }
    }
    void AppendCharacter(std::wstring* text, std::vector<float>* coordinates, int codepoint, const fz_quad& quad, const fz_rect& bounds, float angle)
    {
        if (codepoint < 0 || codepoint > 0x10ffff) codepoint = 0xfffd;
        int units = 1;
        if (codepoint > 0xffff)
        {
            codepoint -= 0x10000;
            text->push_back(static_cast<wchar_t>(0xd800 + (codepoint >> 10)));
            text->push_back(static_cast<wchar_t>(0xdc00 + (codepoint & 0x3ff)));
            units = 2;
        }
        else text->push_back(static_cast<wchar_t>(codepoint));
        const float width = bounds.x1 - bounds.x0, height = bounds.y1 - bounds.y0;
        float values[] = { (quad.ul.x - bounds.x0) / width, (quad.ul.y - bounds.y0) / height,
            (quad.ur.x - bounds.x0) / width, (quad.ur.y - bounds.y0) / height,
            (quad.ll.x - bounds.x0) / width, (quad.ll.y - bounds.y0) / height,
            (quad.lr.x - bounds.x0) / width, (quad.lr.y - bounds.y0) / height, angle };
        while (units--) coordinates->insert(coordinates->end(), values, values + 9);
    }

    void ExtractPage(PdfNativeCore::TextDocumentState* state, unsigned int index, std::wstring* text, std::vector<float>* coordinates)
    {
        auto ctx = state->context;
        fz_page* page = nullptr;
        fz_stext_page* words = nullptr;
        fz_device* device = nullptr;
        fz_var(page); fz_var(words); fz_var(device);
        std::exception_ptr conversionFailure;
        fz_try(ctx)
        {
            page = fz_load_page(ctx, state->document, index);
            auto bounds = fz_bound_page(ctx, page);
            if (bounds.x1 <= bounds.x0 || bounds.y1 <= bounds.y0) fz_throw(ctx, FZ_ERROR_FORMAT, "Invalid PDF page bounds");
            fz_stext_options options = {};
            options.flags = FZ_STEXT_PRESERVE_WHITESPACE | FZ_STEXT_CLIP;
            words = fz_new_stext_page(ctx, bounds);
            device = fz_new_stext_device(ctx, words, &options);
            // The pinned processor extension forwards image placement only to
            // this text device, preserving ActualText without decoding pixels.
            fz_enable_device_hints(ctx, device, FZ_DONT_DECODE_IMAGES | 0x10000);
            fz_run_page_contents(ctx, page, device, fz_identity, nullptr);
            fz_close_device(ctx, device);
            try
            {
                for (auto block = words->first_block; block; block = block->next)
                {
                    if (block->type != FZ_STEXT_BLOCK_TEXT) continue;
                    for (auto line = block->u.t.first_line; line; line = line->next)
                    {
                        float angle = std::atan2(line->dir.y, line->dir.x);
                        for (auto ch = line->first_char; ch; ch = ch->next)
                            AppendCharacter(text, coordinates, ch->c, ch->quad, bounds, angle);
                        if (line->last_char)
                            AppendCharacter(text, coordinates, '\n', line->last_char->quad, bounds, angle);
                    }
                }
            }
            catch (...) { conversionFailure = std::current_exception(); }
        }
        fz_always(ctx) { fz_drop_device(ctx, device); fz_drop_stext_page(ctx, words); fz_drop_page(ctx, page); }
        fz_catch(ctx) { throw PdfError(ctx); }
        if (conversionFailure) std::rethrow_exception(conversionFailure);
    }
    void WriteOutput(fz_context* ctx, void* opaque, const void* data, size_t length)
    {
        auto state = static_cast<StreamState*>(opaque);
        auto bytes = static_cast<const unsigned char*>(data);
        while (length)
        {
            auto count = static_cast<unsigned int>((std::min<size_t>)(length, 65536));
            unsigned int written = 0;
            if (FAILED(state->stream->Write(bytes, count, &written)) || !written || written > count)
                fz_throw(ctx, FZ_ERROR_SYSTEM, "Cannot write exported PDF");
            bytes += written;
            length -= written;
        }
    }
    void CloseOutput(fz_context* ctx, void* opaque)
    {
        bool flushed = false;
        if (FAILED(static_cast<StreamState*>(opaque)->stream->Flush(&flushed)) || !flushed)
            fz_throw(ctx, FZ_ERROR_SYSTEM, "Cannot flush exported PDF");
    }
    void SeekOutput(fz_context* ctx, void* opaque, int64_t offset, int whence)
    {
        auto state = static_cast<StreamState*>(opaque);
        uint64_t base = 0;
        auto hr = whence == SEEK_CUR ? state->stream->Position(&base) : whence == SEEK_END ? state->stream->Size(&base) : S_OK;
        if (FAILED(hr) || base > static_cast<uint64_t>((std::numeric_limits<int64_t>::max)()) ||
            (offset > 0 && static_cast<int64_t>(base) > (std::numeric_limits<int64_t>::max)() - offset) ||
            (offset < 0 && static_cast<int64_t>(base) < (std::numeric_limits<int64_t>::min)() - offset))
            fz_throw(ctx, FZ_ERROR_SYSTEM, "Cannot seek exported PDF");
        auto position = static_cast<int64_t>(base) + offset;
        if (position < 0 || FAILED(state->stream->Seek(static_cast<uint64_t>(position))))
            fz_throw(ctx, FZ_ERROR_SYSTEM, "Cannot seek exported PDF");
    }
    int64_t TellOutput(fz_context* ctx, void* opaque)
    {
        uint64_t position = 0;
        if (FAILED(static_cast<StreamState*>(opaque)->stream->Position(&position)) ||
            position > static_cast<uint64_t>((std::numeric_limits<int64_t>::max)()))
            fz_throw(ctx, FZ_ERROR_SYSTEM, "Cannot read exported PDF position");
        return static_cast<int64_t>(position);
    }
    void TruncateOutput(fz_context* ctx, void* opaque)
    {
        auto state = static_cast<StreamState*>(opaque);
        uint64_t position = 0;
        if (FAILED(state->stream->Position(&position)) || FAILED(state->stream->Resize(position)))
            fz_throw(ctx, FZ_ERROR_SYSTEM, "Cannot truncate exported PDF");
    }
    void SetInkOutline(fz_context* ctx, pdf_document* document, pdf_page* page, pdf_annot* annotation,
        const ExportAnnotation* item, fz_matrix normalizedToPage)
    {
        fz_path* path = nullptr;
        fz_buffer* contents = nullptr;
        pdf_obj* resources = nullptr;
        fz_var(path); fz_var(contents); fz_var(resources);
        fz_try(ctx)
        {
            fz_matrix pageTransform;
            fz_rect pageBox;
            pdf_page_transform(ctx, page, &pageBox, &pageTransform);
            auto toPdf = fz_concat(normalizedToPage, fz_invert_matrix(pageTransform));
            path = fz_new_path(ctx);
            contents = fz_new_buffer(ctx, 4096);
            resources = pdf_new_dict(ctx, document, 1);
            auto states = pdf_dict_put_dict(ctx, resources, PDF_NAME(ExtGState), 1);
            auto state = pdf_dict_puts_dict(ctx, states, "GS", 3);
            pdf_dict_put_real(ctx, state, PDF_NAME(ca), item->opacity);
            pdf_dict_put_real(ctx, state, PDF_NAME(CA), item->opacity);
            if (item->darken) pdf_dict_put_name(ctx, state, PDF_NAME(BM), "Darken");
            else if (item->highlighter) pdf_dict_put_name(ctx, state, PDF_NAME(BM), "Multiply");
            fz_append_printf(ctx, contents, "q /GS gs %g %g %g rg\n", item->color[0], item->color[1], item->color[2]);
            for (const auto& command : item->outline)
            {
                fz_point p[3];
                auto count = command.op == 'h' ? 0 : command.op == 'c' ? 3 : 1;
                for (int i = 0; i < count; ++i)
                {
                    p[i] = fz_transform_point(fz_make_point(command.coordinates[i * 2], command.coordinates[i * 2 + 1]), toPdf);
                    fz_append_printf(ctx, contents, "%g %g ", p[i].x, p[i].y);
                }
                fz_append_printf(ctx, contents, "%c\n", command.op);
                if (command.op == 'm') fz_moveto(ctx, path, p[0].x, p[0].y);
                else if (command.op == 'l') fz_lineto(ctx, path, p[0].x, p[0].y);
                else if (command.op == 'c') fz_curveto(ctx, path, p[0].x, p[0].y, p[1].x, p[1].y, p[2].x, p[2].y);
                else fz_closepath(ctx, path);
            }
            fz_append_string(ctx, contents, item->evenOdd ? "f* Q\n" : "f Q\n");
            auto bounds = fz_bound_path(ctx, path, nullptr, fz_identity);
            // MuPDF's Rect setter excludes Ink because its stock appearance
            // derives bounds from a constant-width InkList. Our outline supplies
            // the true PDF-space bounds, including pressure and pen-tip extents.
            pdf_dict_put_rect(ctx, pdf_annot_obj(ctx, annotation), PDF_NAME(Rect), bounds);
            pdf_set_annot_appearance(ctx, annotation, "N", nullptr, fz_identity, bounds, resources, contents);
        }
        fz_always(ctx) { fz_drop_path(ctx, path); fz_drop_buffer(ctx, contents); pdf_drop_obj(ctx, resources); }
        fz_catch(ctx) { fz_rethrow(ctx); }
    }

    void AddAnnotation(fz_context* ctx, pdf_document* document, ExportAnnotation* item)
    {
        pdf_page* page = nullptr;
        pdf_annot* annotation = nullptr;
        fz_var(page); fz_var(annotation);
        fz_try(ctx)
        {
            page = pdf_load_page(ctx, document, item->page);
            auto bounds = fz_bound_page(ctx, reinterpret_cast<fz_page*>(page));
            auto transform = fz_make_matrix(bounds.x1 - bounds.x0, 0, 0, bounds.y1 - bounds.y0, bounds.x0, bounds.y0);
            annotation = pdf_create_annot(ctx, page, item->ink ? PDF_ANNOT_INK : PDF_ANNOT_HIGHLIGHT);
            pdf_set_annot_color(ctx, annotation, 3, item->color);
            pdf_set_annot_opacity(ctx, annotation, item->opacity);
            pdf_set_annot_author(ctx, annotation, "uwPDF");
            if (item->ink)
            {
                for (auto& point : item->points) point = fz_transform_point(point, transform);
                auto count = static_cast<int>(item->points.size());
                pdf_set_annot_ink_list(ctx, annotation, 1, &count, item->points.data());
                pdf_set_annot_border_width(ctx, annotation, std::max(0.1f, item->width * (bounds.x1 - bounds.x0)));
            }
            else
            {
                for (auto& quad : item->quads) quad = fz_transform_quad(quad, transform);
                pdf_set_annot_quad_points(ctx, annotation, static_cast<int>(item->quads.size()), item->quads.data());
            }
            pdf_update_annot(ctx, annotation);
            if (item->ink && !item->outline.empty()) SetInkOutline(ctx, document, page, annotation, item, transform);
        }
        fz_always(ctx) { pdf_drop_annot(ctx, annotation); fz_drop_page(ctx, reinterpret_cast<fz_page*>(page)); }
        fz_catch(ctx) { fz_rethrow(ctx); }
    }


    void ExportDocument(TextDocumentState* state, StreamState* destination)
    {
        auto ctx = state->context;
        fz_output* output = nullptr;
        fz_var(output); fz_var(destination);
        fz_try(ctx)
        {
            // fz_new_output also consumes state on allocation failure.
            auto transferred = destination;
            destination = nullptr;
            output = fz_new_output(ctx, 65536, transferred, WriteOutput, CloseOutput, DropStream);
            output->seek = SeekOutput;
            output->tell = TellOutput;
            output->truncate = TruncateOutput;
            auto pdf = pdf_specifics(ctx, state->document);
            if (!pdf) fz_throw(ctx, FZ_ERROR_ARGUMENT, "Not a PDF document");
            auto options = pdf_default_write_options;
            options.do_compress = 1;
            pdf_write_document(ctx, pdf, output, &options);
            fz_close_output(ctx, output);
        }
        fz_always(ctx) { delete destination; fz_drop_output(ctx, output); }
        fz_catch(ctx) { throw PdfError(ctx); }
    }
}

std::shared_ptr<TextDocumentState> PdfNativeCore::OpenDocument(std::shared_ptr<PdfStream> input)
{
    auto state = std::make_shared<TextDocumentState>();
    OpenInput(state.get(), new StreamState(std::move(input)));
    return state;
}
TextPageData PdfNativeCore::ReadPage(const std::shared_ptr<TextDocumentState>& state, unsigned int index)
{
    if (!state) throw PdfFailure(RO_E_CLOSED, L"Closed PDF document");
    std::lock_guard<std::mutex> guard(state->gate);
    TextPageData result;
    ExtractPage(state.get(), index, &result.text, &result.coordinates);
    return result;
}
std::pair<float, float> PdfNativeCore::ReadPageSize(const std::shared_ptr<TextDocumentState>& state, unsigned int index)
{
    if (!state) throw PdfFailure(RO_E_CLOSED, L"Closed PDF document");
    std::lock_guard<std::mutex> guard(state->gate);
    auto ctx = state->context;
    fz_page* page = nullptr;
    fz_rect bounds = {};
    fz_var(page); fz_var(bounds);
    fz_try(ctx)
    {
        if (index >= static_cast<unsigned int>(fz_count_pages(ctx, state->document)))
            fz_throw(ctx, FZ_ERROR_ARGUMENT, "Invalid PDF page index");
        page = fz_load_page(ctx, state->document, index);
        bounds = fz_bound_page(ctx, page);
        if (!(bounds.x1 > bounds.x0 && bounds.y1 > bounds.y0))
            fz_throw(ctx, FZ_ERROR_FORMAT, "Invalid PDF page bounds");
    }
    fz_always(ctx) { fz_drop_page(ctx, page); }
    fz_catch(ctx) { throw PdfError(ctx); }
    return {bounds.x1 - bounds.x0, bounds.y1 - bounds.y0};
}
void PdfNativeCore::AppendAnnotations(const std::shared_ptr<TextDocumentState>& state, std::vector<ExportAnnotation>& items)
{
    if (!state) throw PdfFailure(RO_E_CLOSED, L"Closed PDF document");
    std::lock_guard<std::mutex> guard(state->gate);
    auto ctx = state->context;
    fz_try(ctx)
    {
        auto pdf = pdf_specifics(ctx, state->document);
        if (!pdf) fz_throw(ctx, FZ_ERROR_ARGUMENT, "Not a PDF document");
        for (auto& item : items) AddAnnotation(ctx, pdf, &item);
    }
    fz_catch(ctx) { throw PdfError(ctx); }
}
void PdfNativeCore::WriteDocument(const std::shared_ptr<TextDocumentState>& state, std::shared_ptr<PdfStream> output)
{
    if (!state) throw PdfFailure(RO_E_CLOSED, L"Closed PDF document");
    if (!output) throw PdfFailure(E_INVALIDARG, L"Missing PDF output");
    std::lock_guard<std::mutex> guard(state->gate);
    Check(output->Resize(0), L"Cannot reset PDF output");
    ExportDocument(state.get(), new StreamState(std::move(output)));
}
