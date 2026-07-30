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

enum class TonePlacement {
  Modern,
  Traditional,
};

struct EngineConfig {
  TonePlacement tone_placement{TonePlacement::Modern};
  bool application_bypass{false};
  bool restore_invalid_word{false};
};

class Engine {
 public:
  explicit Engine(EngineConfig config = {});

  [[nodiscard]] TextEdit Process(const KeyEvent& event);
  void Reset() noexcept;

  [[nodiscard]] std::u32string_view VisibleText() const noexcept;
  [[nodiscard]] std::u32string_view RawKeys() const noexcept;

 private:
  [[nodiscard]] std::u32string ComposeRaw() const;

  EngineConfig config_;
  std::u32string raw_keys_;
  std::u32string visible_text_;
};

}  // namespace keyina
