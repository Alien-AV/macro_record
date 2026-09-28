# Wait authoring and editor refinement

Baseline: `91fb5d8`, pushed to `origin/master` at the user's request.
Status: delegated implementation; acceptance criteria below are not a delivery claim.

## Requested outcomes

- Represent fixed pauses as understandable Delay steps, not a ubiquitous raw
  event `Pause before` property. Preserve existing recordings' exact timing and
  unknown protobuf fields; opening a recording must not rewrite its bytes.
- Make adding an action discoverable with a visible, typed Add menu. Keep the
  advanced raw-event editor available without making it the primary workflow.
- Show Sequence/Details switches only when they switch between mutually
  exclusive narrow layouts. Keep keyboard pane navigation and drafts intact.
- Fix first-blur numeric draft handling. Empty duration fields normalize to zero
  where zero is valid; other invalid drafts remain editable with a clear error.
  No first-click restoration, flicker, partial coordinate commit, or duplicate undo.
- Provide explicit window/pixel target picking, with editable captured selectors.
  Optional configurable hotkeys capture the foreground window, hovered window,
  or the current pointer pixel/color without bringing the recorder forward first.
- Implement accessibility-text, OCR-region, and read-only process-memory waits.
  Put the observation machinery in a separate library consumed by the app.
  Memory supports explicit pointer chains, not scanning or chain discovery.

## Safety and compatibility

Observation is explicit: a picker/capture command, bounded Test condition, or
real playback. Selection, opening a document, and simulated Preview never read
other apps. Targets use durable selectors; PID/HWND are runtime identity only.
Unavailable/partial reads must never satisfy negative conditions. Bounds,
timeouts, cancellation, stable process identity and resource release are tested.

Memory access is read-only, locally opt-in, and cannot be enabled by an imported
macro. No memory writes, privilege escalation, injection, or security bypass.
OCR must work in the unpackaged distribution; no implicit cloud upload or
runtime download. Unsupported backends/languages must produce actionable errors.

New execution-critical event/condition types need a fail-closed format version;
old readers must reject them rather than ignore them. Existing raw/versioned
recordings remain readable, unchanged bytes remain unchanged, and edits/undo
retain origin metadata and unknown fields. Fixed delay must not inject dummy
keyboard or mouse input, and must remain cancellation-responsive.

The user confirmed capture hotkeys must work both during recording and while
editing. Active insertion needs an ordered recording boundary and chord
suppression, not a UI append racing queued native input. Capture the target at
invocation without activating the recorder, insert at a safe released-input
boundary, and continue capture without a dialog. Idle capture opens an editable
draft. Reject stale commands and unsafe input-held boundaries explicitly.

## Coordination and verification

Fresh-context workers own disjoint editor, picker/hotkey, and backend slices.
Shared schema and insertion contracts are coordinated before dependent edits.
Independent read-only reviews precede integration; coordinator validates actual
branch tips and complete ranges, then runs combined Debug/Release tests.

Do not launch/show/activate the production app, register live global hotkeys,
observe the user's desktop/processes, inject input, or touch real recordings and
preferences in verification. Use fake OS/provider seams, owned test fixtures,
temporary stores, and existing unactivated compiled-control diagnostics. Verify
the final self-contained package without launching it.

Important regressions: first blank-field blur; invalid coordinate pairs;
selection/undo after delay insertion/deletion; legacy raw timing and origins;
trailing/delay-only playback; no dummy input; hotkey conflict/rollback/disable;
negative monitor coordinates and DPI; captured selector identity; missing or
recreated UIA elements; inaccessible/partial memory reads; pointer-width and
address overflow; process exit/restart; OCR bounds/language/deployment failures;
timeout/cancel while provider cleanup remains pending; no observation in Preview.
