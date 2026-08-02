cmake_minimum_required(VERSION 3.16)
project(netbox_xenia_adapter CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(NETBOX_XENIA_ADAPTER_BUILD_TESTS "Build netbox-xenia-adapter tests" ON)

# This project is the adapter layer bridging netbox-server's runtime seam
# (IRuntimeHandle) to the existing Xenia Manager API (xenia api/XeniaManager.Api,
# a separate .NET project). It never recreates launcher/profile/save/
# achievement/config logic - it only calls into IXeniaManagerClient, a
# thin C++ interface representing the Xenia Manager API's HTTP surface, the
# same "define the seam, mock it for tests" approach used by netbox-api for
# netbox-server itself.
if(NOT TARGET netbox_server)
  set(NETBOX_SERVER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
  add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../netbox-server
                  ${CMAKE_CURRENT_BINARY_DIR}/netbox-server-build)
endif()

add_library(netbox_xenia_adapter STATIC
  src/xenia_manager_client.cc
  src/xenia_runtime_handle.cc
  src/xenia_event_bridge.cc
  src/xenia_profile_adapter.cc
  src/xenia_game_library_client.cc
)

target_include_directories(netbox_xenia_adapter PUBLIC
  ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_link_libraries(netbox_xenia_adapter PUBLIC netbox_server)

if(NETBOX_XENIA_ADAPTER_BUILD_TESTS)
  enable_testing()
  add_subdirectory(tests)
endif()
