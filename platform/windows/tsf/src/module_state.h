#pragma once

#include <windows.h>

namespace keyina::tsf {

void ModuleObjectCreated() noexcept;
void ModuleObjectDestroyed() noexcept;
void ModuleLock() noexcept;
void ModuleUnlock() noexcept;
[[nodiscard]] bool ModuleCanUnload() noexcept;
[[nodiscard]] HMODULE ModuleHandle() noexcept;
void SetModuleHandle(HMODULE module) noexcept;

}  // namespace keyina::tsf
