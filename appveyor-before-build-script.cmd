nuget restore
vcpkg install protobuf:%PLATFORM%-windows-static
vcpkg integrate install
cd Common\protobuf\
rmdir /s /q cpp csharp
mkdir cpp csharp
C:\Tools\vcpkg\installed\x64-windows-static\tools\protobuf\protoc --cpp_out=cpp --csharp_out=csharp Events.proto
