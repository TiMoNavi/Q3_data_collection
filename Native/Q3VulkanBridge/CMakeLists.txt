cmake_minimum_required(VERSION 3.22)
project(q3dc_vulkan_bridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT DEFINED UNITY_PLUGIN_API_DIR)
    message(FATAL_ERROR "UNITY_PLUGIN_API_DIR must point to Unity Editor/Data/PluginAPI")
endif()

add_library(q3dc_vulkan_bridge SHARED
    q3dc_vulkan_bridge.cpp
)

target_include_directories(q3dc_vulkan_bridge PRIVATE
    "${UNITY_PLUGIN_API_DIR}"
)

target_compile_definitions(q3dc_vulkan_bridge PRIVATE
    UNITY_ANDROID=1
)

target_link_libraries(q3dc_vulkan_bridge PRIVATE
    android
    log
    vulkan
)

