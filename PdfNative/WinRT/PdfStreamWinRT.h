#pragma once
#include "../PdfDocumentCore.h"
#include <robuffer.h>
#include <winrt/Windows.Storage.Streams.h>
namespace PdfNativeCore
{
    class WinRTPdfStream final : public PdfStream
    {
        winrt::Windows::Storage::Streams::IRandomAccessStream stream;
        winrt::Windows::Storage::Streams::IBuffer buffer{nullptr};
        void EnsureBuffer() { if (!buffer) buffer = winrt::Windows::Storage::Streams::Buffer(65536); }
        unsigned char* Bytes()
        {
            auto access = buffer.as<::Windows::Storage::Streams::IBufferByteAccess>();
            unsigned char* bytes = nullptr; winrt::check_hresult(access->Buffer(&bytes)); return bytes;
        }
        template<class F> HRESULT Call(F action) noexcept
        {
            try { action(); return S_OK; }
            catch (winrt::hresult_error const& error) { return error.code().value; }
            catch (std::bad_alloc const&) { return E_OUTOFMEMORY; }
            catch (...) { return E_FAIL; }
        }
    public:
        explicit WinRTPdfStream(winrt::Windows::Storage::Streams::IRandomAccessStream clone) : stream(std::move(clone)) { }
        ~WinRTPdfStream() override { try { if (stream) stream.Close(); } catch (...) { } }
        HRESULT Read(unsigned char** bytes, unsigned int* count) noexcept override
        {
            return Call([&] { EnsureBuffer(); buffer = stream.ReadAsync(buffer, 65536, winrt::Windows::Storage::Streams::InputStreamOptions::None).get(); *bytes = Bytes(); *count = buffer.Length(); });
        }
        HRESULT Write(const void* bytes, unsigned int count, unsigned int* written) noexcept override
        {
            return Call([&] { EnsureBuffer(); memcpy(Bytes(), bytes, count); buffer.Length(count); *written = stream.WriteAsync(buffer).get(); });
        }
        HRESULT Seek(uint64_t position) noexcept override { return Call([&] { stream.Seek(position); }); }
        HRESULT Size(uint64_t* size) noexcept override { return Call([&] { *size = stream.Size(); }); }
        HRESULT Position(uint64_t* position) noexcept override { return Call([&] { *position = stream.Position(); }); }
        HRESULT Resize(uint64_t size) noexcept override { return Call([&] { stream.Size(size); }); }
        HRESULT Flush(bool* flushed) noexcept override { return Call([&] { *flushed = stream.FlushAsync().get(); }); }
    };
    template<class F> auto TranslateWinRT(F action) -> decltype(action())
    {
        try { return action(); }
        catch (PdfFailure const& error) { throw winrt::hresult_error(error.code, error.message); }
        catch (std::bad_alloc const&) { throw winrt::hresult_error(E_OUTOFMEMORY); }
    }
}
