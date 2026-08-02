add_executable(netbox_server_tests
  test_main.cc
  session_manager_test.cc
)

target_link_libraries(netbox_server_tests PRIVATE netbox_server)
target_include_directories(netbox_server_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

add_test(NAME netbox_server_tests COMMAND netbox_server_tests)
