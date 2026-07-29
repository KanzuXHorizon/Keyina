add_library(keyina_core INTERFACE)
add_library(keyina::core ALIAS keyina_core)

target_compile_features(keyina_core INTERFACE cxx_std_20)
target_include_directories(keyina_core INTERFACE
  "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
  "$<INSTALL_INTERFACE:include>"
)
