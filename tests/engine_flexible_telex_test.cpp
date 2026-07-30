#include <array>
#include <string>
#include <string_view>

#include <keyina/engine.h>

#include "test_support.h"

namespace {

std::u32string TypeSequence(keyina::Engine& engine, std::u32string_view raw) {
  std::u32string external;
  for (const char32_t character : raw) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, character, false, false, false});
    KEYINA_EXPECT_TRUE(edit.consumed);
    KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
    KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
  }
  return external;
}

struct TelexCase {
  std::u32string_view raw;
  std::u32string_view expected;
};

}  // namespace

KEYINA_TEST(accepts_common_flexible_telex_key_orders) {
  constexpr std::array<TelexCase, 13> cases = {{
      {U"dduocwj", U"được"},
      {U"dduowcj", U"được"},
      {U"dduwocj", U"được"},
      {U"dduowjc", U"được"},
      {U"duocdwj", U"được"},
      {U"neesu", U"nếu"},
      {U"neesus", U"nếu"},
      {U"neuse", U"nếu"},
      {U"tieengs", U"tiếng"},
      {U"tieesng", U"tiếng"},
      {U"vieetj", U"việt"},
      {U"vieejt", U"việt"},
      {U"truocws", U"trước"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    const auto actual = TypeSequence(engine, test.raw);
    if (actual != test.expected) {
      std::string raw;
      raw.reserve(test.raw.size());
      for (const char32_t value : test.raw) {
        raw.push_back(static_cast<char>(value));
      }
      throw std::runtime_error("failed flexible Telex case: " + raw);
    }
  }
}

KEYINA_TEST(composes_every_vietnamese_vowel_shape_and_tone) {
  constexpr std::array<TelexCase, 72> cases = {{
      {U"a", U"a"}, {U"as", U"á"}, {U"af", U"à"}, {U"ar", U"ả"}, {U"ax", U"ã"}, {U"aj", U"ạ"},
      {U"aw", U"ă"}, {U"aws", U"ắ"}, {U"awf", U"ằ"}, {U"awr", U"ẳ"}, {U"awx", U"ẵ"}, {U"awj", U"ặ"},
      {U"aa", U"â"}, {U"aas", U"ấ"}, {U"aaf", U"ầ"}, {U"aar", U"ẩ"}, {U"aax", U"ẫ"}, {U"aaj", U"ậ"},
      {U"e", U"e"}, {U"es", U"é"}, {U"ef", U"è"}, {U"er", U"ẻ"}, {U"ex", U"ẽ"}, {U"ej", U"ẹ"},
      {U"ee", U"ê"}, {U"ees", U"ế"}, {U"eef", U"ề"}, {U"eer", U"ể"}, {U"eex", U"ễ"}, {U"eej", U"ệ"},
      {U"i", U"i"}, {U"is", U"í"}, {U"if", U"ì"}, {U"ir", U"ỉ"}, {U"ix", U"ĩ"}, {U"ij", U"ị"},
      {U"o", U"o"}, {U"os", U"ó"}, {U"of", U"ò"}, {U"or", U"ỏ"}, {U"ox", U"õ"}, {U"oj", U"ọ"},
      {U"oo", U"ô"}, {U"oos", U"ố"}, {U"oof", U"ồ"}, {U"oor", U"ổ"}, {U"oox", U"ỗ"}, {U"ooj", U"ộ"},
      {U"ow", U"ơ"}, {U"ows", U"ớ"}, {U"owf", U"ờ"}, {U"owr", U"ở"}, {U"owx", U"ỡ"}, {U"owj", U"ợ"},
      {U"u", U"u"}, {U"us", U"ú"}, {U"uf", U"ù"}, {U"ur", U"ủ"}, {U"ux", U"ũ"}, {U"uj", U"ụ"},
      {U"uw", U"ư"}, {U"uws", U"ứ"}, {U"uwf", U"ừ"}, {U"uwr", U"ử"}, {U"uwx", U"ữ"}, {U"uwj", U"ự"},
      {U"y", U"y"}, {U"ys", U"ý"}, {U"yf", U"ỳ"}, {U"yr", U"ỷ"}, {U"yx", U"ỹ"}, {U"yj", U"ỵ"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
}
