add_executable(keyina_bench
  engine_benchmark.cpp
)

target_link_libraries(keyina_bench PRIVATE keyina::core)
target_compile_features(keyina_bench PRIVATE cxx_std_20)
keyina_configure_target(keyina_bench)
