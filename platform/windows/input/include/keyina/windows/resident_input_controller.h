#pragma once

#include <keyina/engine.h>
#include <keyina/windows/runtime_profile.h>
#include <keyina/windows/runtime_snippet_matcher.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

namespace keyina::windows {

inline constexpr std::size_t kMaximumInputInsertUnits = 128;

struct PhysicalKeyEvent {
  std::uint16_t virtual_key{};
  char32_t character{};
  bool key_down{false};
  bool injected_by_keyina{false};
  bool shift{false};
  bool control{false};
  bool alt{false};
  bool windows{false};
};

struct TypingContext {
  std::uint32_t foreground_process_id{};
  std::uintptr_t focus_window{};
  bool bypass_typing{true};
  std::uint64_t application_hash{};

  friend bool operator==(const TypingContext&, const TypingContext&) = default;
};

struct InputDecision {
  bool suppress{false};
  std::uint16_t backspace_count{};
  std::uint16_t insert_units{};
  std::array<wchar_t, kMaximumInputInsertUnits> insert{};
  std::u16string_view extended_insert{};
  std::u16string_view snippet_command_payload{};
  RuntimeSnippetCommand snippet_command{RuntimeSnippetCommand::None};
  std::uint32_t snippet_target_process_id{};
  std::uintptr_t snippet_target_focus_window{};
};

class ResidentInputController {
 public:
  explicit ResidentInputController(
      RuntimeInputProfile profile = {},
      RuntimeSnippetProfile snippets = DefaultRuntimeSnippetProfile());

  void ApplyProfile(const RuntimeInputProfile& profile);
  void ApplySnippets(RuntimeSnippetProfile snippets);
  [[nodiscard]] InputDecision Process(const PhysicalKeyEvent& event,
                                      const TypingContext& context) noexcept;
  void OnPointerReset() noexcept;
  void Reset() noexcept;

  [[nodiscard]] bool enabled() const noexcept {
    return profile_.vietnamese_enabled;
  }

  [[nodiscard]] bool pointer_observation_required() const noexcept {
    return pointer_observation_required_;
  }

  [[nodiscard]] const RuntimeInputProfile& profile() const noexcept {
    return profile_;
  }

  [[nodiscard]] std::u32string_view snippet_token() const noexcept {
    return snippet_matcher_.token();
  }

  [[nodiscard]] std::vector<const RuntimeSnippetDefinition*>
  snippet_suggestions(std::size_t maximum) const {
    return snippet_matcher_.Suggestions(maximum);
  }

 private:
  class KeyStateSet {
   public:
    [[nodiscard]] bool Get(std::uint16_t virtual_key) const noexcept;
    void Set(std::uint16_t virtual_key, bool value) noexcept;
    void Clear() noexcept;

   private:
    std::array<std::uint64_t, 4> segments_{};
  };

  [[nodiscard]] InputDecision ProcessKeyDown(
      const PhysicalKeyEvent& event,
      const TypingContext& context);
  [[nodiscard]] InputDecision BuildDecision(
      const TextEditView& edit,
      char32_t physical_character) noexcept;
  [[nodiscard]] InputDecision BuildSnippetDecision(
      const RuntimeSnippetMatch& match,
      char32_t delimiter);
  void RememberCommittedComposition();
  void RestoreCommittedCompositionAfterBoundaryBackspace();
  void ClearCommittedComposition() noexcept;
  void ResetEngineState() noexcept;

  RuntimeInputProfile profile_{};
  Engine engine_{};
  RuntimeSnippetProfile snippet_profile_{};
  RuntimeSnippetMatcher snippet_matcher_;
  std::u16string snippet_insert_buffer_;
  std::u32string committed_raw_keys_;
  std::u32string committed_visible_text_;
  TypingContext context_{};
  KeyStateSet suppressed_keys_{};
  bool has_context_{false};
  bool pointer_observation_required_{false};
  bool boundary_backspace_recovery_available_{false};
};

}  // namespace keyina::windows
