nuget restore
vcpkg install protobuf:%PLATFORM%-windows-static
cd c:\tools\vcpkg
vcpkg integrate install