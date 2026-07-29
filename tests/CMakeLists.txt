add_executable(keyina_tests
  context_guard_test.cpp
  engine_history_test.cpp
  engine_letter_test.cpp
  engine_tone_test.cpp
  golden_vectors_test.cpp
  invariant_test.cpp
  test_main.cpp
  vietnamese_test.cpp
)

target_compile_features(keyina_tests PRIVATE cxx_std_20)
target_link_libraries(keyina_tests PRIVATE keyina::core)
target_include_directories(keyina_tests PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}"
)
target_compile_definitions(keyina_tests PRIVATE
  KEYINA_TEST_DATA_DIR="${CMAKE_CURRENT_SOURCE_DIR}/data"
)
keyina_configure_target(keyina_tests)

add_test(NAME keyina.unit COMMAND keyina_tests)
set_tests_properties(keyina.unit PROPERTIES LABELS unit)
