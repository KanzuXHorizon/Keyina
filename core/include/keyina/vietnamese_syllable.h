#pragma once

#include <cstddef>
#include <string_view>

#include <keyina/vietnamese.h>

namespace keyina {

enum class SyllableStatus {
  Valid,
  RecoverablePrefix,
  Impossible,
  Ambiguous,
};

enum class SyllableError {
  None,
  TooLong,
  MissingNucleus,
  InvalidOnset,
  InvalidNucleus,
  InvalidCoda,
  InvalidTone,
  InvalidOrthography,
};

struct SyllableAnalysis {
  SyllableStatus status{SyllableStatus::Impossible};
  SyllableError error{SyllableError::MissingNucleus};
  std::u32string_view onset;
  std::u32string_view nucleus;
  std::u32string_view coda;
  Tone tone{Tone::None};
  std::size_t tone_index{std::u32string_view::npos};
};

[[nodiscard]] SyllableAnalysis AnalyzeVietnameseSyllable(
    std::u32string_view syllable) noexcept;

[[nodiscard]] bool IsValidVietnameseSyllable(
    std::u32string_view syllable) noexcept;

// Returns the zero-based vowel offset that should carry the tone mark.
// `nucleus` may contain precomposed Vietnamese vowels and tone marks.
// Returns std::u32string_view::npos when the input is not a vowel nucleus.
[[nodiscard]] std::size_t SelectVietnameseToneOffset(
    std::u32string_view nucleus, bool has_coda,
    bool modern_placement) noexcept;

}  // namespace keyina
