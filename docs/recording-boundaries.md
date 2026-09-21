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

### Capture delivery, storage and failure

The capture filter holds at most 256 event objects in RAM. Longer candidates spill
in full 256-record batches to one uniquely named temporary file per candidate.
This preserves every high-rate sample and exact keyboard/mouse payload; there is
no movement coalescing or per-sample file write. When a prefix resolves, the source
transfers its owning batch to the collector without reading the spool or invoking
callbacks. Raw input continues to be timestamped at the production adapter while
the collector reads and delivers that batch independently.

The FIFO appends incoming native events to its unconsumed tail in the same bounded,
batched storage. Reads and callbacks run outside its lock. There is no per-event
callback backpressure on successful capture, even when a long Ctrl-motion prefix
becomes genuine through release or typing. This prevents callback replay time from
stretching the next raw event's delay and bunching subsequent queued input. The
production adapter and clock are shared with the fake-source pipeline regression.
The FIFO caps normal metadata entries at 256, plus one reserved failure slot.
Already-published input is never edited or retrospectively trimmed.

Spilling is necessary because an ordinary stop or later modified interaction must
be able to recover the exact prefix, whereas a confirmed command must omit only
its keys. Publishing either interpretation early is irreversible at the callback
boundary. Unlimited RAM would grow with mouse polling rate and how long Ctrl is
held. One atomic 64 MiB budget covers all provisional, queued and collector-owned
record storage, including in-memory record batches. Moving a batch does not reset
its budget or allocate a second independent allowance. Each batch holds at most
256 event objects in RAM; the FIFO's metadata bound also limits the number of
resident batches and handles. At the cap, or on file creation/write/seek/read failure,
the session ends with an explicit Failed boundary. Its partial accepted input can
remain visible, but it cannot be reported as a successful complete capture. No
command keys are flushed as an overflow fallback and no samples are silently
dropped from a successful capture.

Spools contain only undelivered capture data, in the current user's temporary
directory. Each is exclusively opened with Windows delete-on-close before input
is written. A resolved spool stays owned by its FIFO packet or collector until
delivery finishes; its source can already be capturing a later session. Normal
and confirmed stop completion, failure/discard and destruction close the storage.
Shutdown ends the source, drains the collector, then joins it before releasing
callbacks. The OS also closes the handles on process
termination. Spool paths and contents are not saved to the library or macro format.
Creation failure removes the reserved empty filename. Deletion is ordinary file
deletion, not a secure-erasure guarantee against disk forensics or power failure.
Tests verify unique names, denied concurrent opens, batched writes, exact replay,
resource limits, and the absence of files after all normal/error cleanup paths.

Started, data and Stopped remain ordered in one FIFO. A collector-side read or
delay-overflow error closes that batch, reports Failed for its owning session,
and retains that session ID in an atomic mailbox before invoking the Failed
callback. A best-effort thread message only wakes the source: a rejected post
cannot lose the failure or leave the native session rejecting a client restart.
The source consumes the mailbox before raw input, start/stop, pump waits and
shutdown. Collector callbacks never mutate Stream state. Stale data or success
for the failed/ended session are suppressed, including after a newer Started.
The source ignores an old failure if a newer session is active. One FIFO collector
publishes failures in source order, so the fixed-size mailbox may replace an older
failure only after that older source session has ended. Both failure metadata and
the mailbox are independent of the data-byte budget.

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
  deferred UI queue. Capture tests register no Raw Input devices and inject no
  keyboard or mouse input; compiled WinUI checks use hidden controls and fake engines.
- Capture regression tests include 262,145-sample motion, interleaved repeats,
  both command/raw delivery orders, timestamp wrap, real temporary-file lifecycle,
  injected storage faults and checked delay overflow. Managed capture tests verify
  that a failure after readiness faults its session and cannot affect a restart.
- Pipeline tests feed constructed Raw Input through the production adapter and
  clock. An 8 kHz fake source keeps exact 125-microsecond intervals while 75-microsecond
  callbacks replay a long resolved prefix. A separately paused collector also
  verifies concurrent source progress and FIFO-lock ownership. Further tests cover
  rollover, collector faults, stale success/data, shared quota exhaustion, reserved
  error markers and shutdown/discard of transferred storage. Rejected wake-up
  regressions verify source-only failure consumption, restart while the Failed
  callback is paused, stale-session isolation and complete spool cleanup.

Run the Release solution build with `VCPKG_MAX_CONCURRENCY=4`, then
`x64\Release\RecordPlaybackDLLTest.exe` and
`dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Release -p:Platform=x64`.

Verification uses VS18 MSBuild, fake input, temporary files and hidden compiled
WinUI controls only. Physical Raw Input/hotkey delivery and live-device throughput
are not exercised. Disk I/O remains batched on the source; individual filesystem
stalls and OS scheduling are outside the deterministic callback-drain regression.

Pipeline verification (2026-09-21): full x64 Debug and Release solution builds
pass with VS18 MSBuild and VCPKG_MAX_CONCURRENCY=4. Each configuration passes
91 native and 522 managed tests, none skipped. The two rejected-wake regressions
also pass 50 consecutive Release runs. The existing MSB3851 mismatch
between native and managed Windows SDK targets remains the only build warning.

Ordering references: [GetMessage](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmessage),
[Raw Input](https://learn.microsoft.com/windows/win32/inputdev/about-raw-input), and
[WM_INPUT cleanup](https://learn.microsoft.com/windows/win32/inputdev/wm-input).
