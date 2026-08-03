#include <keyina/vietnamese_syllable.h>

#include <array>
#include <string_view>

namespace keyina {
namespace {

constexpr std::size_t kMaximumSyllableLength = 12;
constexpr std::size_t kMaximumNucleusLength = 4;

constexpr char32_t ToLower(char32_t value) noexcept {
  if (value >= U'A' && value <= U'Z') {
    return value + (U'a' - U'A');
  }
  return value == U'Đ' ? U'đ' : value;
}

char32_t NormalizeVowel(const VietnameseLetter& letter) noexcept {
  switch (letter.base) {
    case U'a':
      if (letter.shape == VowelShape::Breve) return U'ă';
      if (letter.shape == VowelShape::Circumflex) return U'â';
      return U'a';
    case U'e':
      return letter.shape == VowelShape::Circumflex ? U'ê' : U'e';
    case U'o':
      if (letter.shape == VowelShape::Circumflex) return U'ô';
      if (letter.shape == VowelShape::Horn) return U'ơ';
      return U'o';
    case U'u':
      return letter.shape == VowelShape::Horn ? U'ư' : U'u';
    default:
      return letter.base;
  }
}

template <std::size_t N>
bool Contains(const std::array<std::u32string_view, N>& values,
              std::u32string_view value) noexcept {
  for (const auto candidate : values) {
    if (candidate == value) {
      return true;
    }
  }
  return false;
}

template <std::size_t N>
bool IsPrefixOfAny(const std::array<std::u32string_view, N>& values,
                   std::u32string_view value) noexcept {
  for (const auto candidate : values) {
    if (candidate.starts_with(value)) {
      return true;
    }
  }
  return false;
}

constexpr std::array<std::u32string_view, 27> kOnsets = {
    U"", U"b", U"c", U"ch", U"d", U"đ", U"g", U"gh", U"gi",
    U"h", U"k", U"kh", U"l", U"m", U"n", U"ng", U"ngh", U"nh",
    U"ph", U"qu", U"r", U"s", U"t", U"th", U"tr", U"v", U"x",
};

constexpr std::array<std::u32string_view, 9> kCodas = {
    U"", U"c", U"ch", U"m", U"n", U"ng", U"nh", U"p", U"t",
};

constexpr std::array<std::u32string_view, 55> kNuclei = {
    U"a", U"e", U"ê", U"i", U"o", U"ô", U"ơ", U"u", U"ư", U"y",
    U"ai", U"ao", U"au", U"ay", U"âu", U"ây", U"eo", U"êu", U"ia",
    U"iê", U"iu", U"oa", U"oă", U"oe", U"oi", U"ôi", U"ơi", U"ua",
    U"uâ", U"uê", U"ui", U"uô", U"uy", U"ưa", U"ưi", U"ưu", U"ươ",
    U"uơ", U"yê", U"iêu", U"oai", U"oao", U"oay", U"uai", U"uao",
    U"uây", U"uôi", U"ươi", U"ươu", U"uya", U"uyê", U"uyu", U"oeo",
    U"uêu", U"yêu",
};

bool MatchesSupportedNucleusIgnoringShape(
    std::u32string_view nucleus) noexcept {
  for (const auto supported : kNuclei) {
    if (supported.size() != nucleus.size()) {
      continue;
    }
    bool matches = true;
    for (std::size_t index = 0; index < nucleus.size(); ++index) {
      const auto actual = DecomposeVietnamese(nucleus[index]);
      const auto expected = DecomposeVietnamese(supported[index]);
      if (!actual.has_value() || !expected.has_value() ||
          actual->base != expected->base) {
        matches = false;
        break;
      }
    }
    if (matches) {
      return true;
    }
  }
  return false;
}

bool IsFrontVowel(char32_t vowel) noexcept {
  return vowel == U'e' || vowel == U'ê' || vowel == U'i' || vowel == U'y';
}

bool IsCheckedCoda(std::u32string_view coda) noexcept {
  return coda == U"c" || coda == U"ch" || coda == U"p" || coda == U"t";
}

std::u32string_view View(const std::array<char32_t, kMaximumSyllableLength>& data,
                         std::size_t begin, std::size_t size) noexcept {
  return {data.data() + begin, size};
}

SyllableAnalysis Failure(SyllableError error) noexcept {
  return {
      .status = SyllableStatus::Impossible,
      .error = error,
      .onset = {},
      .nucleus = {},
      .coda = {},
      .tone = Tone::None,
      .tone_index = std::u32string_view::npos,
  };
}

}  // namespace

SyllableAnalysis AnalyzeVietnameseSyllable(
    std::u32string_view syllable) noexcept {
  if (syllable.empty()) {
    return Failure(SyllableError::MissingNucleus);
  }
  if (syllable.size() > kMaximumSyllableLength) {
    return Failure(SyllableError::TooLong);
  }

  std::array<char32_t, kMaximumSyllableLength> lowered{};
  std::array<std::size_t, kMaximumNucleusLength> vowel_positions{};
  std::size_t vowel_count = 0;
  std::size_t vowel_runs = 0;
  bool previous_was_vowel = false;
  Tone tone = Tone::None;
  std::size_t tone_index = std::u32string_view::npos;

  for (std::size_t index = 0; index < syllable.size(); ++index) {
    lowered[index] = ToLower(syllable[index]);
    const auto letter = DecomposeVietnamese(lowered[index]);
    const bool is_vowel = letter.has_value() && letter->base != U'đ';
    if (!is_vowel) {
      previous_was_vowel = false;
      continue;
    }
    if (!previous_was_vowel) {
      ++vowel_runs;
    }
    previous_was_vowel = true;
    if (vowel_count == vowel_positions.size()) {
      return Failure(SyllableError::InvalidNucleus);
    }
    vowel_positions[vowel_count++] = index;
    if (letter->tone != Tone::None) {
      if (tone != Tone::None) {
        return Failure(SyllableError::InvalidTone);
      }
      tone = letter->tone;
      tone_index = index;
    }
  }

  if (vowel_count == 0) {
    const auto lowered_view = View(lowered, 0, syllable.size());
    if (IsPrefixOfAny(kOnsets, lowered_view)) {
      return {
          .status = SyllableStatus::RecoverablePrefix,
          .error = SyllableError::MissingNucleus,
          .onset = syllable,
          .nucleus = {},
          .coda = {},
          .tone = Tone::None,
          .tone_index = std::u32string_view::npos,
      };
    }
    return Failure(SyllableError::MissingNucleus);
  }

  std::size_t nucleus_start = vowel_positions[0];
  auto lowered_view = View(lowered, 0, syllable.size());
  std::u32string_view onset = lowered_view.substr(0, nucleus_start);

  if (lowered_view.starts_with(U"qu") && syllable.size() > 2 &&
      IsVietnameseVowel(lowered[2])) {
    onset = U"qu";
    nucleus_start = 2;
  } else if (lowered_view.starts_with(U"gi") && syllable.size() > 2 &&
             IsVietnameseVowel(lowered[2])) {
    onset = U"gi";
    nucleus_start = 2;
  }

  if (!Contains(kOnsets, onset)) {
    return Failure(SyllableError::InvalidOnset);
  }

  const auto first_nucleus_letter = DecomposeVietnamese(lowered[nucleus_start]);
  if (!first_nucleus_letter.has_value() || first_nucleus_letter->base == U'đ') {
    return Failure(SyllableError::MissingNucleus);
  }
  const char32_t first_vowel = NormalizeVowel(*first_nucleus_letter);

  if ((onset == U"gh" || onset == U"ngh" || onset == U"k") &&
      !IsFrontVowel(first_vowel)) {
    return Failure(SyllableError::InvalidOrthography);
  }
  const bool gi_i_nucleus =
      onset == U"g" && nucleus_start == 1 && first_vowel == U'i';
  if ((onset == U"g" || onset == U"ng" || onset == U"c") &&
      IsFrontVowel(first_vowel) && !gi_i_nucleus) {
    return Failure(SyllableError::InvalidOrthography);
  }
  if ((onset == U"qu" && first_vowel == U'ư') ||
      (onset == U"gi" && first_vowel == U'i')) {
    return Failure(SyllableError::InvalidOrthography);
  }

  std::u32string_view coda;
  std::size_t nucleus_end = syllable.size();
  for (const auto candidate : kCodas) {
    if (candidate.empty() || candidate.size() > syllable.size() - nucleus_start) {
      continue;
    }
    if (lowered_view.ends_with(candidate) && candidate.size() > coda.size()) {
      coda = candidate;
      nucleus_end = syllable.size() - candidate.size();
    }
  }

  if (nucleus_end <= nucleus_start) {
    return Failure(SyllableError::MissingNucleus);
  }

  std::array<char32_t, kMaximumNucleusLength> normalized_nucleus{};
  const std::size_t nucleus_length = nucleus_end - nucleus_start;
  if (nucleus_length > normalized_nucleus.size()) {
    return Failure(SyllableError::InvalidNucleus);
  }
  for (std::size_t offset = 0; offset < nucleus_length; ++offset) {
    const auto letter = DecomposeVietnamese(lowered[nucleus_start + offset]);
    if (!letter.has_value() || letter->base == U'đ') {
      if (vowel_runs == 2) {
        return {
            .status = SyllableStatus::Ambiguous,
            .error = SyllableError::InvalidCoda,
            .onset = {},
            .nucleus = {},
            .coda = {},
            .tone = Tone::None,
            .tone_index = std::u32string_view::npos,
        };
      }
      return Failure(SyllableError::InvalidCoda);
    }
    normalized_nucleus[offset] = NormalizeVowel(*letter);
  }

  const std::u32string_view nucleus_key{normalized_nucleus.data(), nucleus_length};
  const bool short_closed_nucleus =
      !coda.empty() && (nucleus_key == U"ă" || nucleus_key == U"â");
  if (!short_closed_nucleus && !Contains(kNuclei, nucleus_key)) {
    return Failure(SyllableError::InvalidNucleus);
  }
  if (IsCheckedCoda(coda) && tone != Tone::Acute && tone != Tone::Dot) {
    return Failure(SyllableError::InvalidTone);
  }

  return {
      .status = SyllableStatus::Valid,
      .error = SyllableError::None,
      .onset = syllable.substr(0, nucleus_start),
      .nucleus = syllable.substr(nucleus_start, nucleus_length),
      .coda = syllable.substr(nucleus_end),
      .tone = tone,
      .tone_index = tone_index,
  };
}

bool IsValidVietnameseSyllable(std::u32string_view syllable) noexcept {
  return AnalyzeVietnameseSyllable(syllable).status == SyllableStatus::Valid;
}

std::size_t SelectVietnameseToneOffset(std::u32string_view nucleus,
                                       bool has_coda,
                                       bool modern_placement) noexcept {
  if (nucleus.empty() || nucleus.size() > kMaximumNucleusLength) {
    return std::u32string_view::npos;
  }

  std::array<char32_t, kMaximumNucleusLength> normalized{};
  for (std::size_t offset = 0; offset < nucleus.size(); ++offset) {
    const auto letter = DecomposeVietnamese(nucleus[offset]);
    if (!letter.has_value() || letter->base == U'đ') {
      return std::u32string_view::npos;
    }
    normalized[offset] = NormalizeVowel(*letter);
  }
  const std::u32string_view nucleus_key{normalized.data(), nucleus.size()};
  if (!Contains(kNuclei, nucleus_key) &&
      !MatchesSupportedNucleusIgnoringShape(nucleus)) {
    return std::u32string_view::npos;
  }

  for (std::size_t offset = nucleus.size(); offset > 0; --offset) {
    const auto letter = DecomposeVietnamese(nucleus[offset - 1]);
    if (letter->shape != VowelShape::Plain) {
      return offset - 1;
    }
  }

  if (nucleus.size() == 1) {
    return 0;
  }
  if (nucleus.size() >= 3) {
    return has_coda ? nucleus.size() - 1 : nucleus.size() - 2;
  }
  if (has_coda) {
    return 1;
  }

  const auto first = DecomposeVietnamese(nucleus[0]);
  const auto second = DecomposeVietnamese(nucleus[1]);
  if (!first.has_value() || !second.has_value() || first->base == U'đ' ||
      second->base == U'đ') {
    return std::u32string_view::npos;
  }
  const bool modern_open_cluster =
      (first->base == U'o' &&
       (second->base == U'a' || second->base == U'e')) ||
      (first->base == U'u' && second->base == U'y');
  return modern_placement && modern_open_cluster ? 1 : 0;
}

}  // namespace keyina
