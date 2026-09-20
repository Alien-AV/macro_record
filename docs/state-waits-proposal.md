# Conditional waits — implementation proposal

Status: planning only; not implemented or approved for implementation. Prepared against `4818fda` on 2026-09-20. Source line references describe that revision. The current pointer-origin work will retain the `.macro` extension with versioned contents; reconcile this proposal with that document codec before implementation.

+Implement a single **“Wait until…” action** with typed window, pixel, accessibility-text, OCR-text, and memory conditions. Share timing, cancellation, diagnostics, and failure handling across providers. Extend the existing event stream and native playback session; a new scripting engine is unnecessary.

This proposal is based on inspection of `codex/state-waits-plan` at `4818fda`. It is planning only: no files changed, tests run, applications launched, input injected, or process memory read.

1. **Existing architecture and extension points**

   The [protobuf schema](../Common/protobuf/Events.proto:5) currently stores keyboard and mouse events with a delay before each event. The action editor projects ranges of these original events into moves, clicks, drags, and key sequences. A standalone conditional wait therefore needs an explicit stream entry.

   Add a `WaitCondition` alternative to the existing event `oneof`. Field `4` is available at the inspected revision; coordinate its allocation during integration. Add a managed `WaitConditionEvent`, a native wait-event representation, and `ActionKind.Wait`.

   The principal existing extension points are:

   - [ActionProjection](../MacroRecorderGUI/Editor/ActionProjection.cs:46): make waits standalone grouping boundaries.
   - [ActionEditor](../MacroRecorderGUI/Editor/ActionEditor.cs:211): include condition edits in undo and change tracking.
   - [PlaybackWorkflow](../MacroRecorderGUI/Models/PlaybackWorkflow.cs:25): clone and validate the complete playback snapshot.
   - [PlaybackEngine](../MacroRecorderGUI/Models/PlaybackEngine.cs:65): coordinate asynchronous observation with native progress.
   - [PlaybackSession](../RecordPlaybackDLL/Playback/PlaybackSession.cpp:85): suspend execution while retaining session ownership, timing, cancellation, and cleanup.

   Support waits at the beginning, between actions, and at the end. A wait-only macro should also be valid and should not inject startup modifier releases.

2. **User workflow and editor**

   Add **Add → Wait until…** after the selected action. The inspector builds a readable sentence:

   *Continue when [target] [condition], stable for [200 ms], within [30 seconds].*

   Its controls are:

   - Condition source: window, pixel, accessibility text, OCR text, or advanced memory.
   - Target selector, with a picker and editable fields.
   - Comparison and expected value.
   - Trigger mode.
   - Timeout and stability interval.
   - Advanced polling and timeout policy.

   Manual fields matter because a dialog or process may not exist while the macro is being edited. Show how many targets match and explain ambiguous selectors.

   Provide **Test condition**, which observes only that condition for a bounded period and displays its current result. Selecting a row or opening a macro must not begin monitoring. Test results and runtime baselines remain transient.

   Add a separate **Replace this fixed delay with a condition** command. That command explicitly removes the selected delay; ordinary insertion preserves existing event delays.

   Reuse existing draft validation, command-time commits, selection identity, and undo. Invalid condition drafts block save/play consistently with other invalid inspector edits.

   For the MVP, allow waits only at boundaries where recorded keys and mouse buttons are released. Validate imported macros against the same rule before playback. Waiting during a held drag or chord can be a later deliberate feature.

3. **Precise condition semantics**

   | Mode | Completion rule |
   |---|---|
   | **Is true**, the default | An already-satisfied condition qualifies, subject to stability time |
   | **Becomes true** | Observe false after arming, then true |
   | **Changes from starting value** | Capture the first valid runtime sample, then observe a qualifying difference |
   | **New matching window** | Observe a matching window instance outside the initial matching set |

   The wait starts after its fixed pre-delay. Each occurrence—including each repeat—gets a fresh baseline, deadline, and identity token. Editor samples never become playback baselines.

   Default state semantics handle fast applications well: if the preceding click opens a dialog immediately, “window is visible” succeeds even if opening happened before observation began. Strict transition modes cannot recover earlier changes or guarantee detection of transients between samples. A future “arm before this action” feature would address that different workflow.

   Proposed defaults are **30 seconds timeout** and **200 milliseconds stability**. Both use monotonic time. A false, unavailable, or excessively stale sample resets stability. Success requires fresh matching observations spanning the stability interval.

   Stability means consistency across observations, not proof that nothing changed between them. For “changed from baseline,” stability applies to the difference predicate; the changing value need not become stationary.

   Keep observation results distinct:

   `Match`, `NoMatch`, `Unavailable`, and `Error`.

   A failed read must never satisfy a negative condition. “Window absent” can be established by successful enumeration; inaccessible text cannot establish “text absent.”

4. **Provider scope and behavior**

   **Window conditions**

   Start with top-level desktop windows: exists, visible, foreground, absent, and new matching instance. Visibility does not imply that the application is ready; users should select a text or pixel condition when readiness matters.

   Persist application identity, window class, and optional title matching. Prefer executable path or available application identity over executable name alone. Treat PID and HWND as runtime references, associate processes with creation identity, and revalidate cached handles. Never persist a handle as durable identity.

   Multiple matches require a more specific selector or an explicitly chosen “any matching window” rule. For baseline comparisons, retain the resolved instance identity so switching targets cannot masquerade as a value change.

   Use `EnumWindows` initially. Event-assisted observation can later use scoped, out-of-context `SetWinEventHook`, with callbacks triggering re-evaluation and periodic polling retained. The registering thread requires a message loop. [Microsoft: EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows), [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook).

   **Pixel conditions**

   Offer desktop physical pixels and window-client coordinates. Store the coordinate mode explicitly. For window-client locations, offer DPI-scaled logical offsets, retaining reference DPI and dimensions for diagnostics. Recalculate screen position as the window moves; do not automatically normalize positions against resized content.

   Preserve negative monitor coordinates, reject points outside the selected bounds, and never interpret recorded relative mouse counts as screen pixels. The existing application manifest already declares per-monitor DPI awareness. [Microsoft: ClientToScreen](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clienttoscreen), [DPI guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

   Start with one RGB sample, equality/inequality, and a maximum per-channel tolerance. A later small-patch mode can reduce sensitivity to noise.

   Make the visible-screen sampling contract explicit: another window covering the location can change the observed color. Minimized targets, invalid captures, out-of-bounds coordinates, and detected occlusion produce unavailable observations. Do not promise hidden-window rendering. `GetPixel` is a verified single-pixel candidate, subject to its device-context limitations. [Microsoft: GetPixel](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getpixel).

   **Accessibility-text conditions**

   Scope element lookup to the selected window. Use automation ID, control type, and bounded ancestor context; keep identification separate from the text being tested.

   Expose the chosen text source. An element’s accessible name and a document’s text are different observations. Begin with exact/contains matching and explicit case and whitespace settings. Defer regex until its execution limits and UI are justified.

   Read bounded text, handle element recreation, and report unsupported text access clearly. Do not silently switch to OCR. [Microsoft: accessible name](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-get_currentname), [bounded text retrieval](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-gettext).

   **OCR-text conditions**

   OCR is a separate provider with an explicit region and language. It observes rendered pixels and inherits capture, occlusion, scaling, and recognition limitations.

   There is a verified deployment constraint: this repository sets `WindowsPackageType=None`, while Microsoft lists `Windows.Media.Ocr` among APIs requiring package identity. Resolve packaging or choose a supported local OCR dependency before implementing that backend. Do not assume referencing the WinRT API makes it supported in this unpackaged application. [Microsoft: APIs requiring package identity](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-api-desktop-app-support#apis-that-require-package-identity).

   Validate available recognition languages and backend image-size limits. [Microsoft: OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine?view=winrt-26100).

   **Memory conditions**

   Provide an advanced, locally enabled, read-only provider for legitimate application automation. Start with one fixed-width numeric value at an absolute address or module-plus-offset. Configure signedness, width, numeric comparison, and floating-point tolerance.

   Module-relative addresses accommodate relocation but do not guarantee compatibility across application versions. Validate module identity/version. Absolute addresses should be clearly described as fragile across restarts.

   Resolve one process instance per wait. An initially missing process can remain pending; an exit after binding is an error, not permission to attach to a replacement process.

   Request only memory-read and necessary identity-query rights. Verify the returned byte count before interpreting a sample. An inaccessible or partial read is never a value. Multi-field consistency is outside the initial scope.

   Local opt-in cannot be granted by importing a macro. No memory writing, injection, privilege escalation, scanning, pointer-chain discovery, or security bypass is required. `ReadProcessMemory` explicitly requires `PROCESS_VM_READ` and a readable requested range. [Microsoft: ReadProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory).

5. **Playback execution and cancellation**

   Preserve one native session. Splitting the macro around waits into separate native runs would repeatedly invoke startup modifier handling and final held-input cleanup.

   Extend native progress reporting with an active wait request containing:

   `session ID, wait-occurrence token, event index, remaining timeout`.

   Add a resolution operation accepting the session ID, occurrence token, and outcome. Tokens must distinguish repeated visits to the same event. Reject stale and duplicate results.

   Native execution waits on its existing cancellation-aware synchronization. A managed `WaitRunner` performs observations asynchronously. Native progress polling continues while the observer is active; it must not await the provider inside the playback lock.

   The native session independently enforces the deadline. A stalled observer therefore cannot suspend playback forever. Cancellation and timeout close the occurrence before any late result can resume it.

   After a successful wait, anchor subsequent scheduling to the resume time. The current native scheduler accumulates deadlines; without this adjustment, time spent waiting would make later input overdue and cause a catch-up burst.

   Playback speed affects recorded delays, including a wait’s fixed pre-delay. It does not affect observation intervals, stability requirements, or deadlines.

   Preserve current emergency-stop, ownership, and cleanup guarantees. Native abort must not depend on an external observation call returning.

6. **Resource bounds, failure policy, and run feedback**

   These are proposed initial product limits, not claims about operating-system limits:

   | Resource | Initial bound |
   |---|---|
   | Wait duration | Default 30 seconds; configurable finite maximum of 24 hours |
   | Concurrent work | One active wait and one observation in flight |
   | Window enumeration | Stop after 4,096 candidates; report limit exceeded |
   | Pixel sampling | One point initially; later patches capped at 25 pixels |
   | Accessibility lookup | 512 visited elements, depth 16, 64 KiB returned text |
   | OCR | One region up to one megapixel, additionally constrained by backend dimensions |
   | Memory | One scalar of at most eight bytes per observation |
   | Diagnostics | Latest observation plus a bounded 100-entry result history |

   Start with polling defaults of 100 ms for window/pixel/memory, 250 ms for accessibility, and 500 ms for OCR. Apply provider-specific minimum intervals. Coalesce notifications into one pending refresh.

   Hitting a lookup or text limit must report an incomplete observation, not “absent.” Avoid retaining screenshots or memory values in persistent logs by default.

   UI Automation calls belong on a dedicated MTA thread, with subscription removal on that same thread and explicit provider timeouts. A cancellation token cannot forcibly interrupt an arbitrary external call. Where hard teardown is required, isolate blocking providers in one supervised helper with bounded shutdown; never accumulate abandoned tasks or threads. [Microsoft: UI Automation threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading), [transaction timeout](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation2-put_transactiontimeout).

   Default failure policy is **stop the macro**. Report the action, target, expected condition, last observation, and reason. Distinguish timeout, ambiguous target, access denial, unsupported provider, provider failure, and user cancellation.

   A later explicit “continue after timeout” policy can record a warning. It should not apply to invalid configuration, unsupported semantics, permission failures, or cancellation.

   Add an active, stoppable `Waiting` playback phase. Show the condition, elapsed/remaining time, and stability progress. Waiting for a window does not activate it or redirect subsequent input to it.

7. **File format, old-reader fail-safe, and migration**

   Proposed payload structure:

   ```text
   WaitCondition
     semantics_version
     trigger_mode
     timeout_us
     stable_for_us
     poll_interval_us
     timeout_policy
     oneof condition
       window
       pixel
       accessibility_text
       ocr_text
       memory
   ```

   Require supported versions, valid enum values, exactly one supported condition, finite validated durations, and bounded selectors. Validate the entire schedule before any playback input. Regenerate checked-in C# and C++ protobuf sources together.

   Existing `.macro` files require no conversion. Preserve all existing field numbers and retain original bytes for untouched documents. Continue cloning protobuf messages rather than reconstructing known fields, so unknown fields survive edits and undo. [Current persistence](../MacroRecorderGUI/ViewModels/MacroViewModel.Persistence.cs:72) already preserves the original envelope, and [existing tests](../MacroRecorderGUITests/RecordingLibraryWorkflowTests.cs:91) cover unknown-field retention. Protobuf documents that field-by-field reconstruction can discard unknown fields. [Protobuf guidance](https://protobuf.dev/programming-guides/proto3/#unknowns).

   The old-reader fail-safe is grounded in this repository:

   - The current [managed event factory](../MacroRecorderGUI/Event/InputEvent.cs:42) throws when no supported payload exists.
   - The current [native deserializer](../RecordPlaybackDLL/Common/DeserializeEvent.cpp) also rejects unsupported/missing payloads and parses the schedule before playback starts.

   Consequently, an older reader should reject an event containing only the new wait payload. Verify that behavior with fixtures parsed through the old schema. Do not encode execution-critical waits solely in top-level metadata, which old readers could ignore while replaying the remaining input.

   New readers must also reject unsupported wait versions/operators instead of applying default behavior. No automatic export mode should strip waits.

   The pointer-origin worker’s metadata changes are an integration coordination point only. Reconcile field allocation, envelope cloning, and capability checks. In particular, [SerializeEvents](../MacroRecorderGUI/Utils/SerializeEvents.cs:9) currently creates a fresh top-level envelope for playback; any execution-relevant document metadata must survive that boundary.

8. **Preview and duration presentation**

   Keep Preview simulated and disconnected from all live providers.

   Represent waits as checkpoints with **Simulate satisfied / Next** controls. Add an action-position component to navigation: current preview is time-based, and consecutive waits can share the same timestamp. They must remain individually selectable and step through in order. [Current VisualPreview](../MacroRecorderGUI/Editor/VisualPreview.cs:19).

   Show “recorded timing + N conditional waits.” A timeout is a maximum, not the action’s execution duration. Preserve known input timing and optionally show a calculated maximum for finite runs.

   Update library summaries, `Describe`/`Validate`, action summaries, raw-event wording, and inspector text consistently. Condition testing remains a separate explicit command.

9. **Delivery phases and verification**

   **Phase 1:** protobuf and validation, standalone editor action, undo, native wait boundary, shared runner, window/pixel providers, state/change modes, deadlines, diagnostics, and simulated preview. Ship one condition per wait with stop-on-failure.

   **Phase 2:** accessibility text, bounded element selection and reads, provider timeouts, and event-assisted observation.

   **Phase 3:** locally enabled memory provider, typed scalar reads, module-relative addressing, and process-instance validation. This can proceed independently of text work once the foundation exists.

   **Phase 4:** OCR after resolving deployment. Add bounded flat “all/any” composition only when concrete workflows justify it.

   Automated verification should use fake observations, controllable clocks, and the repository’s existing fake injection sinks. Cover:

   - Already-true states, transitions, new-window identity, baselines, flicker, unavailable samples, and sampling gaps.
   - Timeout/success/cancel races, hung observers, stale responses, repeat resets, and post-wait scheduling.
   - Startup/cleanup behavior, rejected waits during held input, and wait-only macros.
   - Old/new schema fixtures, unknown fields at every relevant nesting level, undo, and untouched byte preservation.
   - Ambiguous targets, PID/handle reuse, DPI/monitor changes, inaccessible memory, partial reads, and provider limits.
   - Consecutive waits, trailing waits, preview stepping, and the absence of live observation during Preview.

   Real desktop validation should be a separate authorized step using controlled target applications.

   The remaining product choices are strict pre-arming before a trigger, waiting while holding input, OCR deployment, and optional timeout continuation. Recommended initial decisions are state-based waits by default, released-input insertion points, stop-on-failure, and accessibility text before OCR.
