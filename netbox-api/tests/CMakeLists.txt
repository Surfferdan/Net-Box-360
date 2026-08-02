add_executable(netbox_api_tests
  test_main.cc
  api_gateway_test.cc
  websocket_gateway_test.cc
)

target_link_libraries(netbox_api_tests PRIVATE netbox_api)
target_include_directories(netbox_api_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

add_test(NAME netbox_api_tests COMMAND netbox_api_tests)
