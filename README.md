[![Build status](https://ci.appveyor.com/api/projects/status/dbelbyyagqukwslb?svg=true)](https://ci.appveyor.com/project/Alien-AV/macro-record)
# Requirements
Use [vcpkg](https://github.com/Microsoft/vcpkg/) to install protobuf libraries.
Run commands from "Quick Start" section in vcpkg readme. Then install protobuf like so:
```
vcpkg install protobuf:x64-windows-static
vcpkg install protobuf:x86-windows-static
```
# Building
Either open the `macro_record.sln` file in VS2017 and press Build,
Or, in command-line, cd to the repository, and run (you'll need a [NuGet CLI](https://www.nuget.org/downloads) for this):
```
nuget restore
msbuild
```
# Projects
## InjectAndCaptureDll
The DLL is the C++ code which does the injecting and capturing of input.
It exposes an API which is used by the GUI part, they communicate using [protobuf](https://developers.google.com/protocol-buffers/).
## InjectAndCaptureDllTest
Unit tests for the DLL.
## MacroRecorderGUI
C# WPF project, calls InjectAndCaptureDll for the actual work.
## macro_record
C++ console executable that's using the DLL.
## Common
The files common to the C++ and C# implementation, such as the protobuf definitions, protobuf-generated code, and the status codes enum.