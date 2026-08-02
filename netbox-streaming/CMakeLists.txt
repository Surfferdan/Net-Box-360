cmake_minimum_required(VERSION 3.16)
project(netbox_streaming CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(NETBOX_STREAMING_BUILD_TESTS "Build netbox-streaming tests" ON)

# Path to the Xenia checkout's `src/` directory. netbox-streaming depends
# ONLY on a few header-only, dependency-free NetBox files under
# src/xenia/netbox/ (netbox_stream_backend.h, netbox_video.h,
# netbox_audio.h) for the IStreamBackend interface and VideoFrame/
# AudioPacket struct definitions. No Xenia .cc/.lib is compiled or linked
# by this project - this is a header-only interface dependency, not a
# library dependency, keeping this project and Xenia mutually
# streaming-backend-agnostic in both directions.
if(NOT DEFINED XENIA_SRC_DIR)
  set(XENIA_SRC_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../Xenia Rebuild folder/xenia-master/src")
endif()

add_library(netbox_streaming STATIC
  src/stream_health.cc
  src/cloudmorph_process.cc
  src/cloudmorph_backend.cc
)

target_include_directories(netbox_streaming
  PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    "${XENIA_SRC_DIR}"
)

if(WIN32)
  target_compile_definitions(netbox_streaming PUBLIC NOMINMAX WIN32_LEAN_AND_MEAN)
endif()

if(NETBOX_STREAMING_BUILD_TESTS)
  enable_testing()
  add_subdirectory(tests)
endif()
