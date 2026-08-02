cmake_minimum_required(VERSION 3.16)
project(netbox_api CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(NETBOX_API_BUILD_TESTS "Build netbox-api tests" ON)

# netbox-api sits directly on top of netbox-server (SessionManager /
# RuntimeRegistry / StreamRegistry / PlayerRegistry / NetBoxEventBus). It
# never depends on Xenia or netbox-streaming headers directly - only
# through netbox-server's IRuntimeHandle/IStreamHandle seams, same as
# netbox-server itself.
if(NOT TARGET netbox_server)
  set(NETBOX_SERVER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
  add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../netbox-server
                  ${CMAKE_CURRENT_BINARY_DIR}/netbox-server-build)
endif()

add_library(netbox_api STATIC
  src/api_gateway.cc
  src/websocket_gateway.cc
)

target_include_directories(netbox_api PUBLIC
  ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_link_libraries(netbox_api PUBLIC netbox_server)

if(NETBOX_API_BUILD_TESTS)
  enable_testing()
  add_subdirectory(tests)
endif()
