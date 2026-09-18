[![Build status](https://ci.appveyor.com/api/projects/status/dbelbyyagqukwslb?svg=true)](https://ci.appveyor.com/project/Alien-AV/macro-record)

# Macro Recorder

Macro Recorder is an x64 Windows desktop application with a C# WinUI 3 front end and a native C++ recording/playback DLL. The managed/native byte boundary is defined by `Common/protobuf/Events.proto`.

## Requirements

- Windows 10 version 1809 or newer.
- .NET SDK 10.0.400 or a compatible later 10.0 patch.
- Visual Studio 2026 with the Windows application development and Desktop development with C++ workloads.
- The vcpkg client bundled with Visual Studio 2026.

The checked-in `vcpkg.json` manifest pins the native protobuf runtime to a version compatible with the checked-in generated sources. A full solution restore handles both NuGet and vcpkg. To restore the native manifest separately from a Visual Studio developer shell, use the bundled client:

```powershell
$env:VCPKG_MAX_CONCURRENCY = '4'
& "${env:VCInstallDir}vcpkg\vcpkg.exe" install --triplet x64-windows-static
```

Setting `VCPKG_MAX_CONCURRENCY` to `4` matches CI and limits the first dependency build to four parallel compiler jobs.

The WinUI project uses Windows App SDK 2.4.0 and is configured as an unpackaged desktop application. It does not require Microsoft Store packaging or registration.

## Build and test

Restore and verify the managed projects:

```powershell
dotnet restore MacroRecorderGUI/MacroRecorderGUI.csproj --runtime win-x64
dotnet build MacroRecorderGUI/MacroRecorderGUI.csproj -c Debug -p:Platform=x64
dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Debug -p:Platform=x64
```

Build the complete mixed C#/C++ solution from a Visual Studio developer shell:

```powershell
$env:VCPKG_MAX_CONCURRENCY = '4'
msbuild macro_record.sln /restore /m /p:Configuration=Release /p:Platform=x64 /p:RestorePackagesConfig=true
```

MSBuild restores the same manifest automatically. The native DLL is copied into the GUI output when it is available under `x64/<Configuration>`.

Run both test suites after the Release build (also run by CI):

```powershell
& ./x64/Release/RecordPlaybackDLLTest.exe
dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Release -p:Platform=x64 --no-build
```

These tests are noninteractive: native playback tests use fake injection sinks and constructed input, and managed lifecycle tests use fake engines. They do not launch the app or inject keyboard/mouse input. Native ABI rejection tests only submit invalid schedules.

Playback owns one cancellable session. Short looping macros have a minimum 5 ms pass interval, and long zero-delay event lists yield after bounded bursts. Modifier keys are released once at session start for playback-hotkey compatibility; completion/abort releases keys and buttons held by that session. Looping uses the event snapshot captured when playback starts, even if the selected tab or macro is edited. Unchecking Loop finishes the current pass; Abort interrupts it immediately. Physical modifier interference during playback remains a separate limitation.

## Unpackaged distribution

Publish a self-contained x64 directory:

```powershell
dotnet publish MacroRecorderGUI/MacroRecorderGUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o x64/Release/publish
```

The publish directory can be copied directly to another machine. To produce the optional Inno Setup installer:

```powershell
iscc inno-setup-script.iss -DMyPublishDir=x64/Release/publish
```

## Projects

- `MacroRecorderGUI`: .NET 10 WinUI 3 desktop UI.
- `MacroRecorderGUITests`: managed behavior tests.
- `RecordPlaybackDLL`: native recording and playback implementation.
- `RecordPlaybackDLLTest`: native tests.
- `Common`: shared protobuf definitions/generated code and status values.

## Protobuf contract

Do not regenerate the checked-in C# and C++ protobuf sources unless `Events.proto` intentionally changes. Regeneration must update both languages from the same schema and must preserve wire compatibility.
