#include "test_text_store.h"

#include <algorithm>
#include <cstddef>
#include <limits>

namespace {

constexpr HRESULT kConnectNoConnection =
    static_cast<HRESULT>(0x80040200L);
constexpr HRESULT kConnectAdviseLimit =
    static_cast<HRESULT>(0x80040201L);
constexpr LONG kAcpEnd = -1;

}  // namespace

TestTextStore::TestTextStore() noexcept {
  selection_.acpStart = 0;
  selection_.acpEnd = 0;
  selection_.style.ase = TS_AE_END;
  selection_.style.fInterimChar = FALSE;
}

TestTextStore::~TestTextStore() {
  if (sink_ != nullptr) {
    sink_->Release();
  }
}

std::wstring_view TestTextStore::Text() const noexcept { return text_; }

LONG TestTextStore::Caret() const noexcept { return selection_.acpEnd; }

void TestTextStore::SelectForTest(LONG start, LONG end) noexcept {
  selection_.acpStart = start;
  selection_.acpEnd = end;
  selection_.style.ase = TS_AE_END;
  selection_.style.fInterimChar = FALSE;
  NotifySelectionChange();
}

HRESULT TestTextStore::QueryInterface(REFIID interface_id, void** object) {
  if (object == nullptr) {
    return E_POINTER;
  }
  *object = nullptr;
  if (!IsEqualIID(interface_id, IID_IUnknown) &&
      !IsEqualIID(interface_id, IID_ITextStoreACP)) {
    return E_NOINTERFACE;
  }
  *object = static_cast<ITextStoreACP*>(this);
  AddRef();
  return S_OK;
}

ULONG TestTextStore::AddRef() { return ++reference_count_; }

ULONG TestTextStore::Release() {
  const ULONG remaining = --reference_count_;
  if (remaining == 0) {
    delete this;
  }
  return remaining;
}

HRESULT TestTextStore::AdviseSink(REFIID interface_id, IUnknown* sink,
                                  DWORD mask) {
  if (!IsEqualIID(interface_id, IID_ITextStoreACPSink) || sink == nullptr) {
    return E_INVALIDARG;
  }

  ITextStoreACPSink* candidate = nullptr;
  HRESULT result = sink->QueryInterface(
      IID_ITextStoreACPSink, reinterpret_cast<void**>(&candidate));
  if (FAILED(result)) {
    return result;
  }

  if (sink_ != nullptr) {
    if (candidate == sink_) {
      sink_mask_ = mask;
      candidate->Release();
      return S_OK;
    }
    candidate->Release();
    return kConnectAdviseLimit;
  }

  sink_ = candidate;
  sink_mask_ = mask;
  return S_OK;
}

HRESULT TestTextStore::UnadviseSink(IUnknown* sink) {
  if (sink_ == nullptr || sink == nullptr) {
    return kConnectNoConnection;
  }

  ITextStoreACPSink* candidate = nullptr;
  const HRESULT result = sink->QueryInterface(
      IID_ITextStoreACPSink, reinterpret_cast<void**>(&candidate));
  if (FAILED(result) || candidate != sink_) {
    if (candidate != nullptr) {
      candidate->Release();
    }
    return kConnectNoConnection;
  }

  candidate->Release();
  sink_->Release();
  sink_ = nullptr;
  sink_mask_ = 0;
  return S_OK;
}

HRESULT TestTextStore::RequestLock(DWORD flags, HRESULT* session_result) {
  if (session_result == nullptr) {
    return E_POINTER;
  }
  if (sink_ == nullptr) {
    *session_result = E_UNEXPECTED;
    return E_UNEXPECTED;
  }
  if (lock_flags_ != 0) {
    *session_result = TS_E_SYNCHRONOUS;
    return S_OK;
  }

  lock_flags_ = flags;
  *session_result = sink_->OnLockGranted(flags);
  lock_flags_ = 0;
  return S_OK;
}

HRESULT TestTextStore::GetStatus(TS_STATUS* status) {
  if (status == nullptr) {
    return E_POINTER;
  }
  status->dwDynamicFlags = 0;
  status->dwStaticFlags = TS_SS_NOHIDDENTEXT;
  return S_OK;
}

HRESULT TestTextStore::QueryInsert(LONG test_start, LONG test_end,
                                   ULONG character_count, LONG* result_start,
                                   LONG* result_end) {
  if (result_start == nullptr || result_end == nullptr) {
    return E_POINTER;
  }
  test_end = NormalizeEnd(test_end);
  if (!IsValidRange(test_start, test_end) ||
      character_count >
          static_cast<ULONG>(std::numeric_limits<LONG>::max())) {
    return TS_E_INVALIDPOS;
  }
  *result_start = test_start;
  *result_end = test_start + static_cast<LONG>(character_count);
  return S_OK;
}

HRESULT TestTextStore::GetSelection(ULONG index, ULONG count,
                                    TS_SELECTION_ACP* selection,
                                    ULONG* fetched) {
  if (!HasReadLock()) {
    return TS_E_NOLOCK;
  }
  if (fetched == nullptr || (count != 0 && selection == nullptr)) {
    return E_POINTER;
  }
  *fetched = 0;
  if (count == 0) {
    return S_OK;
  }
  if (index != TS_DEFAULT_SELECTION && index != 0) {
    return TS_E_NOSELECTION;
  }
  selection[0] = selection_;
  *fetched = 1;
  return S_OK;
}

HRESULT TestTextStore::SetSelection(
    ULONG count, const TS_SELECTION_ACP* selection) {
  if (!HasWriteLock()) {
    return TS_E_NOLOCK;
  }
  if (count != 1 || selection == nullptr) {
    return E_INVALIDARG;
  }
  if (!IsValidRange(selection[0].acpStart, selection[0].acpEnd)) {
    return TS_E_INVALIDPOS;
  }
  selection_ = selection[0];
  NotifySelectionChange();
  return S_OK;
}

HRESULT TestTextStore::GetText(LONG start, LONG end, WCHAR* plain,
                               ULONG plain_capacity, ULONG* plain_count,
                               TS_RUNINFO* run_info, ULONG run_capacity,
                               ULONG* run_count, LONG* next) {
  if (!HasReadLock()) {
    return TS_E_NOLOCK;
  }
  if (plain_count == nullptr || run_count == nullptr || next == nullptr ||
      (plain_capacity != 0 && plain == nullptr) ||
      (run_capacity != 0 && run_info == nullptr)) {
    return E_POINTER;
  }

  end = NormalizeEnd(end);
  if (!IsValidRange(start, end)) {
    return TS_E_INVALIDPOS;
  }

  const ULONG available = static_cast<ULONG>(end - start);
  const ULONG copied = std::min(available, plain_capacity);
  if (copied != 0) {
    std::copy_n(text_.data() + start, copied, plain);
  }
  *plain_count = copied;
  *next = start + static_cast<LONG>(copied);

  *run_count = 0;
  if (run_capacity != 0 && copied != 0) {
    run_info[0].uCount = copied;
    run_info[0].type = TS_RT_PLAIN;
    *run_count = 1;
  }
  return S_OK;
}

HRESULT TestTextStore::SetText(DWORD flags, LONG start, LONG end,
                               const WCHAR* text, ULONG character_count,
                               TS_TEXTCHANGE* change) {
  static_cast<void>(flags);
  if (!HasWriteLock()) {
    return TS_E_NOLOCK;
  }
  if (change == nullptr || (character_count != 0 && text == nullptr) ||
      character_count >
          static_cast<ULONG>(std::numeric_limits<LONG>::max())) {
    return E_INVALIDARG;
  }

  end = NormalizeEnd(end);
  if (!IsValidRange(start, end)) {
    return TS_E_INVALIDPOS;
  }

  change->acpStart = start;
  change->acpOldEnd = end;
  change->acpNewEnd = start + static_cast<LONG>(character_count);
  text_.replace(static_cast<std::size_t>(start),
                static_cast<std::size_t>(end - start),
                text == nullptr ? L"" : text,
                static_cast<std::size_t>(character_count));

  selection_.acpStart = change->acpNewEnd;
  selection_.acpEnd = change->acpNewEnd;
  selection_.style.ase = TS_AE_END;
  selection_.style.fInterimChar = FALSE;

  if (sink_ != nullptr && (sink_mask_ & TS_AS_TEXT_CHANGE) != 0) {
    static_cast<void>(sink_->OnTextChange(0, change));
  }
  NotifySelectionChange();
  return S_OK;
}

HRESULT TestTextStore::GetFormattedText(LONG start, LONG end,
                                        IDataObject** data) {
  static_cast<void>(start);
  static_cast<void>(end);
  if (data == nullptr) {
    return E_POINTER;
  }
  *data = nullptr;
  return E_NOTIMPL;
}

HRESULT TestTextStore::GetEmbedded(LONG position, REFGUID service,
                                   REFIID interface_id, IUnknown** object) {
  static_cast<void>(position);
  static_cast<void>(service);
  static_cast<void>(interface_id);
  if (object == nullptr) {
    return E_POINTER;
  }
  *object = nullptr;
  return TS_E_FORMAT;
}

HRESULT TestTextStore::QueryInsertEmbedded(const GUID* service,
                                           const FORMATETC* format,
                                           BOOL* insertable) {
  static_cast<void>(service);
  static_cast<void>(format);
  if (insertable == nullptr) {
    return E_POINTER;
  }
  *insertable = FALSE;
  return S_OK;
}

HRESULT TestTextStore::InsertEmbedded(DWORD flags, LONG start, LONG end,
                                      IDataObject* data,
                                      TS_TEXTCHANGE* change) {
  static_cast<void>(flags);
  static_cast<void>(start);
  static_cast<void>(end);
  static_cast<void>(data);
  static_cast<void>(change);
  return TS_E_FORMAT;
}

HRESULT TestTextStore::InsertTextAtSelection(
    DWORD flags, const WCHAR* text, ULONG character_count, LONG* start,
    LONG* end, TS_TEXTCHANGE* change) {
  if (!HasWriteLock()) {
    return TS_E_NOLOCK;
  }
  if (start == nullptr || end == nullptr || change == nullptr ||
      (character_count != 0 && text == nullptr)) {
    return E_POINTER;
  }

  *start = selection_.acpStart;
  *end = selection_.acpStart + static_cast<LONG>(character_count);
  if ((flags & TS_IAS_QUERYONLY) != 0) {
    change->acpStart = selection_.acpStart;
    change->acpOldEnd = selection_.acpEnd;
    change->acpNewEnd = *end;
    return S_OK;
  }
  return SetText(flags, selection_.acpStart, selection_.acpEnd, text,
                 character_count, change);
}

HRESULT TestTextStore::InsertEmbeddedAtSelection(
    DWORD flags, IDataObject* data, LONG* start, LONG* end,
    TS_TEXTCHANGE* change) {
  static_cast<void>(flags);
  static_cast<void>(data);
  static_cast<void>(start);
  static_cast<void>(end);
  static_cast<void>(change);
  return TS_E_FORMAT;
}

HRESULT TestTextStore::RequestSupportedAttrs(
    DWORD flags, ULONG count, const TS_ATTRID* attributes) {
  static_cast<void>(flags);
  static_cast<void>(count);
  static_cast<void>(attributes);
  return S_OK;
}

HRESULT TestTextStore::RequestAttrsAtPosition(
    LONG position, ULONG count, const TS_ATTRID* attributes, DWORD flags) {
  static_cast<void>(position);
  static_cast<void>(count);
  static_cast<void>(attributes);
  static_cast<void>(flags);
  return S_OK;
}

HRESULT TestTextStore::RequestAttrsTransitioningAtPosition(
    LONG position, ULONG count, const TS_ATTRID* attributes, DWORD flags) {
  static_cast<void>(position);
  static_cast<void>(count);
  static_cast<void>(attributes);
  static_cast<void>(flags);
  return S_OK;
}

HRESULT TestTextStore::FindNextAttrTransition(
    LONG start, LONG halt, ULONG count, const TS_ATTRID* attributes,
    DWORD flags, LONG* next, BOOL* found, LONG* found_offset) {
  static_cast<void>(count);
  static_cast<void>(attributes);
  static_cast<void>(flags);
  if (next == nullptr || found == nullptr || found_offset == nullptr) {
    return E_POINTER;
  }
  *next = halt;
  *found = FALSE;
  *found_offset = halt - start;
  return S_OK;
}

HRESULT TestTextStore::RetrieveRequestedAttrs(ULONG count,
                                              TS_ATTRVAL* values,
                                              ULONG* fetched) {
  static_cast<void>(count);
  static_cast<void>(values);
  if (fetched == nullptr) {
    return E_POINTER;
  }
  *fetched = 0;
  return S_OK;
}

HRESULT TestTextStore::GetEndACP(LONG* end) {
  if (!HasReadLock()) {
    return TS_E_NOLOCK;
  }
  if (end == nullptr) {
    return E_POINTER;
  }
  *end = static_cast<LONG>(text_.size());
  return S_OK;
}

HRESULT TestTextStore::GetActiveView(TsViewCookie* view) {
  if (view == nullptr) {
    return E_POINTER;
  }
  *view = 1;
  return S_OK;
}

HRESULT TestTextStore::GetACPFromPoint(TsViewCookie view, const POINT* point,
                                       DWORD flags, LONG* position) {
  static_cast<void>(view);
  static_cast<void>(point);
  static_cast<void>(flags);
  static_cast<void>(position);
  return TS_E_NOLAYOUT;
}

HRESULT TestTextStore::GetTextExt(TsViewCookie view, LONG start, LONG end,
                                  RECT* rectangle, BOOL* clipped) {
  static_cast<void>(view);
  static_cast<void>(start);
  static_cast<void>(end);
  static_cast<void>(rectangle);
  static_cast<void>(clipped);
  return TS_E_NOLAYOUT;
}

HRESULT TestTextStore::GetScreenExt(TsViewCookie view, RECT* rectangle) {
  static_cast<void>(view);
  if (rectangle == nullptr) {
    return E_POINTER;
  }
  SetRectEmpty(rectangle);
  return S_OK;
}

HRESULT TestTextStore::GetWnd(TsViewCookie view, HWND* window) {
  static_cast<void>(view);
  if (window == nullptr) {
    return E_POINTER;
  }
  *window = nullptr;
  return S_OK;
}

bool TestTextStore::HasReadLock() const noexcept {
  return (lock_flags_ & TS_LF_READ) != 0 ||
         (lock_flags_ & TS_LF_READWRITE) == TS_LF_READWRITE;
}

bool TestTextStore::HasWriteLock() const noexcept {
  return (lock_flags_ & TS_LF_READWRITE) == TS_LF_READWRITE;
}

bool TestTextStore::IsValidRange(LONG start, LONG end) const noexcept {
  return start >= 0 && end >= start &&
         static_cast<std::size_t>(end) <= text_.size();
}

LONG TestTextStore::NormalizeEnd(LONG end) const noexcept {
  return end == kAcpEnd ? static_cast<LONG>(text_.size()) : end;
}

void TestTextStore::NotifySelectionChange() noexcept {
  if (sink_ != nullptr && (sink_mask_ & TS_AS_SEL_CHANGE) != 0) {
    static_cast<void>(sink_->OnSelectionChange());
  }
}
