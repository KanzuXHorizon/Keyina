cmake_minimum_required(VERSION 3.25)

project(Keyina VERSION 0.1.0 LANGUAGES CXX)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(KeyinaCompilerOptions)

option(KEYINA_BUILD_BENCHMARKS "Build native latency benchmarks" ON)

enable_testing()

add_subdirectory(core)
if(WIN32)
  add_subdirectory(platform/windows/tsf)
endif()
add_subdirectory(tests)
if(KEYINA_BUILD_BENCHMARKS)
  add_subdirectory(benchmarks)
endif()
