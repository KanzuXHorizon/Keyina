add_executable(keyina_tests
  context_guard_test.cpp
  engine_flexible_telex_test.cpp
  engine_history_test.cpp
  engine_letter_test.cpp
  engine_tone_test.cpp
  golden_vectors_test.cpp
  invariant_test.cpp
  test_main.cpp
  vietnamese_syllable_test.cpp
  vietnamese_test.cpp
)

target_compile_features(keyina_tests PRIVATE cxx_std_20)
target_link_libraries(keyina_tests PRIVATE keyina::core)
target_include_directories(keyina_tests PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}"
)
if(WIN32)
  target_sources(keyina_tests PRIVATE
    windows/edit_translator_test.cpp
    windows/ipc_protocol_test.cpp
    windows/key_router_test.cpp
  )
  target_link_libraries(keyina_tests PRIVATE keyina::tsf_support)

  add_executable(keyina_tsf_dll_smoke windows/dll_smoke_test.cpp)
  target_compile_features(keyina_tsf_dll_smoke PRIVATE cxx_std_20)
  target_include_directories(keyina_tsf_dll_smoke PRIVATE
    "${PROJECT_SOURCE_DIR}/platform/windows/tsf/include"
  )
  target_link_libraries(keyina_tsf_dll_smoke PRIVATE ole32 uuid)
  keyina_configure_target(keyina_tsf_dll_smoke)
  add_dependencies(keyina_tsf_dll_smoke KeyinaTsf)
  add_test(
    NAME keyina.windows.tsf_dll_smoke
    COMMAND keyina_tsf_dll_smoke "$<TARGET_FILE:KeyinaTsf>"
  )
  set_tests_properties(keyina.windows.tsf_dll_smoke PROPERTIES LABELS integration)

  add_executable(keyina_tsf_registration_probe windows/registration_probe.cpp)
  target_compile_features(keyina_tsf_registration_probe PRIVATE cxx_std_20)
  keyina_configure_target(keyina_tsf_registration_probe)
  add_dependencies(keyina_tsf_registration_probe KeyinaTsf)

  add_executable(keyina_pipe_client_test windows/pipe_client_test.cpp)
  target_compile_features(keyina_pipe_client_test PRIVATE cxx_std_20)
  target_link_libraries(keyina_pipe_client_test PRIVATE
    keyina::tsf_support
    ole32
    uuid
  )
  keyina_configure_target(keyina_pipe_client_test)
  add_test(NAME keyina.windows.pipe_client COMMAND keyina_pipe_client_test)
  set_tests_properties(keyina.windows.pipe_client PROPERTIES
    LABELS integration
    TIMEOUT 15
  )

  add_executable(keyina_tsf_integration
    windows/test_text_store.cpp
    windows/tsf_integration_test.cpp
  )
  target_compile_features(keyina_tsf_integration PRIVATE cxx_std_20)
  target_compile_definitions(keyina_tsf_integration PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    KEYINA_TSF_TEST_HOOKS
  )
  target_include_directories(keyina_tsf_integration PRIVATE
    "${PROJECT_SOURCE_DIR}/platform/windows/tsf/include"
  )
  target_link_libraries(keyina_tsf_integration PRIVATE
    keyina::tsf_support
    ole32
    oleaut32
    uuid
  )
  keyina_configure_target(keyina_tsf_integration)
  add_dependencies(keyina_tsf_integration KeyinaTsfTest)
  add_test(
    NAME keyina.windows.tsf_integration
    COMMAND keyina_tsf_integration "$<TARGET_FILE:KeyinaTsfTest>"
  )
  set_tests_properties(keyina.windows.tsf_integration PROPERTIES
    LABELS integration
    TIMEOUT 30
  )
endif()

target_compile_definitions(keyina_tests PRIVATE
  KEYINA_TEST_DATA_DIR="${CMAKE_CURRENT_SOURCE_DIR}/data"
)
keyina_configure_target(keyina_tests)

add_test(NAME keyina.unit COMMAND keyina_tests)
set_tests_properties(keyina.unit PROPERTIES LABELS unit)
