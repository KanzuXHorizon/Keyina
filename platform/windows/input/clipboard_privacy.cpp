#include <keyina/windows/clipboard_privacy.h>

#include <array>
#include <atomic>
#include <cstddef>
#include <cstring>
#include <limits>
#include <new>

namespace keyina::windows {
namespace {

constexpr wchar_t kExcludeClipboardContentFromMonitorProcessing[] =
    L"ExcludeClipboardContentFromMonitorProcessing";
constexpr wchar_t kCanIncludeInClipboardHistory[] =
    L"CanIncludeInClipboardHistory";
constexpr wchar_t kCanUploadToCloudClipboard[] =
    L"CanUploadToCloudClipboard";

HGLOBAL CreateDwordStorage(DWORD value) noexcept {
  HGLOBAL storage = GlobalAlloc(GMEM_MOVEABLE, sizeof(DWORD));
  if (storage == nullptr) {
    return nullptr;
  }
  void* destination = GlobalLock(storage);
  if (destination == nullptr) {
    GlobalFree(storage);
    return nullptr;
  }
  std::memcpy(destination, &value, sizeof(value));
  GlobalUnlock(storage);
  return storage;
}

HGLOBAL CreateUnicodeTextStorage(std::wstring_view text) noexcept {
  if (text.size() >
      (std::numeric_limits<std::size_t>::max() / sizeof(wchar_t)) - 1) {
    return nullptr;
  }
  const std::size_t bytes = (text.size() + 1) * sizeof(wchar_t);
  HGLOBAL storage = GlobalAlloc(GMEM_MOVEABLE, bytes);
  if (storage == nullptr) {
    return nullptr;
  }
  void* destination = GlobalLock(storage);
  if (destination == nullptr) {
    GlobalFree(storage);
    return nullptr;
  }
  if (!text.empty()) {
    std::memcpy(destination, text.data(), text.size() * sizeof(wchar_t));
  }
  static_cast<wchar_t*>(destination)[text.size()] = L'\0';
  GlobalUnlock(storage);
  return storage;
}

void FreeIfOwned(HGLOBAL storage) noexcept {
  if (storage != nullptr) {
    GlobalFree(storage);
  }
}

bool IsPrivacyFormat(
    CLIPFORMAT format,
    const ClipboardPrivacyFormats& formats,
    DWORD& value) noexcept {
  if (format == formats.exclude_from_monitor_processing) {
    value = 1;
    return true;
  }
  if (format == formats.can_include_in_history ||
      format == formats.can_upload_to_cloud) {
    value = 0;
    return true;
  }
  return false;
}

FORMATETC MakeFormatEtc(CLIPFORMAT format) noexcept {
  FORMATETC value{};
  value.cfFormat = format;
  value.dwAspect = DVASPECT_CONTENT;
  value.lindex = -1;
  value.tymed = TYMED_HGLOBAL;
  return value;
}

class PrivacyFormatEnumerator final : public IEnumFORMATETC {
 public:
  PrivacyFormatEnumerator(
      IEnumFORMATETC* inner,
      const ClipboardPrivacyFormats& formats,
      ULONG privacy_index = 0) noexcept
      : inner_(inner),
        privacy_formats_{
            MakeFormatEtc(static_cast<CLIPFORMAT>(
                formats.exclude_from_monitor_processing)),
            MakeFormatEtc(static_cast<CLIPFORMAT>(
                formats.can_include_in_history)),
            MakeFormatEtc(static_cast<CLIPFORMAT>(
                formats.can_upload_to_cloud))},
        privacy_index_(privacy_index) {
    if (inner_ != nullptr) {
      inner_->AddRef();
    }
  }

  ~PrivacyFormatEnumerator() {
    if (inner_ != nullptr) {
      inner_->Release();
    }
  }

  HRESULT STDMETHODCALLTYPE QueryInterface(
      REFIID interface_id,
      void** object) override {
    if (object == nullptr) {
      return E_POINTER;
    }
    *object = nullptr;
    if (interface_id == IID_IUnknown ||
        interface_id == IID_IEnumFORMATETC) {
      *object = static_cast<IEnumFORMATETC*>(this);
      AddRef();
      return S_OK;
    }
    return E_NOINTERFACE;
  }

  ULONG STDMETHODCALLTYPE AddRef() override {
    return reference_count_.fetch_add(1, std::memory_order_relaxed) + 1;
  }

  ULONG STDMETHODCALLTYPE Release() override {
    const ULONG remaining =
        reference_count_.fetch_sub(1, std::memory_order_acq_rel) - 1;
    if (remaining == 0) {
      delete this;
    }
    return remaining;
  }

  HRESULT STDMETHODCALLTYPE Next(
      ULONG requested,
      FORMATETC* values,
      ULONG* fetched) override {
    if (values == nullptr || (requested > 1 && fetched == nullptr)) {
      return E_POINTER;
    }
    ULONG count = 0;
    while (count < requested && privacy_index_ < privacy_formats_.size()) {
      values[count++] = privacy_formats_[privacy_index_++];
    }
    if (count < requested && inner_ != nullptr) {
      ULONG inner_fetched = 0;
      const HRESULT result = inner_->Next(
          requested - count,
          values + count,
          &inner_fetched);
      count += inner_fetched;
      if (FAILED(result) && result != S_FALSE) {
        if (fetched != nullptr) {
          *fetched = count;
        }
        return result;
      }
    }
    if (fetched != nullptr) {
      *fetched = count;
    }
    return count == requested ? S_OK : S_FALSE;
  }

  HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {
    ULONG remaining = count;
    const ULONG privacy_remaining = static_cast<ULONG>(
        privacy_formats_.size() - privacy_index_);
    const ULONG skip_privacy = (remaining < privacy_remaining)
        ? remaining
        : privacy_remaining;
    privacy_index_ += skip_privacy;
    remaining -= skip_privacy;
    if (remaining == 0) {
      return S_OK;
    }
    if (inner_ == nullptr) {
      return S_FALSE;
    }
    return inner_->Skip(remaining);
  }

  HRESULT STDMETHODCALLTYPE Reset() override {
    privacy_index_ = 0;
    return inner_ == nullptr ? S_OK : inner_->Reset();
  }

  HRESULT STDMETHODCALLTYPE Clone(IEnumFORMATETC** clone) override {
    if (clone == nullptr) {
      return E_POINTER;
    }
    *clone = nullptr;
    IEnumFORMATETC* inner_clone = nullptr;
    if (inner_ != nullptr) {
      const HRESULT result = inner_->Clone(&inner_clone);
      if (FAILED(result)) {
        return result;
      }
    }
    auto* copy = new (std::nothrow) PrivacyFormatEnumerator(
        inner_clone,
        ClipboardPrivacyFormats{
            privacy_formats_[0].cfFormat,
            privacy_formats_[1].cfFormat,
            privacy_formats_[2].cfFormat},
        privacy_index_);
    if (inner_clone != nullptr) {
      inner_clone->Release();
    }
    if (copy == nullptr) {
      return E_OUTOFMEMORY;
    }
    *clone = copy;
    return S_OK;
  }

 private:
  std::atomic_ulong reference_count_{1};
  IEnumFORMATETC* inner_{};
  std::array<FORMATETC, 3> privacy_formats_{};
  ULONG privacy_index_{};
};

class PrivateClipboardDataObject final : public IDataObject {
 public:
  PrivateClipboardDataObject(
      IDataObject* inner,
      const ClipboardPrivacyFormats& formats) noexcept
      : inner_(inner), formats_(formats) {
    inner_->AddRef();
  }

  ~PrivateClipboardDataObject() { inner_->Release(); }

  HRESULT STDMETHODCALLTYPE QueryInterface(
      REFIID interface_id,
      void** object) override {
    if (object == nullptr) {
      return E_POINTER;
    }
    *object = nullptr;
    if (interface_id == IID_IUnknown || interface_id == IID_IDataObject) {
      *object = static_cast<IDataObject*>(this);
      AddRef();
      return S_OK;
    }
    return E_NOINTERFACE;
  }

  ULONG STDMETHODCALLTYPE AddRef() override {
    return reference_count_.fetch_add(1, std::memory_order_relaxed) + 1;
  }

  ULONG STDMETHODCALLTYPE Release() override {
    const ULONG remaining =
        reference_count_.fetch_sub(1, std::memory_order_acq_rel) - 1;
    if (remaining == 0) {
      delete this;
    }
    return remaining;
  }

  HRESULT STDMETHODCALLTYPE GetData(
      FORMATETC* format,
      STGMEDIUM* medium) override {
    if (format == nullptr || medium == nullptr) {
      return E_POINTER;
    }
    DWORD value = 0;
    if (!IsPrivacyFormat(format->cfFormat, formats_, value)) {
      return inner_->GetData(format, medium);
    }
    const HRESULT validation = ValidatePrivacyFormat(*format);
    if (FAILED(validation)) {
      return validation;
    }
    HGLOBAL storage = CreateDwordStorage(value);
    if (storage == nullptr) {
      return E_OUTOFMEMORY;
    }
    medium->tymed = TYMED_HGLOBAL;
    medium->hGlobal = storage;
    medium->pUnkForRelease = nullptr;
    return S_OK;
  }

  HRESULT STDMETHODCALLTYPE GetDataHere(
      FORMATETC* format,
      STGMEDIUM* medium) override {
    if (format == nullptr || medium == nullptr) {
      return E_POINTER;
    }
    DWORD value = 0;
    if (!IsPrivacyFormat(format->cfFormat, formats_, value)) {
      return inner_->GetDataHere(format, medium);
    }
    const HRESULT validation = ValidatePrivacyFormat(*format);
    if (FAILED(validation)) {
      return validation;
    }
    if (medium->tymed != TYMED_HGLOBAL || medium->hGlobal == nullptr ||
        GlobalSize(medium->hGlobal) < sizeof(DWORD)) {
      return STG_E_MEDIUMFULL;
    }
    void* destination = GlobalLock(medium->hGlobal);
    if (destination == nullptr) {
      return STG_E_MEDIUMFULL;
    }
    std::memcpy(destination, &value, sizeof(value));
    GlobalUnlock(medium->hGlobal);
    return S_OK;
  }

  HRESULT STDMETHODCALLTYPE QueryGetData(FORMATETC* format) override {
    if (format == nullptr) {
      return E_POINTER;
    }
    DWORD value = 0;
    return IsPrivacyFormat(format->cfFormat, formats_, value)
        ? ValidatePrivacyFormat(*format)
        : inner_->QueryGetData(format);
  }

  HRESULT STDMETHODCALLTYPE GetCanonicalFormatEtc(
      FORMATETC* format_in,
      FORMATETC* format_out) override {
    if (format_in == nullptr || format_out == nullptr) {
      return E_POINTER;
    }
    DWORD value = 0;
    if (!IsPrivacyFormat(format_in->cfFormat, formats_, value)) {
      return inner_->GetCanonicalFormatEtc(format_in, format_out);
    }
    *format_out = *format_in;
    format_out->ptd = nullptr;
    return DATA_S_SAMEFORMATETC;
  }

  HRESULT STDMETHODCALLTYPE SetData(
      FORMATETC* format,
      STGMEDIUM* medium,
      BOOL release) override {
    return inner_->SetData(format, medium, release);
  }

  HRESULT STDMETHODCALLTYPE EnumFormatEtc(
      DWORD direction,
      IEnumFORMATETC** enumerator) override {
    if (enumerator == nullptr) {
      return E_POINTER;
    }
    *enumerator = nullptr;
    if (direction != DATADIR_GET) {
      return inner_->EnumFormatEtc(direction, enumerator);
    }
    IEnumFORMATETC* inner_enumerator = nullptr;
    const HRESULT inner_result =
        inner_->EnumFormatEtc(direction, &inner_enumerator);
    if (FAILED(inner_result) && inner_result != E_NOTIMPL) {
      return inner_result;
    }
    auto* value = new (std::nothrow) PrivacyFormatEnumerator(
        inner_enumerator,
        formats_);
    if (inner_enumerator != nullptr) {
      inner_enumerator->Release();
    }
    if (value == nullptr) {
      return E_OUTOFMEMORY;
    }
    *enumerator = value;
    return S_OK;
  }

  HRESULT STDMETHODCALLTYPE DAdvise(
      FORMATETC* format,
      DWORD flags,
      IAdviseSink* sink,
      DWORD* connection) override {
    return inner_->DAdvise(format, flags, sink, connection);
  }

  HRESULT STDMETHODCALLTYPE DUnadvise(DWORD connection) override {
    return inner_->DUnadvise(connection);
  }

  HRESULT STDMETHODCALLTYPE EnumDAdvise(
      IEnumSTATDATA** enumerator) override {
    return inner_->EnumDAdvise(enumerator);
  }

 private:
  static HRESULT ValidatePrivacyFormat(const FORMATETC& format) noexcept {
    if (format.dwAspect != DVASPECT_CONTENT || format.lindex != -1) {
      return DV_E_DVASPECT;
    }
    if ((format.tymed & TYMED_HGLOBAL) == 0) {
      return DV_E_TYMED;
    }
    return S_OK;
  }

  std::atomic_ulong reference_count_{1};
  IDataObject* inner_{};
  ClipboardPrivacyFormats formats_{};
};

}  // namespace

ClipboardPrivacyFormats RegisterClipboardPrivacyFormats() noexcept {
  return {
      RegisterClipboardFormatW(
          kExcludeClipboardContentFromMonitorProcessing),
      RegisterClipboardFormatW(kCanIncludeInClipboardHistory),
      RegisterClipboardFormatW(kCanUploadToCloudClipboard),
  };
}

bool SetPrivateClipboardUnicodeText(
    std::wstring_view text,
    const ClipboardPrivacyFormats& formats) noexcept {
  if (!formats) {
    return false;
  }

  HGLOBAL exclude = CreateDwordStorage(1);
  HGLOBAL history = CreateDwordStorage(0);
  HGLOBAL cloud = CreateDwordStorage(0);
  HGLOBAL unicode_text = CreateUnicodeTextStorage(text);
  if (exclude == nullptr || history == nullptr || cloud == nullptr ||
      unicode_text == nullptr) {
    FreeIfOwned(exclude);
    FreeIfOwned(history);
    FreeIfOwned(cloud);
    FreeIfOwned(unicode_text);
    return false;
  }

  if (EmptyClipboard() == FALSE) {
    FreeIfOwned(exclude);
    FreeIfOwned(history);
    FreeIfOwned(cloud);
    FreeIfOwned(unicode_text);
    return false;
  }

  auto transfer = [](UINT format, HGLOBAL& storage) noexcept {
    if (SetClipboardData(format, storage) == nullptr) {
      return false;
    }
    storage = nullptr;
    return true;
  };

  // The exclusion marker is transferred first so any partially constructed
  // clipboard item is already private before text is made visible.
  const bool success =
      transfer(formats.exclude_from_monitor_processing, exclude) &&
      transfer(formats.can_include_in_history, history) &&
      transfer(formats.can_upload_to_cloud, cloud) &&
      transfer(CF_UNICODETEXT, unicode_text);
  if (!success) {
    static_cast<void>(EmptyClipboard());
  }
  FreeIfOwned(exclude);
  FreeIfOwned(history);
  FreeIfOwned(cloud);
  FreeIfOwned(unicode_text);
  return success;
}

IDataObject* CreatePrivateClipboardDataObject(
    IDataObject* inner,
    const ClipboardPrivacyFormats& formats) noexcept {
  if (inner == nullptr || !formats) {
    return nullptr;
  }
  return new (std::nothrow) PrivateClipboardDataObject(inner, formats);
}

}  // namespace keyina::windows
