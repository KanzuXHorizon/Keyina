#include <keyina/tsf/key_router.h>

#include <windows.h>

#include <keyina/input_character_classification.h>

#include <array>

namespace keyina::tsf {
namespace {

constexpr KeyRoute PassThrough() noexcept { return {}; }

constexpr KeyRoute RouteTextCharacter(char32_t character) noexcept {
  return {
      ClassifyInputCharacter(character, false) ==
              InputCharacterClass::Composition
          ? KeyRouteKind::Character
          : KeyRouteKind::Boundary,
      character,
  };
}

constexpr KeyRoute OwnedOnly(KeyRoute route,
                             bool active_composition) noexcept {
  return active_composition ? route : PassThrough();
}

constexpr char32_t ShiftedDigit(std::uint32_t virtual_key) noexcept {
  constexpr std::array<char32_t, 10> shifted = {
      U')', U'!', U'@', U'#', U'$', U'%', U'^', U'&', U'*', U'(',
  };
  return shifted[virtual_key - static_cast<std::uint32_t>('0')];
}

constexpr bool IsResetKey(std::uint32_t virtual_key) noexcept {
  switch (virtual_key) {
    case VK_ESCAPE:
    case VK_LEFT:
    case VK_RIGHT:
    case VK_UP:
    case VK_DOWN:
    case VK_HOME:
    case VK_END:
    case VK_PRIOR:
    case VK_NEXT:
    case VK_INSERT:
    case VK_DELETE:
      return true;
    default:
      return false;
  }
}

constexpr KeyRoute RouteBoundary(std::uint32_t virtual_key,
                                 bool shift) noexcept {
  switch (virtual_key) {
    case VK_SPACE:
      return {KeyRouteKind::Boundary, U' '};
    case VK_TAB:
      return {KeyRouteKind::Boundary, U'\t'};
    case VK_RETURN:
      return {KeyRouteKind::Boundary, U'\n'};
    case VK_OEM_COMMA:
      return RouteTextCharacter(shift ? U'<' : U',');
    case VK_OEM_1:
      return RouteTextCharacter(shift ? U':' : U';');
    case VK_OEM_2:
      return RouteTextCharacter(shift ? U'?' : U'/');
    default:
      return PassThrough();
  }
}

constexpr KeyRoute RouteTechnical(std::uint32_t virtual_key,
                                  bool shift) noexcept {
  switch (virtual_key) {
    case VK_ADD:
      return RouteTextCharacter(U'+');
    case VK_SUBTRACT:
      return RouteTextCharacter(U'-');
    case VK_MULTIPLY:
      return RouteTextCharacter(U'*');
    case VK_DIVIDE:
      return RouteTextCharacter(U'/');
    case VK_DECIMAL:
      return RouteTextCharacter(U'.');
    case VK_OEM_MINUS:
      return RouteTextCharacter(shift ? U'_' : U'-');
    case VK_OEM_PLUS:
      return RouteTextCharacter(shift ? U'+' : U'=');
    case VK_OEM_PERIOD:
      return RouteTextCharacter(shift ? U'>' : U'.');
    case VK_OEM_3:
      return RouteTextCharacter(shift ? U'~' : U'`');
    case VK_OEM_4:
      return RouteTextCharacter(shift ? U'{' : U'[');
    case VK_OEM_5:
      return RouteTextCharacter(shift ? U'|' : U'\\');
    case VK_OEM_6:
      return RouteTextCharacter(shift ? U'}' : U']');
    case VK_OEM_7:
      return RouteTextCharacter(shift ? U'"' : U'\'');
    default:
      return PassThrough();
  }
}

}  // namespace

KeyRoute RouteKey(const KeyRoutingInput& input) noexcept {
  if (input.control || input.alt || input.windows) {
    return input.active_composition
               ? KeyRoute{KeyRouteKind::Reset, U'\0'}
               : PassThrough();
  }

  if (IsResetKey(input.virtual_key)) {
    return input.active_composition
               ? KeyRoute{KeyRouteKind::Reset, U'\0'}
               : PassThrough();
  }

  if (input.virtual_key == VK_BACK) {
    return input.active_composition
               ? KeyRoute{KeyRouteKind::Backspace, U'\0'}
               : PassThrough();
  }

  if (input.virtual_key >= static_cast<std::uint32_t>('A') &&
      input.virtual_key <= static_cast<std::uint32_t>('Z')) {
    const bool uppercase = input.shift != input.caps_lock;
    const char32_t offset = static_cast<char32_t>(
        input.virtual_key - static_cast<std::uint32_t>('A'));
    return {KeyRouteKind::Character,
            (uppercase ? U'A' : U'a') + offset};
  }

  if (input.virtual_key >= VK_NUMPAD0 && input.virtual_key <= VK_NUMPAD9) {
    const char32_t character =
        U'0' + static_cast<char32_t>(input.virtual_key - VK_NUMPAD0);
    return OwnedOnly(RouteTextCharacter(character),
                     input.active_composition);
  }

  if (input.virtual_key >= static_cast<std::uint32_t>('0') &&
      input.virtual_key <= static_cast<std::uint32_t>('9')) {
    const char32_t character =
        input.shift ? ShiftedDigit(input.virtual_key)
                    : static_cast<char32_t>(input.virtual_key);
    return OwnedOnly(RouteTextCharacter(character),
                     input.active_composition);
  }

  const KeyRoute boundary = RouteBoundary(input.virtual_key, input.shift);
  if (boundary.kind != KeyRouteKind::PassThrough) {
    return OwnedOnly(boundary, input.active_composition);
  }

  const KeyRoute technical = RouteTechnical(input.virtual_key, input.shift);
  if (technical.kind != KeyRouteKind::PassThrough) {
    return OwnedOnly(technical, input.active_composition);
  }

  return PassThrough();
}

}  // namespace keyina::tsf
