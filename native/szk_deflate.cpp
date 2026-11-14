// sevenzipkit: raw deflate through 7-Zip's Deflate encoder. See sevenzipkit.h.
//
// 7-Zip's library exports its codecs as well as its archive handlers
// (GetNumberOfMethods, GetMethodProperty, CreateEncoder). This finds the
// Deflate codec (method id 0x040108) and drives it as an ICompressCoder over
// memory streams. The interface ids are defined here rather than taken from
// 7-Zip's headers, whose IID_* symbols bit7z also defines.

#include "sevenzipkit.h"

#ifdef _WIN32
// The full windows.h (not WIN32_LEAN_AND_MEAN): 7-Zip's headers need its OLE
// types (IUnknown, PROPVARIANT).
#  include <windows.h>
#else
#  include <dlfcn.h>
#endif

#include <Common/MyWindows.h>
#include <7zip/ICoder.h>

#include <cstring>
#include <exception>
#include <string>

namespace {

constexpr UInt64 kDeflateMethodId = 0x040108;

constexpr GUID make_7zip_iid(unsigned char group, unsigned char sub) {
    return GUID{k_7zip_GUID_Data1, k_7zip_GUID_Data2, k_7zip_GUID_Data3_Common, {0, 0, 0, group, 0, sub, 0, 0}};
}

constexpr GUID kIidUnknown = {0x00000000, 0x0000, 0x0000, {0xC0, 0, 0, 0, 0, 0, 0, 0x46}};
constexpr GUID kIidSequentialInStream = make_7zip_iid(3, 0x01);
constexpr GUID kIidSequentialOutStream = make_7zip_iid(3, 0x02);
constexpr GUID kIidCompressCoder = make_7zip_iid(4, 0x05);
constexpr GUID kIidCompressSetCoderProperties = make_7zip_iid(4, 0x20);

bool same_iid(REFIID a, const GUID &b) { return std::memcmp(&a, &b, sizeof(GUID)) == 0; }

typedef HRESULT(STDAPICALLTYPE *GetNumberOfMethodsFn)(UInt32 *count);
typedef HRESULT(STDAPICALLTYPE *GetMethodPropertyFn)(UInt32 index, PROPID prop, PROPVARIANT *value);
typedef HRESULT(STDAPICALLTYPE *CreateEncoderFn)(UInt32 index, const GUID *iid, void **out);

// Streams live on the caller's stack for one Code() call; the encoder may
// AddRef/Release them, so reference counting is kept but never deletes.
class MemoryInStream final : public ISequentialInStream {
public:
    MemoryInStream(const void *data, uint32_t size) : data_(static_cast<const Byte *>(data)), size_(size) {}

    STDMETHOD(QueryInterface)(REFIID iid, void **out) override {
        if (same_iid(iid, kIidUnknown) || same_iid(iid, kIidSequentialInStream)) {
            *out = static_cast<ISequentialInStream *>(this);
            return S_OK;
        }
        *out = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHOD_(ULONG, AddRef)() override { return 1; }
    STDMETHOD_(ULONG, Release)() override { return 1; }

    Z7_COM7F_IMF(Read(void *data, UInt32 size, UInt32 *processed)) override {
        const uint32_t n = size < size_ - pos_ ? size : size_ - pos_;
        std::memcpy(data, data_ + pos_, n);
        pos_ += n;
        if (processed != nullptr) *processed = n;
        return S_OK;
    }

private:
    const Byte *data_;
    uint32_t size_;
    uint32_t pos_ = 0;
};

// Writes into a fixed buffer; output past its end is dropped and remembered.
class MemoryOutStream final : public ISequentialOutStream {
public:
    MemoryOutStream(void *data, uint32_t capacity) : data_(static_cast<Byte *>(data)), capacity_(capacity) {}

    STDMETHOD(QueryInterface)(REFIID iid, void **out) override {
        if (same_iid(iid, kIidUnknown) || same_iid(iid, kIidSequentialOutStream)) {
            *out = static_cast<ISequentialOutStream *>(this);
            return S_OK;
        }
        *out = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHOD_(ULONG, AddRef)() override { return 1; }
    STDMETHOD_(ULONG, Release)() override { return 1; }

    Z7_COM7F_IMF(Write(const void *data, UInt32 size, UInt32 *processed)) override {
        if (!overflow_ && size <= capacity_ - size_) {
            std::memcpy(data_ + size_, data, size);
            size_ += size;
        } else {
            overflow_ = true;
        }
        if (processed != nullptr) *processed = size;
        return S_OK;
    }

    uint32_t size() const { return size_; }
    bool overflow() const { return overflow_; }

private:
    Byte *data_;
    uint32_t capacity_;
    uint32_t size_ = 0;
    bool overflow_ = false;
};

#ifdef _WIN32
typedef HMODULE Module;
Module load_module(const char *utf8_path) {
    const int n = MultiByteToWideChar(CP_UTF8, 0, utf8_path, -1, nullptr, 0);
    if (n <= 0) return nullptr;
    std::wstring wide(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8_path, -1, &wide[0], n);
    return LoadLibraryW(wide.c_str());
}
void *find_symbol(Module m, const char *name) { return reinterpret_cast<void *>(GetProcAddress(m, name)); }
void unload_module(Module m) { FreeLibrary(m); }
#else
typedef void *Module;
Module load_module(const char *path) { return dlopen(path, RTLD_NOW | RTLD_LOCAL); }
void *find_symbol(Module m, const char *name) { return dlsym(m, name); }
void unload_module(Module m) { dlclose(m); }
#endif

} // namespace

struct szk_deflater {
    Module module = nullptr;
    ICompressCoder *coder = nullptr;
    std::string error;

    ~szk_deflater() {
        if (coder != nullptr) coder->Release();
        if (module != nullptr) unload_module(module);
    }
};

extern "C" {

SZK_API szk_status SZK_CALL szk_deflater_create(const char *lib7z_path, int32_t level,
                                                szk_deflater **out, const char **error) {
    static thread_local std::string create_error;
    if (out == nullptr || lib7z_path == nullptr || level < 1 || level > 9) return SZK_BAD_ARGS;
    *out = nullptr;
    auto fail = [&](const std::string &message) {
        create_error = message;
        if (error != nullptr) *error = create_error.c_str();
        return SZK_ERROR;
    };

    auto *deflater = new (std::nothrow) szk_deflater();
    if (deflater == nullptr) return fail("out of memory");
    deflater->module = load_module(lib7z_path);
    if (deflater->module == nullptr) {
        delete deflater;
        return fail(std::string("7-Zip's library could not be loaded from ") + lib7z_path);
    }

    auto count_methods = reinterpret_cast<GetNumberOfMethodsFn>(find_symbol(deflater->module, "GetNumberOfMethods"));
    auto method_property = reinterpret_cast<GetMethodPropertyFn>(find_symbol(deflater->module, "GetMethodProperty"));
    auto create_encoder = reinterpret_cast<CreateEncoderFn>(find_symbol(deflater->module, "CreateEncoder"));
    if (count_methods == nullptr || method_property == nullptr || create_encoder == nullptr) {
        delete deflater;
        return fail("7-Zip's library does not export its codecs");
    }

    UInt32 count = 0;
    UInt32 index = UINT32_MAX;
    if (count_methods(&count) == S_OK) {
        for (UInt32 i = 0; i < count && index == UINT32_MAX; i++) {
            PROPVARIANT id;
            std::memset(&id, 0, sizeof(id));
            if (method_property(i, NMethodPropID::kID, &id) == S_OK && id.vt == VT_UI8 &&
                id.uhVal.QuadPart == kDeflateMethodId) {
                index = i;
            }
        }
    }
    if (index == UINT32_MAX) {
        delete deflater;
        return fail("7-Zip's library has no Deflate codec");
    }

    void *coder = nullptr;
    if (create_encoder(index, &kIidCompressCoder, &coder) != S_OK || coder == nullptr) {
        delete deflater;
        return fail("7-Zip's Deflate encoder could not be created");
    }
    deflater->coder = static_cast<ICompressCoder *>(coder);

    void *props = nullptr;
    if (deflater->coder->QueryInterface(kIidCompressSetCoderProperties, &props) != S_OK || props == nullptr) {
        delete deflater;
        return fail("7-Zip's Deflate encoder takes no properties");
    }
    auto *set_props = static_cast<ICompressSetCoderProperties *>(props);
    const PROPID ids[] = {NCoderPropID::kLevel};
    PROPVARIANT values[1];
    std::memset(values, 0, sizeof(values));
    values[0].vt = VT_UI4;
    values[0].ulVal = static_cast<ULONG>(level);
    const HRESULT set = set_props->SetCoderProperties(ids, values, 1);
    set_props->Release();
    if (set != S_OK) {
        delete deflater;
        return fail("7-Zip's Deflate encoder rejected the compression level");
    }

    *out = deflater;
    return SZK_OK;
}

SZK_API void SZK_CALL szk_deflater_free(szk_deflater *deflater) { delete deflater; }

SZK_API const char *SZK_CALL szk_deflater_last_error(const szk_deflater *deflater) {
    return deflater != nullptr ? deflater->error.c_str() : "";
}

SZK_API szk_status SZK_CALL szk_deflate(szk_deflater *deflater, const void *in, uint32_t in_size,
                                        void *out, uint32_t out_capacity, uint32_t *out_size) {
    if (deflater == nullptr || out_size == nullptr || (in == nullptr && in_size > 0) ||
        (out == nullptr && out_capacity > 0)) {
        return SZK_BAD_ARGS;
    }
    *out_size = 0;
    deflater->error.clear();
    MemoryInStream input(in, in_size);
    MemoryOutStream output(out, out_capacity);
    const UInt64 size = in_size;
    HRESULT result;
    try {
        result = deflater->coder->Code(&input, &output, &size, nullptr, nullptr);
    } catch (const std::exception &e) {
        deflater->error = e.what();
        return SZK_ERROR;
    } catch (...) {
        deflater->error = "unknown error in 7-Zip's Deflate encoder";
        return SZK_ERROR;
    }
    if (result != S_OK) {
        deflater->error = "7-Zip's Deflate encoder failed with HRESULT " + std::to_string(static_cast<long>(result));
        return SZK_ERROR;
    }
    if (!output.overflow()) *out_size = output.size();
    return SZK_OK;
}

} // extern "C"
