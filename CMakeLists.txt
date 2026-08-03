cmake_minimum_required(VERSION 3.25)

set(KEYINA_VERSION "0.1.8" CACHE STRING "Keyina semantic version")
project(Keyina VERSION "${KEYINA_VERSION}" LANGUAGES CXX)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(KeyinaCompilerOptions)

option(KEYINA_BUILD_BENCHMARKS "Build native latency benchmarks" ON)
option(KEYINA_BUILD_TSF "Build the optional legacy Windows TSF backend" OFF)

include(CTest)

add_subdirectory(core)
if(WIN32)
  add_subdirectory(platform/windows/input)
  add_subdirectory(platform/windows/hook)
  if(KEYINA_BUILD_TSF)
    add_subdirectory(platform/windows/tsf)
  endif()
endif()
add_subdirectory(tests)
if(KEYINA_BUILD_BENCHMARKS)
  add_subdirectory(benchmarks)
endif()
