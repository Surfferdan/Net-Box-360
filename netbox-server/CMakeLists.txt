cmake_minimum_required(VERSION 3.16)
project(netbox_server CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(NETBOX_SERVER_BUILD_TESTS "Build netbox-server tests" ON)

# Pure orchestration layer - no dependency on Xenia or netbox-streaming at
# build time. IRuntimeHandle/IStreamHandle are the only seams to either; a
# future bridge project would implement them against a real Xenia process /
# netbox-streaming CloudMorphBackend.
add_library(netbox_server STATIC
  src/types.cc
  src/events.cc
  src/session/session.cc
  src/session/session_manager.cc
  src/runtime/runtime_registry.cc
  src/stream/stream_registry.cc
  src/player/player_registry.cc
)

target_include_directories(netbox_server PUBLIC
  ${CMAKE_CURRENT_SOURCE_DIR}/include
)

if(NETBOX_SERVER_BUILD_TESTS)
  enable_testing()
  add_subdirectory(tests)
endif()
