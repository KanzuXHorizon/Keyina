#include <keyina/vietnamese_syllable.h>

#include <array>
#include <string>
#include <string_view>

#include <keyina/vietnamese.h>

namespace keyina {
namespace {

constexpr char32_t ToLower(char32_t value) noexcept {
  if (value >= U'A' && value <= U'Z') {
    return value + (U'a' - U'A');
  }
  if (value == U'Đ') {
    return U'đ';
  }
  return value;
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

constexpr std::array<std::u32string_view, 26> kOnsets = {
    U"", U"b", U"c", U"ch", U"d", U"đ", U"g", U"gh", U"gi",
    U"h", U"k", U"kh", U"l", U"m", U"n", U"ng", U"ngh", U"nh",
    U"ph", U"qu", U"r", U"s", U"t", U"th", U"tr", U"v",
};

constexpr std::array<std::u32string_view, 1> kExtraOnsets = {U"x"};

constexpr std::array<std::u32string_view, 9> kCodas = {
    U"", U"c", U"ch", U"m", U"n", U"ng", U"nh", U"p", U"t",
};

constexpr std::array<std::u32string_view, 56> kNuclei = {
    U"a", U"e", U"ê", U"i", U"o", U"ô", U"ơ", U"u", U"ư", U"y",
    U"ai", U"ao", U"au", U"ay", U"âu", U"ây", U"eo", U"êu", U"ia",
    U"iê", U"iu", U"oa", U"oă", U"oe", U"oi", U"ôi", U"ơi", U"ua",
    U"uâ", U"uê", U"ui", U"uô", U"uy", U"ưa", U"ưi", U"ưu", U"ươ",
    U"yê", U"iêu", U"oai", U"oao", U"oay", U"uai", U"uao", U"uây",
    U"uôi", U"ươi", U"ươu", U"uya", U"uyê", U"uyu", U"oeo", U"uêu",
    U"yêu", U"ươ", U"uơ",
};

bool IsFrontVowel(char32_t vowel) noexcept {
  return vowel == U'e' || vowel == U'ê' || vowel == U'i' || vowel == U'y';
}

bool IsCheckedCoda(std::u32string_view coda) noexcept {
  return coda == U"c" || coda == U"ch" || coda == U"p" || coda == U"t";
}

}  // namespace

bool IsValidVietnameseSyllable(std::u32string_view syllable) noexcept {
  if (syllable.empty() || syllable.size() > 12) {
    return false;
  }

  std::u32string lowered;
  lowered.reserve(syllable.size());
  std::u32string normalized_vowels;
  normalized_vowels.reserve(4);
  std::array<std::size_t, 4> vowel_positions{};
  std::size_t vowel_count = 0;
  Tone tone = Tone::None;
  std::size_t tone_bearing_vowels = 0;

  for (std::size_t index = 0; index < syllable.size(); ++index) {
    const char32_t scalar = ToLower(syllable[index]);
    lowered.push_back(scalar);
    const auto letter = DecomposeVietnamese(scalar);
    if (!letter.has_value() || letter->base == U'đ') {
      continue;
    }
    if (vowel_count >= vowel_positions.size()) {
      return false;
    }
    vowel_positions[vowel_count++] = index;
    normalized_vowels.push_back(NormalizeVowel(*letter));
    if (letter->tone != Tone::None) {
      ++tone_bearing_vowels;
      if (tone_bearing_vowels > 1 ||
          (tone != Tone::None && tone != letter->tone)) {
        return false;
      }
      tone = letter->tone;
    }
  }

  if (vowel_count == 0) {
    return false;
  }

  std::size_t nucleus_start = vowel_positions[0];
  std::u32string_view onset{lowered.data(), nucleus_start};

  if (lowered.starts_with(U"qu") && lowered.size() > 2 &&
      IsVietnameseVowel(lowered[2])) {
    onset = U"qu";
    nucleus_start = 2;
  } else if (lowered.starts_with(U"gi") && lowered.size() > 2 &&
             IsVietnameseVowel(lowered[2])) {
    onset = U"gi";
    nucleus_start = 2;
  }

  if (!Contains(kOnsets, onset) && !Contains(kExtraOnsets, onset)) {
    return false;
  }

  const auto first_nucleus_letter = DecomposeVietnamese(lowered[nucleus_start]);
  if (!first_nucleus_letter.has_value() || first_nucleus_letter->base == U'đ') {
    return false;
  }
  const char32_t first_vowel = NormalizeVowel(*first_nucleus_letter);

  if ((onset == U"gh" || onset == U"ngh" || onset == U"k") &&
      !IsFrontVowel(first_vowel)) {
    return false;
  }
  const bool gi_i_nucleus =
      onset == U"g" && nucleus_start == 1 && first_vowel == U'i';
  if ((onset == U"g" || onset == U"ng" || onset == U"c") &&
      IsFrontVowel(first_vowel) && !gi_i_nucleus) {
    return false;
  }
  if (onset == U"qu" && first_vowel == U'ư') {
    return false;
  }
  if (onset == U"gi" && first_vowel == U'i') {
    return false;
  }

  std::u32string_view coda;
  std::size_t nucleus_end = lowered.size();
  for (const auto candidate : kCodas) {
    if (candidate.empty() || candidate.size() > lowered.size() - nucleus_start) {
      continue;
    }
    if (lowered.ends_with(candidate) && candidate.size() > coda.size()) {
      coda = candidate;
      nucleus_end = lowered.size() - candidate.size();
    }
  }

  for (std::size_t index = nucleus_start; index < nucleus_end; ++index) {
    const auto letter = DecomposeVietnamese(lowered[index]);
    if (!letter.has_value() || letter->base == U'đ') {
      return false;
    }
  }

  std::u32string nucleus;
  nucleus.reserve(nucleus_end - nucleus_start);
  for (std::size_t index = nucleus_start; index < nucleus_end; ++index) {
    const auto letter = DecomposeVietnamese(lowered[index]);
    nucleus.push_back(NormalizeVowel(*letter));
  }

  const bool short_closed_nucleus =
      !coda.empty() && (nucleus == U"ă" || nucleus == U"â");
  if (!short_closed_nucleus && !Contains(kNuclei, nucleus)) {
    return false;
  }

  if (IsCheckedCoda(coda) && tone != Tone::Acute && tone != Tone::Dot) {
    return false;
  }

  return true;
}

}  // namespace keyina
