# Recording boundaries

Ctrl+Q starts a capture session with a shortcut drain. Q and Ctrl transitions that
belong to keys held at that session's native boundary are suppressed until their
release. Other input passes immediately. A released key can be used again while
another command key is still draining. Suppressed event delays accumulate onto
the next accepted event; the protobuf schema and `.macro` format are unchanged.
Button starts do not enable this filter. `MOD_NOREPEAT` prevents a held shortcut
from repeatedly issuing UI commands, and duplicate starts cannot clear a live macro.

## Stop-command input

Each session snapshots the stop gestures that were actually registered: Ctrl+W
and the effective emergency shortcut. Native capture provisionally holds fresh,
otherwise-unused modifier presses and a possible registered shortcut trigger.
Only a stop request carrying that shortcut's identity and original `WM_HOTKEY`
timestamp confirms that this input belonged to the recorder command. This also
handles a command delivered before its raw trigger. Ordinary/controller stops
flush provisional input unchanged. No editor cleanup or file-load trimming runs.

Ordinary pointer motion can occur between command keys, including before the raw
trigger arrives. It remains provisional alongside those keys. On confirmation,
only the command's otherwise-unused keys are omitted; all motion samples retain
their order, coordinates, flags and metadata. Omitted key delays accumulate onto
the next retained event, preserving its elapsed capture time. Both accumulation
and application of that delay are checked for overflow.

Button transitions, wheel input, drags, other typing and opaque input cancel the
candidate and publish its original sequence. This applies after a possible trigger
too. Mouse buttons are tracked through idle periods, so a drag begun before capture
cannot make a fresh Ctrl press look unused. Held-at-start/previously-used modifiers
and their repeats/releases remain genuine. A release followed by a new press is
not an auto-repeat. Thus Ctrl used for a drag can truthfully remain incomplete if
it is still held when recording stops; no release is invented. A different,
unregistered, stale-session or older-timestamp command cannot claim the candidate.
There is no time-based guess about how long a command chord takes.

### Provisional storage and failure

The capture filter holds at most 256 event objects in RAM. Longer candidates spill
in full 256-record batches to one uniquely named temporary file per candidate.
This preserves every high-rate sample and exact keyboard/mouse payload; there is
no movement coalescing or per-sample file write. The collector FIFO also caps its
queued packets at 256 and applies backpressure during replay. Already-published
input is never edited or retrospectively trimmed.

Spilling is necessary because an ordinary stop or later modified interaction must
be able to recover the exact prefix, whereas a confirmed command must omit only
its keys. Publishing either interpretation early is irreversible at the callback
boundary. Unlimited RAM would grow with mouse polling rate and how long Ctrl is
held. The temporary spool instead caps provisional record storage at 64 MiB
(including the current in-memory record batch), sufficient for minutes at 8 kHz,
while keeping RAM fixed. At the cap, or on file creation/write/seek/read failure,
the session ends with an explicit Failed boundary. Its partial accepted input can
remain visible, but it cannot be reported as a successful complete capture. No
command keys are flushed as an overflow fallback and no samples are silently
dropped from a successful capture.

The spool contains only the unresolved candidate, in the current user's temporary
directory. It is exclusively opened with Windows delete-on-close before any input
is written. Normal stop, confirmed stop, cancellation by genuine input, failure,
restart and destruction all close it; the OS also closes the handle on process
termination. Spool paths and contents are not saved to the library or macro format.
Creation failure removes the reserved empty filename. Deletion is ordinary file
deletion, not a secure-erasure guarantee against disk forensics or power failure.
Tests verify unique names, denied concurrent opens, batched writes, exact replay,
resource limits, and the absence of files after all normal/error cleanup paths.

## Native ordering

The native recorder registers Raw Input once at initialization and unregisters on
shutdown. While idle it retains virtual-key held bits and the start chord's release
bits, not events or typed text. Idle mouse input retains only button-held bits. This background input
registration is the cost of keeping key state consistent with the raw queue.
Ctrl/Alt extended flags and Shift scan codes identify their sides before tracking
and serialization.

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
cursor-position event; stopped follows the accepted captured tail. The collector wakes
on a condition variable and drains that FIFO. Stop requests retain captured input
apart from confirmed provisional stop-command input described above. Shutdown joins both native threads before releasing the
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
- Mouse-held state is likewise per button, not per physical mouse. Buttons already
  held before Raw Input registration have no observed down transition.
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
- Capture regression tests include 262,145-sample motion, interleaved repeats,
  both command/raw delivery orders, timestamp wrap, real temporary-file lifecycle,
  injected storage faults and checked delay overflow. Managed capture tests verify
  that a failure after readiness faults its session and cannot affect a restart.

Run the Release solution build with `VCPKG_MAX_CONCURRENCY=4`, then
`x64\Release\RecordPlaybackDLLTest.exe` and
`dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Release -p:Platform=x64`.

Stop-motion verification (2026-09-20): full x64 Debug and Release solution builds
pass with VS18 MSBuild; each configuration passes 82 native and 522 managed tests,
none skipped. The existing MSB3851 native/managed Windows SDK mismatch warning
remains. Verification used fake input, temporary files and hidden compiled WinUI
controls only. Physical Raw Input/hotkey delivery and live-device throughput were
not exercised. Provisional input is delivered after resolution; a large resolved
candidate can delay capture-thread processing while the collector drains it.

Ordering references: [GetMessage](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmessage),
[Raw Input](https://learn.microsoft.com/windows/win32/inputdev/about-raw-input), and
[WM_INPUT cleanup](https://learn.microsoft.com/windows/win32/inputdev/wm-input).
