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

These tests are noninteractive: native playback tests use fake injection sinks and constructed input, and managed lifecycle tests use fake engines and temporary stores. A separate diagnostic constructs compiled UI controls in invisible windows without production startup, hotkey registration, capture, or activation. Tests do not show the app or inject keyboard/mouse input. Native ABI rejection tests only submit invalid schedules.

The native test project compiles the production C++ model/playback sources into its executable, so owning C++ objects stay within one static CRT. Its DLL reference is build-only: ABI tests load that DLL explicitly and pass only borrowed byte buffers and scalar values. Debug selects Debug vcpkg libraries and `/MTd`; Release selects Release libraries and `/MT`. To verify Debug compilation without executing a Debug test process, build the solution with `/p:Configuration=Debug /p:Platform=x64`.

Playback owns one cancellable session. Short native looping macros have a minimum 5 ms pass interval, and long zero-delay event lists yield after bounded bursts. Modifier keys are released once at session start for playback-hotkey compatibility, except for wait-only schedules; completion/abort releases keys and buttons held by that session. Playback uses a cloned event snapshot, including speed-adjusted delays, so the saved recording is not modified. Finite repeats and repeat-until-stopped share the cancellable countdown and emergency stop. Physical modifier interference during playback remains a separate limitation.

## Recording workspace

The library, two-column action editor, visual preview, and compact run controller
follow the original polished design. Preview stays inside the app; **Play** sends
real input to other applications after its countdown. Exact raw events and microsecond timing remain available
behind the simplified action list. Relative mouse traces are device counts, not
reconstructed screen positions.

Named recordings and drafts are stored under `%LOCALAPPDATA%/MacroRecorder/Recordings`
with atomic writes and a previous-copy recovery file. The library accepts macros
up to 64 MB. Legacy `.macro` files remain readable; recordings with pointer-origin
metadata or conditional waits use versioned contents under the same `.macro` extension, which older
players reject. Unsaved
changes and failures are shown explicitly, and navigation/close waits for saves.
An untouched initial document is not saved as an empty library entry.

Each library card has an explicit **Play** action using its saved settings and
adjacent playback options that do not start a run. Opening the card edits the
recording; selecting its checkbox never starts playback or changes what opening
does. **Compact list** offers the same commands without large thumbnails.

Use the always-visible checkboxes to select recordings, then **Delete selected**
in the contextual toolbar. A card's options menu can delete just that recording.
Space toggles a focused checkbox; Escape clears the batch without opening a card.
**Select all visible** covers the current search results. Hidden selections are
discarded when the search changes. Deletion moves library copies into persistent
**Local trash**, with last-batch Undo and restore after restart. Imported/exported
source files are untouched. Dirty documents are saved first; failures and live
recording/playback targets are protected and reported rather than discarded.

New captures keep starting pointer positions as metadata, not visible input
actions. Playback options offer **Recorded starting point** or **Current pointer**;
the latter samples the pointer after countdown and reuses that run origin for
every repeat. Appended captures retain their own origin boundaries. Raw relative
movements remain device counts, while actual absolute positions are translated
within their screen coordinate frame. Invalid or out-of-bounds positions stop
playback before input is sent.

Legacy recordings are not guessed or trimmed. Playback options can explicitly
adopt an eligible first absolute Move as origin, with undo and a saved pre-adoption
recovery copy. The document menu offers explicit **Export legacy .macro**: it
materializes recorded setup moves for old players and leaves the library document
unchanged. Ordinary export retains origin metadata and recovery information;
per-recording playback preferences stay local. Legacy export rejects conditional
waits and standalone Delay actions rather than dropping them.

### Editing actions

The sequence and selected action details occupy independent bounded panes. At
narrow widths, **Sequence** and **Details** switch views without dropping the
selection or drafts; F6 switches keyboard focus between the panes. **Exact input**
opens a dedicated view with its own event
list; returning to the action retains unfinished raw edits. Raw changes remain
explicit: **Apply** or **Discard** them before running or leaving the recording.

The primary **Add** menu offers **Click**, **Shortcut**, **Pointer movement**, and
**Wait until**. New actions are inserted after the selected action (at the
beginning when nothing is selected), and Undo restores the previous input.
Clicks use the current pointer position; shortcuts contain balanced key presses
and releases, not inferred text. Pointer movement is one explicit report in
screen pixels or relative device counts, not an invented interpolated path.
Exact mouse and keyboard event construction is under **Advanced**.

**Pause before** is a fixed delay; **Execution time** is time within an action;
**Wait until** observes a condition whose duration is not known in advance.
Precise raw values and technical diagnostics remain available in Exact input.

### Wait until

Use **Add → Wait until…** to insert a condition after the selected action, or
explicitly replace an action's fixed delay with a condition. The reusable
`MacroRecorder.Waiting` library implements window existence/visibility/foreground/absence,
pixel colors, accessibility text, local OCR regions, and read-only scalar memory
comparisons with explicit pointer chains. It has no WinUI dependency.

Memory observation requires local opt-in; importing a macro cannot enable it.
It binds one process instance, performs exact bounded reads, and stops after
process exit or permission revocation. It does not write, scan, discover chains,
inject, or elevate. OCR uses a local Tesseract 5 executable and installed language
data, with no playback downloads or cloud upload. Missing local OCR components
produce an explicit unavailable result. See the [current provider and deployment
contract](docs/wait-library-contract.md) for configuration and resource limits.

The inspector supports condition edits and Undo. The default is a condition that
is true for 200 ms, with a 30-second timeout. Advanced triggers can require a
transition, a change from the runtime baseline, or a new matching window.
**Test condition** explicitly observes for at most five seconds; opening a
recording or using Preview does not observe the desktop. Preview stops at waits
until **Simulate satisfied / Next**. Real playback stops if a condition times out
or fails; waiting never focuses a target window.

Window/pixel-only wait files use document version 3 under the same `.macro` extension.
Accessibility, OCR, memory and standalone Delay actions require version 4, so
older players reject unsupported content. Legacy raw and version-2/3 imports
retain their existing meaning and unknown data.
Unsupported or malformed conditions are rejected, and playback validates the
whole schedule before sending input. Conditional waits require released recorded keys/buttons;
fixed Delay events preserve held input and remain cancellable, including at zero duration.
Pixel targets do not move when playback uses the current pointer as its origin.
See [conditional-wait semantics and limitations](docs/state-waits-proposal.md).

### Recording and playback controls

Record and Ctrl+Q start a new automatically named recording after the configured
countdown. Play and Ctrl+E start the selected recording using its saved settings.
Neither requires a setup dialog. Adjacent options controls change settings without
starting a run: recording defaults are shared, while playback speed, repeat and
countdown are remembered per recording across app restarts. The playback summary
shows the active settings, including repeat-until-stopped. Preview retains the
original captured timing and never sends input. Ctrl+W stops recording.
Emergency stop defaults to Ctrl+R; Settings offers alternate registered shortcuts
for the current session. A registered emergency shortcut is required before
recording/playback starts. Use the recording stop shortcut to avoid capturing
a controller mouse click.

See `docs/direct-run-controls.md` for direct-run behavior and safety,
`docs/action-editor.md` for editing semantics and manual smoke checks, and
`docs/design-implementation.md` for the visual specification and verification scope.
See `docs/library-origin-and-branding.md` for combined regression checks and
`docs/wait-library-contract.md` for current wait providers and `docs/state-waits-proposal.md`
for the historical design and remaining optional capabilities.

## Unpackaged distribution

Build the complete solution for Release first, then publish a self-contained x64
directory. Publishing requires the matching native DLL under `x64/Release` and
fails if it is missing; a managed-only build is not a complete distribution.

```powershell
dotnet publish MacroRecorderGUI/MacroRecorderGUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o x64/Release/publish
```

Verify packaging without launching the application:

```powershell
./scripts/Test-Publish.ps1 -Configuration Release
```

This checks missing-native failures, native DLL and compiled UI resource hashes,
and self-contained runtime configuration and files in an isolated directory under
`artifacts`.

The publish directory can be copied directly to another machine. To produce the optional Inno Setup installer:

```powershell
iscc inno-setup-script.iss -DMyPublishDir=x64/Release/publish
```

## Projects

- `MacroRecorderGUI`: .NET 10 WinUI 3 desktop UI.
- `MacroRecorder.Waiting`: reusable Windows wait runner, schemas, validators, and read-only providers; no WinUI dependency.
- `MacroRecorderGUITests`: managed behavior tests.
- `RecordPlaybackDLL`: native recording and playback implementation.
- `RecordPlaybackDLLTest`: native tests.
- `Common`: shared protobuf definitions/generated code and status values.

## Protobuf contract

Do not regenerate the checked-in C# and C++ protobuf sources unless `Events.proto` intentionally changes. Regeneration must update both languages from the same schema and must preserve wire compatibility.
