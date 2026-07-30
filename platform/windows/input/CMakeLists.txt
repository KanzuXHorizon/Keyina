add_library(keyina_windows_input STATIC
  runtime_profile.cpp
)
add_library(keyina::windows_input ALIAS keyina_windows_input)

target_compile_features(keyina_windows_input PRIVATE cxx_std_20)
target_include_directories(keyina_windows_input PUBLIC
  "${CMAKE_CURRENT_SOURCE_DIR}/include"
)
target_compile_definitions(keyina_windows_input PRIVATE
  UNICODE
  _UNICODE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
)
target_link_libraries(keyina_windows_input PUBLIC keyina::core)
keyina_configure_target(keyina_windows_input)
