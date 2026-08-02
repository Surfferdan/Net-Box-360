add_executable(netbox_streaming_tests
  test_main.cc
  cloudmorph_backend_test.cc
)

target_link_libraries(netbox_streaming_tests PRIVATE netbox_streaming)
target_include_directories(netbox_streaming_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

add_test(NAME netbox_streaming_tests COMMAND netbox_streaming_tests)
