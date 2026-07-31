add_library(keyina_windows_input STATIC
  input_injection.cpp
  native_latency_histogram.cpp
  pointer_input.cpp
  resident_input_controller.cpp
  runtime_hotkeys.cpp
  runtime_profile.cpp
  runtime_snippet_matcher.cpp
  runtime_snippet_profile.cpp
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

add_executable(KeyinaInput WIN32
  native_resident.cpp
  win32_input_runtime.cpp
)
target_compile_features(KeyinaInput PRIVATE cxx_std_20)
target_compile_definitions(KeyinaInput PRIVATE
  UNICODE
  _UNICODE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
)
target_link_libraries(KeyinaInput PRIVATE
  keyina::windows_input
  psapi
  user32
)
set_target_properties(KeyinaInput PROPERTIES
  OUTPUT_NAME "KeyinaInput"
)
keyina_configure_target(KeyinaInput)

add_test(NAME keyina.windows.input_self_test
  COMMAND KeyinaInput --self-test)
set_tests_properties(keyina.windows.input_self_test PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "keyina_input_ready"
  TIMEOUT 10
)

add_test(NAME keyina.windows.input_resource
  COMMAND KeyinaInput --resource-self-test)
set_tests_properties(keyina.windows.input_resource PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "\\\"budget_pass\\\":true"
  TIMEOUT 15
)

add_test(NAME keyina.windows.input_typing
  COMMAND KeyinaInput --typing-self-test)
set_tests_properties(keyina.windows.input_typing PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "typing_self_test_pass"
  TIMEOUT 10
)

add_test(NAME keyina.windows.input_clipboard_typing
  COMMAND KeyinaInput --clipboard-typing-self-test)
set_tests_properties(keyina.windows.input_clipboard_typing PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "clipboard_typing_self_test_pass"
  TIMEOUT 10
)

add_test(NAME keyina.windows.input_tray_resource
  COMMAND KeyinaInput --tray-resource-self-test)
set_tests_properties(keyina.windows.input_tray_resource PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "\\\"tray_enabled\\\":true.*\\\"budget_pass\\\":true"
  TIMEOUT 15
)

add_test(NAME keyina.windows.input_profile_reload
  COMMAND KeyinaInput --profile-reload-self-test)
set_tests_properties(keyina.windows.input_profile_reload PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "profile_reload_self_test_pass"
  TIMEOUT 10
)

add_test(NAME keyina.windows.input_callback_latency
  COMMAND KeyinaInput --callback-latency-self-test)
set_tests_properties(keyina.windows.input_callback_latency PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "callback_latency_self_test_pass"
  TIMEOUT 20
)

add_test(NAME keyina.windows.input_transform_callback_latency
  COMMAND KeyinaInput --transform-callback-latency-self-test)
set_tests_properties(keyina.windows.input_transform_callback_latency PROPERTIES
  LABELS integration
  PASS_REGULAR_EXPRESSION "transform_callback_latency_self_test_pass"
  TIMEOUT 30
)
