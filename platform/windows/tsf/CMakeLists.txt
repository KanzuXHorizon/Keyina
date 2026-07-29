add_library(keyina_tsf_support STATIC
  src/edit_translator.cpp
  src/ipc_protocol.cpp
  src/key_router.cpp
)
add_library(keyina::tsf_support ALIAS keyina_tsf_support)

target_compile_features(keyina_tsf_support PUBLIC cxx_std_20)
target_link_libraries(keyina_tsf_support PUBLIC keyina::core)
target_include_directories(keyina_tsf_support PUBLIC
  "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
)
keyina_configure_target(keyina_tsf_support)

function(keyina_add_tsf_dll target definition_file output_name)
  add_library(${target} SHARED
    ${definition_file}
    src/dll_entry.cpp
    src/external_edit_session.cpp
    src/key_edit_session.cpp
    src/text_service.cpp
  )

  target_compile_features(${target} PRIVATE cxx_std_20)
  target_compile_definitions(${target} PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
  )
  target_include_directories(${target} PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/include"
    "${CMAKE_CURRENT_SOURCE_DIR}/src"
  )
  target_link_libraries(${target} PRIVATE
    keyina::core
    keyina::tsf_support
    advapi32
    ole32
    oleaut32
    uuid
  )
  keyina_configure_target(${target})
  set_target_properties(${target} PROPERTIES
    PREFIX ""
    OUTPUT_NAME "${output_name}"
  )
endfunction()

keyina_add_tsf_dll(KeyinaTsf KeyinaTsf.def KeyinaTsf)

if(BUILD_TESTING)
  keyina_add_tsf_dll(KeyinaTsfTest KeyinaTsfTest.def KeyinaTsfTest)
  target_compile_definitions(KeyinaTsfTest PRIVATE KEYINA_TSF_TEST_HOOKS)
endif()
