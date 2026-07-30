add_library(KeyinaEngine SHARED
  engine_bridge.cpp
  engine_bridge.def
)

target_compile_features(KeyinaEngine PRIVATE cxx_std_20)
target_link_libraries(KeyinaEngine PRIVATE keyina::core)
target_compile_definitions(KeyinaEngine PRIVATE
  UNICODE
  _UNICODE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
)
set_target_properties(KeyinaEngine PROPERTIES
  OUTPUT_NAME "KeyinaEngine"
)
keyina_configure_target(KeyinaEngine)
