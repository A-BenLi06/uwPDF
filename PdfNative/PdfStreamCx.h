#pragma once
#include "PdfDocumentCore.h"
#include <robuffer.h>
#include <wrl/client.h>
#include <ppltasks.h>
namespace PdfNativeCore
{
    class CxPdfStream final : public PdfStream
    {
        Windows::Storage::Streams::IRandomAccessStream^ stream;
        Windows::Storage::Streams::IBuffer^ buffer;
        void EnsureBuffer() { if (!buffer) buffer = ref new Windows::Storage::Streams::Buffer(65536); }
        unsigned char* Bytes()
        {
            Microsoft::WRL::ComPtr<Windows::Storage::Streams::IBufferByteAccess> access;
            auto hr = reinterpret_cast<IUnknown*>(buffer)->QueryInterface(IID_PPV_ARGS(&access));
            if (FAILED(hr)) throw Platform::Exception::CreateException(hr);
            unsigned char* bytes = nullptr;
            hr = access->Buffer(&bytes);
            if (FAILED(hr)) throw Platform::Exception::CreateException(hr);
            return bytes;
        }
        template<class F> HRESULT Call(F action) noexcept
        {
            try { action(); return S_OK; }
            catch (Platform::Exception^ error) { return error->HResult; }
            catch (std::bad_alloc const&) { return E_OUTOFMEMORY; }
            catch (...) { return E_FAIL; }
        }
    public:
        explicit CxPdfStream(Windows::Storage::Streams::IRandomAccessStream^ clone) : stream(clone) { }
        ~CxPdfStream() override { try { delete stream; } catch (...) { } }
        HRESULT Read(unsigned char** bytes, unsigned int* count) noexcept override
        {
            return Call([&] { EnsureBuffer(); buffer = concurrency::create_task(stream->ReadAsync(buffer, 65536, Windows::Storage::Streams::InputStreamOptions::None)).get(); *bytes = Bytes(); *count = buffer->Length; });
        }
        HRESULT Write(const void* bytes, unsigned int count, unsigned int* written) noexcept override
        {
            return Call([&] { EnsureBuffer(); memcpy(Bytes(), bytes, count); buffer->Length = count; *written = concurrency::create_task(stream->WriteAsync(buffer)).get(); });
        }
        HRESULT Seek(uint64_t position) noexcept override { return Call([&] { stream->Seek(position); }); }
        HRESULT Size(uint64_t* size) noexcept override { return Call([&] { *size = stream->Size; }); }
        HRESULT Position(uint64_t* position) noexcept override { return Call([&] { *position = stream->Position; }); }
        HRESULT Resize(uint64_t size) noexcept override { return Call([&] { stream->Size = size; }); }
        HRESULT Flush(bool* flushed) noexcept override { return Call([&] { *flushed = concurrency::create_task(stream->FlushAsync()).get(); }); }
    };
    template<class F> auto TranslateCx(F action) -> decltype(action())
    {
        try { return action(); }
        catch (PdfFailure const& error) { throw Platform::Exception::CreateException(error.code, ref new Platform::String(error.message.c_str())); }
        catch (std::bad_alloc const&) { throw ref new Platform::OutOfMemoryException(); }
    }
}
