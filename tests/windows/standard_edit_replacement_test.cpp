#include <keyina/windows/standard_edit_replacement.h>

#include "../test_support.h"

#include <string>
#include <utility>

namespace {

struct EditFixture {
  HWND window{};
  HWND edit{};

  EditFixture() = default;
  EditFixture(const EditFixture&) = delete;
  EditFixture& operator=(const EditFixture&) = delete;
  EditFixture(EditFixture&& other) noexcept
      : window(std::exchange(other.window, nullptr)),
        edit(std::exchange(other.edit, nullptr)) {}
  EditFixture& operator=(EditFixture&&) = delete;

  ~EditFixture() {
    if (edit != nullptr) {
      DestroyWindow(edit);
    }
    if (window != nullptr) {
      DestroyWindow(window);
    }
  }
};

EditFixture CreateFocusedEdit() {
  EditFixture fixture;
  fixture.window = CreateWindowExW(
      WS_EX_TOOLWINDOW,
      L"STATIC",
      L"Keyina edit replacement test",
      WS_OVERLAPPEDWINDOW,
      40,
      40,
      500,
      200,
      nullptr,
      nullptr,
      GetModuleHandleW(nullptr),
      nullptr);
  if (fixture.window == nullptr) {
    return fixture;
  }
  fixture.edit = CreateWindowExW(
      0,
      L"EDIT",
      L"",
      WS_CHILD | WS_VISIBLE | ES_MULTILINE | ES_AUTOVSCROLL,
      0,
      0,
      480,
      160,
      fixture.window,
      nullptr,
      GetModuleHandleW(nullptr),
      nullptr);
  if (fixture.edit == nullptr) {
    return fixture;
  }
  SendMessageW(fixture.edit, EM_SETLIMITTEXT, 200000, 0);
  ShowWindow(fixture.window, SW_SHOWNOACTIVATE);
  SetActiveWindow(fixture.window);
  SetFocus(fixture.edit);
  return fixture;
}

}  // namespace

KEYINA_TEST(editable_control_classes_are_not_conflated) {
  using keyina::windows::ClassifyEditableControlClass;
  using keyina::windows::EditableControlKind;

  KEYINA_EXPECT_EQ(
      ClassifyEditableControlClass(L"Edit"),
      EditableControlKind::Win32Edit);
  KEYINA_EXPECT_EQ(
      ClassifyEditableControlClass(L"WindowsForms10.EDIT.app.0.1"),
      EditableControlKind::Win32Edit);
  KEYINA_EXPECT_EQ(
      ClassifyEditableControlClass(L"RICHEDIT50W"),
      EditableControlKind::RichEdit);
  KEYINA_EXPECT_EQ(
      ClassifyEditableControlClass(L"Chrome_RenderWidgetHostHWND"),
      EditableControlKind::Unsupported);
}

KEYINA_TEST(standard_edit_replacement_uses_32_bit_selection_positions) {
  auto fixture = CreateFocusedEdit();
  KEYINA_EXPECT_TRUE(fixture.window != nullptr);
  KEYINA_EXPECT_TRUE(fixture.edit != nullptr);

  std::wstring text(70000, L'x');
  text.append(L"as");
  KEYINA_EXPECT_TRUE(SetWindowTextW(fixture.edit, text.c_str()) != FALSE);
  const DWORD caret = static_cast<DWORD>(text.size());
  SendMessageW(fixture.edit, EM_SETSEL, caret, caret);

  const auto result = keyina::windows::TryReplaceFocusedStandardEdit(
      fixture.edit,
      GetCurrentProcessId(),
      2,
      std::wstring{L"á"},
      50);
  KEYINA_EXPECT_EQ(
      result,
      keyina::windows::StandardEditReplacementResult::Replaced);

  const int length = GetWindowTextLengthW(fixture.edit);
  KEYINA_EXPECT_EQ(length, 70001);
  std::wstring actual(static_cast<std::size_t>(length) + 1, L'\0');
  const int copied = GetWindowTextW(
      fixture.edit,
      actual.data(),
      static_cast<int>(actual.size()));
  KEYINA_EXPECT_EQ(copied, length);
  actual.resize(static_cast<std::size_t>(copied));
  KEYINA_EXPECT_EQ(actual.back(), L'á');
}

KEYINA_TEST(standard_edit_mutation_classification_is_conservative) {
  using keyina::windows::StandardEditReplacementResult;
  using keyina::windows::StandardEditTargetMayBeMutated;

  KEYINA_EXPECT_TRUE(!StandardEditTargetMayBeMutated(
      StandardEditReplacementResult::TargetHung));
  KEYINA_EXPECT_TRUE(!StandardEditTargetMayBeMutated(
      StandardEditReplacementResult::FocusChanged));
  KEYINA_EXPECT_TRUE(StandardEditTargetMayBeMutated(
      StandardEditReplacementResult::TargetHungAfterSelection));
  KEYINA_EXPECT_TRUE(StandardEditTargetMayBeMutated(
      StandardEditReplacementResult::FocusChangedAfterSelection));
  KEYINA_EXPECT_TRUE(StandardEditTargetMayBeMutated(
      StandardEditReplacementResult::ReplaceFailed));
}

KEYINA_TEST(standard_edit_replacement_rejects_stale_focus) {
  auto first = CreateFocusedEdit();
  auto second = CreateFocusedEdit();
  KEYINA_EXPECT_TRUE(first.edit != nullptr);
  KEYINA_EXPECT_TRUE(second.edit != nullptr);
  SetActiveWindow(second.window);
  SetFocus(second.edit);

  const auto result = keyina::windows::TryReplaceFocusedStandardEdit(
      first.edit,
      GetCurrentProcessId(),
      1,
      std::wstring{L"x"},
      10);
  KEYINA_EXPECT_EQ(
      result,
      keyina::windows::StandardEditReplacementResult::FocusChanged);
}
