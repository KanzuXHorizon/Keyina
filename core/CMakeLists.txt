add_library(keyina_core STATIC
  src/context_guard.cpp
  src/vietnamese.cpp
)
add_library(keyina::core ALIAS keyina_core)

target_compile_features(keyina_core PUBLIC cxx_std_20)
target_include_directories(keyina_core PUBLIC
  "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
  "$<INSTALL_INTERFACE:include>"
)
keyina_configure_target(keyina_core)
