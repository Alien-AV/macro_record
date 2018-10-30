cmake_minimum_required(VERSION 3.10)

set(CMAKE_TOOLCHAIN_FILE "C:/Users/Alien_AV/Desktop/Programing/git/vcpkg/scripts/buildsystems/vcpkg.cmake")

project(InjectAndCaptureDll)

set(CMAKE_CXX_STANDARD 11)


add_definitions(-D_WINDLL -D_UNICODE -DUNICODE -DPROTOBUF_USE_DLLS -D_WINDOWS -D_USRDLL -DINJECTANDCAPTUREDLL_EXPORTS)

find_package(protobuf CONFIG REQUIRED)

include_directories(.)
include_directories(Capture)
include_directories(Common)
include_directories(Inject)

add_library(InjectAndCaptureDll
        Capture/WindowForCaptureEvents.cpp
        Common/DeserializeEvent.cpp
        Common/Event.cpp
        Common/Event.h
        Common/KeyboardEvent.cpp
        Common/KeyboardEvent.h
        Common/MouseEvent.cpp
        Common/MouseEvent.h
        Inject/InjectInput.cpp
        Inject/InjectInput.h
        dllmain.cpp
        InjectAndCaptureCAdaptor.cpp
        InjectAndCaptureDll.h
        Common/protobuf/cpp/Events.pb.h
        Common/protobuf/cpp/Events.pb.cc
        stdafx.cpp
        stdafx.h
        targetver.h)

target_link_libraries(InjectAndCaptureDll PRIVATE protobuf::libprotoc protobuf::libprotobuf protobuf::libprotobuf-lite)
