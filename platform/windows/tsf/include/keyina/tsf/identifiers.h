#pragma once

#include <guiddef.h>

#include <cstdint>

namespace keyina::tsf {

inline constexpr CLSID kTextServiceClsid = {
    0xD66D2599,
    0x6B75,
    0x4AFF,
    {0x95, 0xB3, 0x47, 0x6C, 0x31, 0x0C, 0xDE, 0x70},
};

inline constexpr GUID kVietnameseProfileGuid = {
    0x06BA433A,
    0x9594,
    0x48CD,
    {0xAE, 0x85, 0x32, 0x64, 0x2A, 0x1D, 0xDB, 0x16},
};

#if defined(KEYINA_TSF_TEST_HOOKS)
// Local integration hosts dispatch ITfKeyEventSink calls directly because they
// do not own a profile-manager-assigned TIP client ID. Production builds do not
// compile this hook or the corresponding activation path.
inline constexpr std::uint32_t kManualKeyDispatchForTests = 0x80000000U;
#endif

}  // namespace keyina::tsf
