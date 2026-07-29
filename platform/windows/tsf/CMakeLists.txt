add_library(keyina_tsf_support STATIC
  src/edit_translator.cpp
  src/key_router.cpp
)
add_library(keyina::tsf_support ALIAS keyina_tsf_support)

target_compile_features(keyina_tsf_support PUBLIC cxx_std_20)
target_link_libraries(keyina_tsf_support PUBLIC keyina::core)
target_include_directories(keyina_tsf_support PUBLIC
  "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
)
keyina_configure_target(keyina_tsf_support)

add_library(KeyinaTsf SHARED
  KeyinaTsf.def
  src/dll_entry.cpp
  src/text_service.cpp
)

target_compile_features(KeyinaTsf PRIVATE cxx_std_20)
target_compile_definitions(KeyinaTsf PRIVATE
  UNICODE
  _UNICODE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
)
target_include_directories(KeyinaTsf PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/include"
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
)
target_link_libraries(KeyinaTsf PRIVATE
  keyina::core
  advapi32
  ole32
  uuid
)
keyina_configure_target(KeyinaTsf)
set_target_properties(KeyinaTsf PROPERTIES
  PREFIX ""
  OUTPUT_NAME "KeyinaTsf"
)
