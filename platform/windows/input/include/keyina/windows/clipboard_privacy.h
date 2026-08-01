#pragma once

#include <windows.h>
#include <ole2.h>

#include <string_view>

namespace keyina::windows {

struct ClipboardPrivacyFormats {
  UINT exclude_from_monitor_processing{};
  UINT can_include_in_history{};
  UINT can_upload_to_cloud{};

  [[nodiscard]] explicit operator bool() const noexcept {
    return exclude_from_monitor_processing != 0 &&
        can_include_in_history != 0 &&
        can_upload_to_cloud != 0;
  }
};

[[nodiscard]] ClipboardPrivacyFormats RegisterClipboardPrivacyFormats()
    noexcept;

// The caller must own an open clipboard. On success the clipboard contains
// Unicode text plus Windows-recognized privacy formats that exclude the item
// from Clipboard History and cloud synchronization.
[[nodiscard]] bool SetPrivateClipboardUnicodeText(
    std::wstring_view text,
    const ClipboardPrivacyFormats& formats) noexcept;

// Returns an IDataObject wrapper with one caller-owned reference. The wrapper
// delegates the original formats and adds the privacy formats above.
[[nodiscard]] IDataObject* CreatePrivateClipboardDataObject(
    IDataObject* inner,
    const ClipboardPrivacyFormats& formats) noexcept;

}  // namespace keyina::windows
