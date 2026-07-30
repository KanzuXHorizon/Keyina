#pragma once

#include <windows.h>

#include <textstor.h>

#include <atomic>
#include <string>
#include <string_view>

class TestTextStore final : public ITextStoreACP {
 public:
  TestTextStore() noexcept;

  TestTextStore(const TestTextStore&) = delete;
  TestTextStore& operator=(const TestTextStore&) = delete;

  [[nodiscard]] std::wstring_view Text() const noexcept;
  [[nodiscard]] LONG Caret() const noexcept;
  void SelectForTest(LONG start, LONG end) noexcept;

  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID interface_id,
                                           void** object) override;
  ULONG STDMETHODCALLTYPE AddRef() override;
  ULONG STDMETHODCALLTYPE Release() override;

  HRESULT STDMETHODCALLTYPE AdviseSink(REFIID interface_id, IUnknown* sink,
                                       DWORD mask) override;
  HRESULT STDMETHODCALLTYPE UnadviseSink(IUnknown* sink) override;
  HRESULT STDMETHODCALLTYPE RequestLock(DWORD flags,
                                        HRESULT* session_result) override;
  HRESULT STDMETHODCALLTYPE GetStatus(TS_STATUS* status) override;
  HRESULT STDMETHODCALLTYPE QueryInsert(LONG test_start, LONG test_end,
                                        ULONG character_count,
                                        LONG* result_start,
                                        LONG* result_end) override;
  HRESULT STDMETHODCALLTYPE GetSelection(ULONG index, ULONG count,
                                         TS_SELECTION_ACP* selection,
                                         ULONG* fetched) override;
  HRESULT STDMETHODCALLTYPE SetSelection(
      ULONG count, const TS_SELECTION_ACP* selection) override;
  HRESULT STDMETHODCALLTYPE GetText(LONG start, LONG end, WCHAR* plain,
                                    ULONG plain_capacity,
                                    ULONG* plain_count, TS_RUNINFO* run_info,
                                    ULONG run_capacity, ULONG* run_count,
                                    LONG* next) override;
  HRESULT STDMETHODCALLTYPE SetText(DWORD flags, LONG start, LONG end,
                                    const WCHAR* text, ULONG character_count,
                                    TS_TEXTCHANGE* change) override;
  HRESULT STDMETHODCALLTYPE GetFormattedText(LONG start, LONG end,
                                             IDataObject** data) override;
  HRESULT STDMETHODCALLTYPE GetEmbedded(LONG position, REFGUID service,
                                        REFIID interface_id,
                                        IUnknown** object) override;
  HRESULT STDMETHODCALLTYPE QueryInsertEmbedded(
      const GUID* service, const FORMATETC* format,
      BOOL* insertable) override;
  HRESULT STDMETHODCALLTYPE InsertEmbedded(DWORD flags, LONG start, LONG end,
                                           IDataObject* data,
                                           TS_TEXTCHANGE* change) override;
  HRESULT STDMETHODCALLTYPE InsertTextAtSelection(
      DWORD flags, const WCHAR* text, ULONG character_count, LONG* start,
      LONG* end, TS_TEXTCHANGE* change) override;
  HRESULT STDMETHODCALLTYPE InsertEmbeddedAtSelection(
      DWORD flags, IDataObject* data, LONG* start, LONG* end,
      TS_TEXTCHANGE* change) override;
  HRESULT STDMETHODCALLTYPE RequestSupportedAttrs(
      DWORD flags, ULONG count, const TS_ATTRID* attributes) override;
  HRESULT STDMETHODCALLTYPE RequestAttrsAtPosition(
      LONG position, ULONG count, const TS_ATTRID* attributes,
      DWORD flags) override;
  HRESULT STDMETHODCALLTYPE RequestAttrsTransitioningAtPosition(
      LONG position, ULONG count, const TS_ATTRID* attributes,
      DWORD flags) override;
  HRESULT STDMETHODCALLTYPE FindNextAttrTransition(
      LONG start, LONG halt, ULONG count, const TS_ATTRID* attributes,
      DWORD flags, LONG* next, BOOL* found, LONG* found_offset) override;
  HRESULT STDMETHODCALLTYPE RetrieveRequestedAttrs(
      ULONG count, TS_ATTRVAL* values, ULONG* fetched) override;
  HRESULT STDMETHODCALLTYPE GetEndACP(LONG* end) override;
  HRESULT STDMETHODCALLTYPE GetActiveView(TsViewCookie* view) override;
  HRESULT STDMETHODCALLTYPE GetACPFromPoint(TsViewCookie view,
                                            const POINT* point, DWORD flags,
                                            LONG* position) override;
  HRESULT STDMETHODCALLTYPE GetTextExt(TsViewCookie view, LONG start, LONG end,
                                       RECT* rectangle,
                                       BOOL* clipped) override;
  HRESULT STDMETHODCALLTYPE GetScreenExt(TsViewCookie view,
                                         RECT* rectangle) override;
  HRESULT STDMETHODCALLTYPE GetWnd(TsViewCookie view, HWND* window) override;

 private:
  ~TestTextStore();

  [[nodiscard]] bool HasReadLock() const noexcept;
  [[nodiscard]] bool HasWriteLock() const noexcept;
  [[nodiscard]] bool IsValidRange(LONG start, LONG end) const noexcept;
  [[nodiscard]] LONG NormalizeEnd(LONG end) const noexcept;
  void NotifySelectionChange() noexcept;

  std::atomic<ULONG> reference_count_{1};
  ITextStoreACPSink* sink_{nullptr};
  DWORD sink_mask_{0};
  DWORD lock_flags_{0};
  std::wstring text_;
  TS_SELECTION_ACP selection_{};
};
