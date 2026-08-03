#include <keyina/windows/clipboard_privacy.h>

#include "../test_support.h"

#include <array>
#include <cstring>
#include <string_view>

namespace {

bool OpenClipboardForTest() noexcept {
  for (int attempt = 0; attempt < 20; ++attempt) {
    if (OpenClipboard(nullptr) != FALSE) {
      return true;
    }
    Sleep(5);
  }
  return false;
}

bool GetOleClipboardForTest(IDataObject** data_object) noexcept {
  if (data_object == nullptr) {
    return false;
  }
  *data_object = nullptr;
  for (int attempt = 0; attempt < 20; ++attempt) {
    const HRESULT result = OleGetClipboard(data_object);
    if (SUCCEEDED(result)) {
      return true;
    }
    if (*data_object != nullptr) {
      (*data_object)->Release();
      *data_object = nullptr;
    }
    Sleep(5);
  }
  return false;
}

class ClipboardRestoreGuard {
 public:
  explicit ClipboardRestoreGuard(
      keyina::windows::ClipboardPrivacyFormats formats) noexcept
      : formats_(formats) {
    const HRESULT initialized = OleInitialize(nullptr);
    ole_initialized_ = SUCCEEDED(initialized);
    if (ole_initialized_) {
      ready_ = GetOleClipboardForTest(&previous_);
    }
  }

  [[nodiscard]] bool ready() const noexcept { return ready_; }
  void MarkMutated() noexcept { mutated_ = true; }

  ~ClipboardRestoreGuard() {
    if (!mutated_) {
      if (previous_ != nullptr) {
        previous_->Release();
      }
      if (ole_initialized_) {
        OleUninitialize();
      }
      return;
    }
    if (previous_ != nullptr) {
      IDataObject* private_previous =
          keyina::windows::CreatePrivateClipboardDataObject(
              previous_, formats_);
      IDataObject* restore =
          private_previous == nullptr ? previous_ : private_previous;
      if (SUCCEEDED(OleSetClipboard(restore))) {
        static_cast<void>(OleFlushClipboard());
      }
      if (private_previous != nullptr) {
        private_previous->Release();
      }
      previous_->Release();
    } else if (OpenClipboardForTest()) {
      static_cast<void>(EmptyClipboard());
      CloseClipboard();
    }
    if (ole_initialized_) {
      OleUninitialize();
    }
  }

 private:
  keyina::windows::ClipboardPrivacyFormats formats_{};
  IDataObject* previous_{};
  bool ole_initialized_{false};
  bool ready_{false};
  bool mutated_{false};
};

class OpenedClipboardGuard {
 public:
  explicit OpenedClipboardGuard(bool open) noexcept : open_(open) {}
  ~OpenedClipboardGuard() {
    if (open_) {
      CloseClipboard();
    }
  }
  void Close() noexcept {
    if (open_) {
      CloseClipboard();
      open_ = false;
    }
  }

 private:
  bool open_{false};
};

DWORD ReadClipboardDword(UINT format) {
  const HANDLE handle = GetClipboardData(format);
  KEYINA_EXPECT_TRUE(handle != nullptr);
  const void* value = GlobalLock(handle);
  KEYINA_EXPECT_TRUE(value != nullptr);
  DWORD result = 0;
  std::memcpy(&result, value, sizeof(result));
  GlobalUnlock(handle);
  return result;
}

}  // namespace

KEYINA_TEST(clipboard_privacy_formats_are_registered_and_distinct) {
  const auto formats =
      keyina::windows::RegisterClipboardPrivacyFormats();
  KEYINA_EXPECT_TRUE(static_cast<bool>(formats));
  KEYINA_EXPECT_TRUE(
      formats.exclude_from_monitor_processing !=
      formats.can_include_in_history);
  KEYINA_EXPECT_TRUE(
      formats.exclude_from_monitor_processing !=
      formats.can_upload_to_cloud);
  KEYINA_EXPECT_TRUE(
      formats.can_include_in_history != formats.can_upload_to_cloud);
}

KEYINA_TEST(private_clipboard_text_excludes_history_and_cloud_sync) {
  const auto formats =
      keyina::windows::RegisterClipboardPrivacyFormats();
  KEYINA_EXPECT_TRUE(static_cast<bool>(formats));
  ClipboardRestoreGuard restore(formats);
  KEYINA_EXPECT_TRUE(restore.ready());
  const bool opened = OpenClipboardForTest();
  KEYINA_EXPECT_TRUE(opened);
  OpenedClipboardGuard open_guard(opened);

  const bool written = keyina::windows::SetPrivateClipboardUnicodeText(
      L"Keyina clipboard privacy probe",
      formats);
  if (written) {
    restore.MarkMutated();
  }
  KEYINA_EXPECT_TRUE(written);
  KEYINA_EXPECT_TRUE(
      IsClipboardFormatAvailable(
          formats.exclude_from_monitor_processing) != FALSE);
  KEYINA_EXPECT_TRUE(
      IsClipboardFormatAvailable(formats.can_include_in_history) != FALSE);
  KEYINA_EXPECT_TRUE(
      IsClipboardFormatAvailable(formats.can_upload_to_cloud) != FALSE);
  KEYINA_EXPECT_EQ(
      ReadClipboardDword(formats.exclude_from_monitor_processing),
      static_cast<DWORD>(1));
  KEYINA_EXPECT_EQ(
      ReadClipboardDword(formats.can_include_in_history),
      static_cast<DWORD>(0));
  KEYINA_EXPECT_EQ(
      ReadClipboardDword(formats.can_upload_to_cloud),
      static_cast<DWORD>(0));

  const HANDLE text_handle = GetClipboardData(CF_UNICODETEXT);
  KEYINA_EXPECT_TRUE(text_handle != nullptr);
  const auto* text = static_cast<const wchar_t*>(GlobalLock(text_handle));
  KEYINA_EXPECT_TRUE(text != nullptr);
  KEYINA_EXPECT_EQ(
      std::wstring_view(text),
      std::wstring_view(L"Keyina clipboard privacy probe"));
  GlobalUnlock(text_handle);
  open_guard.Close();
}

KEYINA_TEST(private_clipboard_supports_an_empty_restorable_text_value) {
  const auto formats =
      keyina::windows::RegisterClipboardPrivacyFormats();
  KEYINA_EXPECT_TRUE(static_cast<bool>(formats));
  ClipboardRestoreGuard restore(formats);
  KEYINA_EXPECT_TRUE(restore.ready());
  const bool opened = OpenClipboardForTest();
  KEYINA_EXPECT_TRUE(opened);
  OpenedClipboardGuard open_guard(opened);

  const bool written =
      keyina::windows::SetPrivateClipboardUnicodeText(L"", formats);
  if (written) {
    restore.MarkMutated();
  }
  KEYINA_EXPECT_TRUE(written);
  const HANDLE text_handle = GetClipboardData(CF_UNICODETEXT);
  KEYINA_EXPECT_TRUE(text_handle != nullptr);
  const auto* text = static_cast<const wchar_t*>(GlobalLock(text_handle));
  KEYINA_EXPECT_TRUE(text != nullptr);
  KEYINA_EXPECT_EQ(std::wstring_view(text), std::wstring_view{});
  GlobalUnlock(text_handle);
  open_guard.Close();
}

KEYINA_TEST(private_clipboard_data_object_advertises_privacy_formats) {
  const auto formats =
      keyina::windows::RegisterClipboardPrivacyFormats();
  KEYINA_EXPECT_TRUE(static_cast<bool>(formats));
  ClipboardRestoreGuard restore(formats);
  KEYINA_EXPECT_TRUE(restore.ready());
  const bool opened = OpenClipboardForTest();
  KEYINA_EXPECT_TRUE(opened);
  OpenedClipboardGuard open_guard(opened);
  const bool written = keyina::windows::SetPrivateClipboardUnicodeText(
      L"Keyina wrapper probe",
      formats);
  if (written) {
    restore.MarkMutated();
  }
  KEYINA_EXPECT_TRUE(written);
  open_guard.Close();

  IDataObject* inner = nullptr;
  KEYINA_EXPECT_TRUE(GetOleClipboardForTest(&inner) && inner != nullptr);
  IDataObject* wrapper =
      keyina::windows::CreatePrivateClipboardDataObject(inner, formats);
  inner->Release();
  KEYINA_EXPECT_TRUE(wrapper != nullptr);

  FORMATETC query{};
  query.cfFormat = static_cast<CLIPFORMAT>(
      formats.exclude_from_monitor_processing);
  query.dwAspect = DVASPECT_CONTENT;
  query.lindex = -1;
  query.tymed = TYMED_HGLOBAL;
  KEYINA_EXPECT_EQ(wrapper->QueryGetData(&query), S_OK);

  STGMEDIUM medium{};
  KEYINA_EXPECT_EQ(wrapper->GetData(&query, &medium), S_OK);
  KEYINA_EXPECT_EQ(medium.tymed, static_cast<DWORD>(TYMED_HGLOBAL));
  const void* value = GlobalLock(medium.hGlobal);
  KEYINA_EXPECT_TRUE(value != nullptr);
  DWORD marker = 0;
  std::memcpy(&marker, value, sizeof(marker));
  GlobalUnlock(medium.hGlobal);
  ReleaseStgMedium(&medium);
  KEYINA_EXPECT_EQ(marker, static_cast<DWORD>(1));
  wrapper->Release();
}
