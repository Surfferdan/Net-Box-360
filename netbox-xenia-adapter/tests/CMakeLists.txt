add_executable(netbox_xenia_adapter_tests
  test_main.cc
  xenia_runtime_handle_test.cc
  xenia_event_bridge_test.cc
  session_lifecycle_test.cc
)

target_link_libraries(netbox_xenia_adapter_tests PRIVATE netbox_xenia_adapter)
target_include_directories(netbox_xenia_adapter_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

add_test(NAME netbox_xenia_adapter_tests COMMAND netbox_xenia_adapter_tests)
