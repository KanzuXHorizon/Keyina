#pragma once

#include <cstddef>
#include <string>
#include <string_view>

namespace keyina {

enum class KeyKind {
  Character,
  Backspace,
  CommitBoundary,
  Reset,
};

struct KeyEvent {
  KeyKind kind;
  char32_t character{};
  bool shift{false};
  bool control{false};
  bool alt{false};
};

inline constexpr std::size_t kMaxActiveKeys = 64;

struct TextEdit {
  std::size_t erase_codepoints{};
  std::u32string insert;
  bool consumed{false};
  bool commit_before{false};

  friend bool operator==(const TextEdit&, const TextEdit&) = default;
};

struct TextEditView {
  std::size_t erase_codepoints{};
  std::u32string_view insert;
  bool consumed{false};
  bool commit_before{false};
};

enum class TonePlacement {
  Modern,
  Traditional,
};

struct EngineConfig {
  TonePlacement tone_placement{TonePlacement::Modern};
  bool application_bypass{false};
  bool restore_invalid_word{false};
  bool quick_telex_letters{false};
};

class Engine {
 public:
  explicit Engine(EngineConfig config = {});

  [[nodiscard]] TextEdit Process(const KeyEvent& event);

  // The insert view remains valid until the next Process, ProcessView, Reset,
  // assignment, or destruction of this engine instance.
  [[nodiscard]] TextEditView ProcessView(const KeyEvent& event);
  void Reset() noexcept;

  [[nodiscard]] std::u32string_view VisibleText() const noexcept;
  [[nodiscard]] std::u32string_view RawKeys() const noexcept;

 private:
  void ComposeRaw(std::u32string& destination);
  void BuildVisibleForRaw();
  [[nodiscard]] TextEditView ReplaceVisibleView(bool consumed);
  void ResetCompositionState() noexcept;

  EngineConfig config_;
  std::u32string raw_keys_;
  std::u32string visible_text_;
  std::u32string composition_buffer_;
  std::u32string previous_key_buffer_;
  std::u32string edit_buffer_;
  std::u32string literal_text_buffer_;
};

}  // namespace keyina
