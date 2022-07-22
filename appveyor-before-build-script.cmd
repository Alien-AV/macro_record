nuget restore
vcpkg install protobuf:%PLATFORM%-windows-static
vcpkg integrate install
.\Common\protobuf\regenerate-protoc-events.bat