# Recording boundaries

Ctrl+Q starts a capture session with a shortcut drain. Q and Ctrl transitions that
belong to keys held at that session's native boundary are suppressed until their
release. Other input passes immediately. A released key can be used again while
another command key is still draining. Suppressed event delays accumulate onto
the next accepted event; the protobuf schema and `.macro` format are unchanged.
Button starts do not enable this filter. `MOD_NOREPEAT` prevents a held shortcut
from repeatedly issuing UI commands, and duplicate starts cannot clear a live macro.

## Native ordering

The native recorder registers Raw Input once at initialization and unregisters on
shutdown. While idle it retains only Q/left-Ctrl/right-Ctrl held and release bits, not
events or typed text. Mouse input is discarded while idle. This background input
registration is the cost of keeping key state consistent with the raw queue.
Ctrl's raw extended flag identifies its side before tracking and serialization.

A posted start or stop can be retrieved before older hardware input. Each command
therefore has a fixed `MSG.time` cutoff. The capture thread consumes raw messages
through that cutoff, at most 256 per turn, servicing control messages between
batches. Newer input cannot keep extending the prefix; it stays queued for the
next session state. Every removed `WM_INPUT` is dispatched through the same
handler and reaches `DefWindowProc` for cleanup, including idle input and errors.
No `GetAsyncKeyState` sample is treated as an atomic queue snapshot, and there is
no timed grace period or sleep in the capture pipeline.

The held mask at start comes from consumed raw transitions. Started also carries
the chord-key releases observed during the preceding idle gap, including releases
followed by new presses. This compact history lets a rollover drain the original
press without suppressing a newly held press that has the same virtual key.
Started, input, and
stopped packets use one FIFO and carry a session ID. Started precedes the initial
cursor-position event; stopped follows the captured tail. The collector wakes
on a condition variable and drains that FIFO. Stop requests do not discard
already captured input. Shutdown joins both native threads before releasing the
managed callbacks. The native callback ABI changed and requires the matching DLL;
this does not change saved macro compatibility.

## UI delivery and content changes

Every recorded event carries its original session and macro/revision destination.
The ordered end notification posts a UI action behind that session's input
actions. Native `RecordingSession.Completion` alone does **not** imply that the UI
queue has run. Stop's optional delay adjustment runs in the end UI action and
includes the original macro's buffered tail and earlier sessions' pending input
for the same content revision. Pending delay adjustments have a stopping-session
ID cutoff, so a newer recording or another macro cannot join that adjustment.

Clear and replace increment a macro revision. During active capture they roll
the recording into a new native session with the same shortcut-drain state and a
fresh timing origin. The new boundary reconciles pending keys with the held mask
and idle-release history; it never rearms keys that have already drained.
Old native callbacks and already queued UI additions fail
the old revision check. Fresh input continues into the new content. Clearing
after Stop does not restart capture. Closing the capture target stops recording;
its remaining events cannot migrate to another tab.

## Limits and verification

- Windows message timestamps have millisecond resolution. Equal-timestamp input
  belongs to the prefix before the command, so start/stop is a queue-consumption
  boundary, not an exact physical hardware instant. Rapid commands remain FIFO.
- Held state is per virtual key, not per physical keyboard. Multiple keyboards
  holding the same sided key cannot be distinguished by the existing event format.
- Keys held before initial registration have no observed make. Hotkey captures
  suppress a chord-key orphan release until a corresponding down is observed.
  A make with no observed predecessor cannot be distinguished from an auto-repeat
  of a key held before initialization. Device removal or a desktop transition can
  also lose a release; no timer attempts to guess such missing transitions.
- The OS registration/message plumbing is build-checked but not exercised by these
  tests. Native tests use constructed raw structures and fake queue/state sinks;
  managed tests use the real session-aware RecordEngine with a fake transport and
  deferred UI queue. They create no windows, register no Raw Input devices, and
  inject no keyboard or mouse input.

Run the Release solution build with `VCPKG_MAX_CONCURRENCY=4`, then
`x64\Release\RecordPlaybackDLLTest.exe` and
`dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Release -p:Platform=x64`.

Ordering references: [GetMessage](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmessage),
[Raw Input](https://learn.microsoft.com/windows/win32/inputdev/about-raw-input), and
[WM_INPUT cleanup](https://learn.microsoft.com/windows/win32/inputdev/wm-input).
