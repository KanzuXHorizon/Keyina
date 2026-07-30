#include <array>
#include <cstddef>
#include <cstdint>
#include <string>

#include <keyina/engine.h>

#include "test_support.h"

namespace {

void ApplyEdit(std::u32string& visible, const keyina::TextEdit& edit) {
  if (edit.commit_before) {
    visible.clear();
  }
  KEYINA_EXPECT_TRUE(edit.erase_codepoints <= visible.size());
  visible.erase(visible.size() - edit.erase_codepoints);
  visible.append(edit.insert);
}

std::uint32_t NextRandom(std::uint32_t& state) noexcept {
  state ^= state << 13;
  state ^= state >> 17;
  state ^= state << 5;
  return state;
}

}  // namespace

KEYINA_TEST(million_event_endurance_preserves_engine_invariants) {
  constexpr std::size_t kEventCount = 1'000'000;
  constexpr std::array<char32_t, 32> kAlphabet = {
      U'a', U'b', U'c', U'd', U'e', U'f', U'g', U'h',
      U'i', U'j', U'o', U'r', U's', U'u', U'w', U'x',
      U'A', U'D', U'E', U'O', U'U', U'0', U'7', U'@',
      U'.', U'/', U':', U'_', U'-', U'á', U'Đ', U'🙂',
  };

  keyina::Engine engine({
      .tone_placement = keyina::TonePlacement::Modern,
      .application_bypass = false,
      .restore_invalid_word = true,
  });
  std::u32string external;
  external.reserve(keyina::kMaxActiveKeys + 1);
  std::uint32_t random_state = 0x4B455949U;

  for (std::size_t index = 0; index < kEventCount; ++index) {
    const std::uint32_t random = NextRandom(random_state);
    const std::uint32_t action = random % 100U;

    if (action < 78U) {
      const char32_t character =
          kAlphabet[(random >> 8U) % kAlphabet.size()];
      const auto edit = engine.Process({
          keyina::KeyKind::Character,
          character,
          false,
          false,
          false,
      });
      KEYINA_EXPECT_TRUE(edit.consumed);
      ApplyEdit(external, edit);
      KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
    } else if (action < 88U) {
      const auto edit = engine.Process({
          keyina::KeyKind::Backspace,
          U'\0',
          false,
          false,
          false,
      });
      ApplyEdit(external, edit);
      KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
    } else if (action < 96U) {
      const auto edit = engine.Process({
          keyina::KeyKind::CommitBoundary,
          U' ',
          false,
          false,
          false,
      });
      ApplyEdit(external, edit);
      external.clear();
      KEYINA_EXPECT_TRUE(engine.RawKeys().empty());
      KEYINA_EXPECT_TRUE(engine.VisibleText().empty());
    } else {
      const auto edit = engine.Process({
          keyina::KeyKind::Reset,
          U'\0',
          false,
          false,
          false,
      });
      KEYINA_EXPECT_TRUE(!edit.consumed);
      external.clear();
    }

    KEYINA_EXPECT_TRUE(engine.RawKeys().size() <= keyina::kMaxActiveKeys);
  }
}
