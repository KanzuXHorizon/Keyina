#include <keyina/windows/resident_input_controller.h>

#include "../test_support.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <new>
#include <string>
#include <string_view>
#include <utility>

namespace {
std::atomic<std::uint64_t> g_controller_allocation_count{0};
}

void* operator new(std::size_t size) {
  g_controller_allocation_count.fetch_add(1, std::memory_order_relaxed);
  if (void* memory = std::malloc(size)) {
    return memory;
  }
  throw std::bad_alloc{};
}

void* operator new[](std::size_t size) {
  g_controller_allocation_count.fetch_add(1, std::memory_order_relaxed);
  if (void* memory = std::malloc(size)) {
    return memory;
  }
  throw std::bad_alloc{};
}

void operator delete(void* memory) noexcept { std::free(memory); }
void operator delete[](void* memory) noexcept { std::free(memory); }
void operator delete(void* memory, std::size_t) noexcept { std::free(memory); }
void operator delete[](void* memory, std::size_t) noexcept { std::free(memory); }

namespace {

using keyina::windows::InputDecision;
using keyina::windows::PhysicalKeyEvent;
using keyina::windows::ResidentInputController;
using keyina::windows::RuntimeInputProfile;
using keyina::windows::RuntimeSnippetCommand;
using keyina::windows::RuntimeSnippetDefinition;
using keyina::windows::RuntimeSnippetProfile;
using keyina::windows::TypingContext;

constexpr TypingContext kOrdinaryContext{
    .foreground_process_id = 1,
    .focus_window = 1,
    .bypass_typing = false,
};

PhysicalKeyEvent KeyEvent(char32_t character, bool key_down = true) {
  const auto virtual_key = character == U' '
                               ? std::uint16_t{0x20}
                               : static_cast<std::uint16_t>(
                                     character >= U'a' && character <= U'z'
                                         ? character - U'a' + U'A'
                                         : character);
  return PhysicalKeyEvent{
      .virtual_key = virtual_key,
      .character = character,
      .key_down = key_down,
  };
}

void EraseCodepoints(std::u16string& text, std::uint16_t count) {
  while (count > 0 && !text.empty()) {
    text.pop_back();
    --count;
  }
}

void ApplyDecision(std::u16string& text, char32_t physical_character,
                   const InputDecision& decision) {
  if (!decision.suppress) {
    if (physical_character > 0 && physical_character <= 0xFFFF) {
      text.push_back(static_cast<char16_t>(physical_character));
    }
    return;
  }
  EraseCodepoints(text, decision.backspace_count);
  for (std::uint16_t index = 0; index < decision.insert_units; ++index) {
    text.push_back(static_cast<char16_t>(decision.insert[index]));
  }
  text.append(decision.extended_insert);
}

void Type(ResidentInputController& controller, std::u16string& visible,
          std::u32string_view raw, const TypingContext& context = kOrdinaryContext) {
  for (const auto character : raw) {
    const auto down = controller.Process(KeyEvent(character), context);
    ApplyDecision(visible, character, down);
    const auto up = controller.Process(KeyEvent(character, false), context);
    KEYINA_EXPECT_EQ(up.suppress, down.suppress);
  }
}

}  // namespace

KEYINA_TEST(resident_input_controller_fails_open_while_disabled) {
  auto profile = RuntimeInputProfile{};
  profile.vietnamese_enabled = false;
  ResidentInputController controller(profile);

  const auto decision = controller.Process(KeyEvent(U'a'), kOrdinaryContext);

  KEYINA_EXPECT_TRUE(!decision.suppress);
  KEYINA_EXPECT_TRUE(!controller.pointer_observation_required());
}

KEYINA_TEST(resident_input_controller_suppresses_owned_auto_repeat_without_recomposing) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"a");

  auto modifier_down = KeyEvent(U's');
  const auto transformed = controller.Process(modifier_down, kOrdinaryContext);
  ApplyDecision(visible, U's', transformed);
  KEYINA_EXPECT_TRUE(transformed.suppress);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á"});

  modifier_down.key_repeat = true;
  const auto repeated = controller.Process(modifier_down, kOrdinaryContext);
  ApplyDecision(visible, U's', repeated);
  KEYINA_EXPECT_TRUE(repeated.suppress);
  KEYINA_EXPECT_EQ(repeated.backspace_count, std::uint16_t{0});
  KEYINA_EXPECT_EQ(repeated.insert_units, std::uint16_t{0});
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á"});

  const auto modifier_up = controller.Process(KeyEvent(U's', false), kOrdinaryContext);
  KEYINA_EXPECT_TRUE(modifier_up.suppress);
}

KEYINA_TEST(resident_input_controller_leaves_unowned_literal_auto_repeat_pass_through) {
  ResidentInputController controller(RuntimeInputProfile{});

  auto literal = KeyEvent(U'q');
  const auto first = controller.Process(literal, kOrdinaryContext);
  KEYINA_EXPECT_TRUE(!first.suppress);

  literal.key_repeat = true;
  const auto repeated = controller.Process(literal, kOrdinaryContext);
  KEYINA_EXPECT_TRUE(!repeated.suppress);
}

KEYINA_TEST(resident_input_controller_clears_owned_repeat_on_focus_change) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"a");
  auto modifier = KeyEvent(U's');
  const auto transformed = controller.Process(modifier, kOrdinaryContext);
  ApplyDecision(visible, U's', transformed);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á"});

  auto changed_focus = kOrdinaryContext;
  changed_focus.focus_window = 2;
  modifier.key_repeat = true;
  const auto repeated = controller.Process(modifier, changed_focus);
  KEYINA_EXPECT_TRUE(!repeated.suppress);
}

KEYINA_TEST(resident_input_controller_clears_owned_repeat_in_secure_context) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"a");
  auto modifier = KeyEvent(U's');
  const auto transformed = controller.Process(modifier, kOrdinaryContext);
  ApplyDecision(visible, U's', transformed);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á"});

  auto secure = kOrdinaryContext;
  secure.bypass_typing = true;
  modifier.key_repeat = true;
  const auto repeated = controller.Process(modifier, secure);
  KEYINA_EXPECT_TRUE(!repeated.suppress);
  KEYINA_EXPECT_TRUE(!controller.pointer_observation_required());
}

KEYINA_TEST(resident_input_controller_allows_intentional_second_tone_press_after_key_up) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"a");

  auto modifier = KeyEvent(U's');
  auto decision = controller.Process(modifier, kOrdinaryContext);
  ApplyDecision(visible, U's', decision);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á"});

  const auto released = controller.Process(KeyEvent(U's', false), kOrdinaryContext);
  KEYINA_EXPECT_TRUE(released.suppress);

  decision = controller.Process(KeyEvent(U's'), kOrdinaryContext);
  ApplyDecision(visible, U's', decision);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"as"});
}

KEYINA_TEST(resident_input_controller_applies_typing_personality_profile) {
  auto quick_profile = RuntimeInputProfile{};
  quick_profile.quick_telex_letters = true;
  ResidentInputController quick_controller(quick_profile);
  std::u16string quick_visible;
  Type(quick_controller, quick_visible, U"[]");
  KEYINA_EXPECT_EQ(quick_visible, std::u16string{u"ươ"});

  auto simple_profile = RuntimeInputProfile{};
  simple_profile.standalone_w_to_u_horn = false;
  ResidentInputController simple_controller(simple_profile);
  std::u16string simple_visible;
  Type(simple_controller, simple_visible, U"w");
  KEYINA_EXPECT_EQ(simple_visible, std::u16string{u"w"});

  auto traditional_profile = RuntimeInputProfile{};
  traditional_profile.traditional_tone_placement = true;
  ResidentInputController traditional_controller(traditional_profile);
  std::u16string traditional_visible;
  Type(traditional_controller, traditional_visible, U"hoaf");
  KEYINA_EXPECT_EQ(traditional_visible, std::u16string{u"hòa"});
}

KEYINA_TEST(resident_input_controller_composes_telex_without_heap_output) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"tieengs");

  KEYINA_EXPECT_EQ(visible, std::u16string{u"tiếng"});
  KEYINA_EXPECT_TRUE(controller.pointer_observation_required());
}

KEYINA_TEST(resident_input_controller_keeps_embedded_tone_keys_literal_in_latin_tokens) {
  ResidentInputController controller(
      keyina::windows::DefaultRuntimeInputProfile());
  std::u16string visible;

  Type(controller, visible, U"register");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"register"});

  controller.Reset();
  visible.clear();
  Type(controller, visible, U"process");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"process"});
}

KEYINA_TEST(resident_input_controller_preserves_mixed_vietnamese_and_latin_sentence) {
  ResidentInputController controller(
      keyina::windows::DefaultRuntimeInputProfile());
  std::u16string visible;

  Type(
      controller,
      visible,
      U"tuyf banj cuws research vaf dduwa ra huowngs toots nhaats ");

  KEYINA_EXPECT_EQ(
      visible,
      std::u16string{u"tuỳ bạn cứ research và đưa ra hướng tốt nhất "});
}

KEYINA_TEST(resident_input_controller_resets_on_pointer_focus_and_secure_context) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"a");
  controller.OnPointerReset();
  Type(controller, visible, U"s");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"as"});

  Type(controller, visible, U"a");
  auto changed_focus = kOrdinaryContext;
  changed_focus.focus_window = 2;
  Type(controller, visible, U"s", changed_focus);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"asas"});

  Type(controller, visible, U"a", changed_focus);
  auto secure = changed_focus;
  secure.bypass_typing = true;
  Type(controller, visible, U"s", secure);
  KEYINA_EXPECT_EQ(visible, std::u16string{u"asasas"});
  KEYINA_EXPECT_TRUE(!controller.pointer_observation_required());
}

KEYINA_TEST(resident_input_controller_bypasses_injected_shortcut_and_backspace_events) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"a");
  auto injected = KeyEvent(U's');
  injected.injected_by_keyina = true;
  KEYINA_EXPECT_TRUE(!controller.Process(injected, kOrdinaryContext).suppress);

  auto shortcut = KeyEvent(U'c');
  shortcut.control = true;
  KEYINA_EXPECT_TRUE(!controller.Process(shortcut, kOrdinaryContext).suppress);

  auto backspace = PhysicalKeyEvent{
      .virtual_key = 0x08,
      .key_down = true,
  };
  KEYINA_EXPECT_TRUE(!controller.Process(backspace, kOrdinaryContext).suppress);
  KEYINA_EXPECT_TRUE(!controller.pointer_observation_required());

  Type(controller, visible, U"s");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"as"});
}

KEYINA_TEST(resident_input_controller_disarms_pointer_observation_on_boundary) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"as");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á"});
  KEYINA_EXPECT_TRUE(controller.pointer_observation_required());

  Type(controller, visible, U" ");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"á "});
  KEYINA_EXPECT_TRUE(!controller.pointer_observation_required());
}

KEYINA_TEST(resident_input_controller_restores_word_after_deleting_boundary) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U"ba ");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"ba "});

  PhysicalKeyEvent backspace{};
  backspace.virtual_key = 0x08;
  backspace.key_down = true;
  const auto backspace_decision =
      controller.Process(backspace, kOrdinaryContext);
  KEYINA_EXPECT_TRUE(!backspace_decision.suppress);
  visible.pop_back();

  Type(controller, visible, U"or");

  KEYINA_EXPECT_EQ(visible, std::u16string{u"bảo"});
}

KEYINA_TEST(resident_input_controller_restores_word_after_deleting_literal_suffix_and_boundary) {
  ResidentInputController controller(
      keyina::windows::DefaultRuntimeInputProfile());
  std::u16string visible;

  Type(controller, visible, U"sai x");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"sai x"});

  PhysicalKeyEvent backspace{};
  backspace.virtual_key = 0x08;
  backspace.key_down = true;

  KEYINA_EXPECT_TRUE(
      !controller.Process(backspace, kOrdinaryContext).suppress);
  visible.pop_back();
  KEYINA_EXPECT_TRUE(
      !controller.Process(backspace, kOrdinaryContext).suppress);
  visible.pop_back();

  Type(controller, visible, U"f");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"sài"});
}

KEYINA_TEST(resident_input_controller_restores_word_after_multiple_literal_suffix_characters) {
  ResidentInputController controller(
      keyina::windows::DefaultRuntimeInputProfile());
  std::u16string visible;

  Type(controller, visible, U"sai xyz");

  PhysicalKeyEvent backspace{};
  backspace.virtual_key = 0x08;
  backspace.key_down = true;
  for (int index = 0; index < 4; ++index) {
    KEYINA_EXPECT_TRUE(
        !controller.Process(backspace, kOrdinaryContext).suppress);
    visible.pop_back();
  }

  Type(controller, visible, U"f");
  KEYINA_EXPECT_EQ(visible, std::u16string{u"sài"});
}

KEYINA_TEST(resident_input_controller_expands_raw_telex_sensitive_snippet_on_space) {
  RuntimeSnippetProfile snippets{};
  RuntimeSnippetDefinition definition{};
  definition.trigger = U";aws";
  definition.expansion = u"literal";
  definition.delimiters = U" ";
  definition.case_sensitive = true;
  definition.preserve_delimiter = false;
  snippets.entries.push_back(std::move(definition));
  ResidentInputController controller(RuntimeInputProfile{}, std::move(snippets));
  std::u16string visible;

  Type(controller, visible, U";aws ");

  KEYINA_EXPECT_EQ(visible, std::u16string{u"literal"});
}

KEYINA_TEST(resident_input_controller_runs_builtin_commands_even_when_telex_is_off) {
  RuntimeInputProfile profile{};
  profile.vietnamese_enabled = false;
  ResidentInputController controller(profile);
  std::u16string visible;
  Type(controller, visible, U";kvi");

  const auto decision = controller.Process(KeyEvent(U' '), kOrdinaryContext);
  ApplyDecision(visible, U' ', decision);

  KEYINA_EXPECT_TRUE(decision.suppress);
  KEYINA_EXPECT_EQ(decision.snippet_command,
                   RuntimeSnippetCommand::ToggleVietnamese);
  KEYINA_EXPECT_EQ(visible, std::u16string{});
}

KEYINA_TEST(resident_input_controller_routes_external_output_payload_without_blocking_hook) {
  RuntimeSnippetProfile snippets{};
  RuntimeSnippetDefinition definition{};
  definition.trigger = U";kcmd";
  definition.expansion = u"{\"ExecutablePath\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\",\"Arguments\":\"/d /c echo hi\",\"WorkingDirectory\":\"\",\"TimeoutMilliseconds\":1000}";
  definition.delimiters = U" ";
  definition.case_sensitive = false;
  definition.preserve_delimiter = false;
  definition.command = RuntimeSnippetCommand::ExternalOutput;
  snippets.entries.push_back(std::move(definition));
  ResidentInputController controller(RuntimeInputProfile{}, std::move(snippets));
  std::u16string visible;

  Type(controller, visible, U";kcmd");
  const auto decision = controller.Process(KeyEvent(U' '), kOrdinaryContext);
  ApplyDecision(visible, U' ', decision);

  KEYINA_EXPECT_TRUE(decision.suppress);
  KEYINA_EXPECT_EQ(decision.snippet_command,
                   RuntimeSnippetCommand::ExternalOutput);
  KEYINA_EXPECT_TRUE(!decision.snippet_command_payload.empty());
  KEYINA_EXPECT_EQ(decision.snippet_target_process_id,
                   kOrdinaryContext.foreground_process_id);
  KEYINA_EXPECT_EQ(decision.snippet_target_focus_window,
                   kOrdinaryContext.focus_window);
  KEYINA_EXPECT_EQ(visible, std::u16string{});
}

KEYINA_TEST(resident_input_controller_preserves_variable_snippet_delimiter) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;

  Type(controller, visible, U";kdate ");

  KEYINA_EXPECT_EQ(visible.size(), std::size_t{11});
  KEYINA_EXPECT_EQ(visible[4], u'-');
  KEYINA_EXPECT_EQ(visible[7], u'-');
  KEYINA_EXPECT_EQ(visible[10], u' ');
}

KEYINA_TEST(resident_input_controller_bypasses_snippets_in_secure_context) {
  ResidentInputController controller(RuntimeInputProfile{});
  std::u16string visible;
  auto secure = kOrdinaryContext;
  secure.bypass_typing = true;

  Type(controller, visible, U";kvi ", secure);

  KEYINA_EXPECT_EQ(visible, std::u16string{u";kvi "});
}

KEYINA_TEST(resident_input_controller_allocates_zero_on_ordinary_hot_path) {
  ResidentInputController controller(RuntimeInputProfile{});
  for (std::size_t index = 0; index < 10'000; ++index) {
    controller.Reset();
    (void)controller.Process(KeyEvent(U'a'), kOrdinaryContext);
    (void)controller.Process(KeyEvent(U'a', false), kOrdinaryContext);
    (void)controller.Process(KeyEvent(U's'), kOrdinaryContext);
    (void)controller.Process(KeyEvent(U's', false), kOrdinaryContext);
  }

  const auto before =
      g_controller_allocation_count.load(std::memory_order_relaxed);
  std::uint64_t checksum = 0;
  for (std::size_t index = 0; index < 100'000; ++index) {
    controller.Reset();
    const auto literal = controller.Process(KeyEvent(U'a'), kOrdinaryContext);
    const auto literal_up =
        controller.Process(KeyEvent(U'a', false), kOrdinaryContext);
    const auto transformed = controller.Process(KeyEvent(U's'), kOrdinaryContext);
    const auto transformed_up =
        controller.Process(KeyEvent(U's', false), kOrdinaryContext);
    checksum += literal.insert_units + literal_up.insert_units +
                transformed.insert_units + transformed_up.insert_units +
                static_cast<std::uint64_t>(transformed.suppress);
  }
  const auto after =
      g_controller_allocation_count.load(std::memory_order_relaxed);
  KEYINA_EXPECT_EQ(after - before, std::uint64_t{0});
  KEYINA_EXPECT_TRUE(checksum > 0);
}

KEYINA_TEST(resident_input_controller_survives_one_million_mixed_events) {
  ResidentInputController controller(RuntimeInputProfile{});
  auto context = kOrdinaryContext;
  std::uint32_t state = 0xC001D00Du;
  std::uint64_t checksum = 0;

  for (std::size_t index = 0; index < 1'000'000; ++index) {
    state = state * 1664525u + 1013904223u;
    PhysicalKeyEvent event{};
    switch ((state >> 24u) % 10u) {
      case 0:
      case 1:
      case 2:
      case 3:
        event = KeyEvent(static_cast<char32_t>(U'a' + (state % 26u)));
        break;
      case 4:
        event = KeyEvent(U' ');
        break;
      case 5:
        event.virtual_key = 0x08;
        event.key_down = true;
        break;
      case 6:
        controller.OnPointerReset();
        event = KeyEvent(U's');
        break;
      case 7:
        ++context.focus_window;
        event = KeyEvent(U'a');
        break;
      case 8:
        context.bypass_typing = !context.bypass_typing;
        event = KeyEvent(U'a');
        break;
      default:
        event = KeyEvent(U'x');
        event.injected_by_keyina = true;
        break;
    }

    const auto decision = controller.Process(event, context);
    KEYINA_EXPECT_TRUE(decision.insert_units <= decision.insert.size());
    KEYINA_EXPECT_TRUE(decision.backspace_count <= keyina::kMaxActiveKeys + 1);
    if (!decision.suppress) {
      KEYINA_EXPECT_EQ(decision.insert_units, std::uint16_t{0});
      KEYINA_EXPECT_EQ(decision.backspace_count, std::uint16_t{0});
    }
    checksum += decision.insert_units + decision.backspace_count +
                static_cast<std::uint64_t>(decision.suppress) +
                static_cast<std::uint64_t>(
                    controller.pointer_observation_required());
  }

  KEYINA_EXPECT_TRUE(checksum > 0);
}
