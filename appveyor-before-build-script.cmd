nuget restore
cd c:\tools\vcpkg
git pull
.\bootstrap-vcpkg.bat
vcpkg install protobuf:%PLATFORM%-windows-static
vcpkg integrate install