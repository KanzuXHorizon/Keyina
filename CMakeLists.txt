cmake_minimum_required(VERSION 3.25)

project(Keyina VERSION 0.1.0 LANGUAGES CXX)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
include(KeyinaCompilerOptions)

enable_testing()

add_subdirectory(core)
add_subdirectory(tests)
