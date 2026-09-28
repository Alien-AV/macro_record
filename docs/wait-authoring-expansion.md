# Wait authoring and editor refinement

Baseline: `91fb5d8`, pushed to `origin/master` at the user's request.
Status: implemented, independently reviewed, and integrated. Full-feature
verification was performed at `e626166`; subsequent report edits do not change code.

## Integration checkpoints

- Shared wait contracts, standalone Delay wire type and format version 4:
  integrated at `a6d94b9` after independent review.
- Bounded Windows accessibility backend: integrated at `8bc3d81` after
  independent review, including verification against Windows SDK COM signatures.
- Editor and Delay interactions: integrated through `f8b73e7`.
- Ordered recording markers and read-only capture preflight: integrated through
  `1feff54` and `41fad6d`.
- Shared observation backends and lifecycle repairs: integrated through `6dd5ad3`.
- Pickers, configurable capture shortcuts, local settings and recovery:
  integrated through `065d3c4`.
- All five source forms and repairable validation drafts: `b9d1a2f`.
- Portable OCR, explicit preparation, CI and publish verification: `e626166`.

## Verification and review result

- Full x64 Debug and Release solution builds passed. Each configuration passed
  **1,062 managed tests and 131 native tests**, with no skipped managed tests.
- Debug and Release `scripts/Test-Publish.ps1` passed: missing native/OCR sources
  and stale bundles are rejected; newer corrupted destination files are replaced;
  native DLL, PRI, eight XBF files, self-contained runtime and OCR hashes match.
- Main-checkout OCR preparation and generated PNG/BMP/binary-stdin recognition
  passed. The 43-file bundle is approximately 10.6 MB and depends only on Windows
  inbox DLLs. No global installation or PATH change is needed.
- Independent full-range reviews and re-reviews closed every reported finding.
  Repairs include held-input completeness across Delay, selection/delete scope,
  capture modifier ordering and timestamp ambiguity, ABI versioning, expiry,
  stale-callback preflight, failed settings rollback/close recovery, numeric
  validation parity, OCR termination cleanup, hidden invalid drafts, accessible
  Cancel labels, and OCR provenance/notice handling.
- Independent integration audits found no semantic drift or missing wiring.
  Both editor and source-form hidden regression hooks remain reachable.
- The existing MSB3851 managed/native Windows SDK target warning remains; there
  are no new build failures. Physical focus, appearance and live-provider behavior
  still need the user's manual checks; automated checks are not a substitute.

The release artifact is `x64/Release/publish/MacroRecorderGUI.exe`. Its final
self-contained copy is rebuilt after committing this report, so build metadata
corresponds to the delivered revision. No production app is launched during
verification, and real recordings/preferences are not modified.

## Using the new controls

- Use **Add event** for Click, Shortcut, Pointer movement, Delay, or Wait until;
  raw event creation remains under Advanced. Legacy leading gaps appear as Delay
  steps without rewriting the imported stream.
- Sequence/Details switches appear only when the narrow layout needs them.
  Empty optional timing fields commit zero on the first blur; other invalid
  drafts stay visible and repairable.
- Wait forms offer explicit window/pixel picks, accessible-element choices,
  two-corner OCR regions, installed languages, and process/module choices.
- Enable/configure foreground-window, hovered-window and pointer-pixel capture
  shortcuts in Settings. They default to off. Editing opens a captured draft;
  recording inserts at an ordered safe boundary without a dialog or chord leak.
- Read-only memory observation is locally opt-in and cannot be enabled by an
  imported recording. It supports typed scalars and up to 16 explicit pointer
  offsets, not scanning, writes, injection or elevation.
- English OCR is bundled app-locally; other installed language data and engine
  locations can be configured locally. No runtime downloads or cloud OCR occur.
- New standalone Delay and extended wait sources require version 4 contents
  under the existing `.macro` extension; old readers reject them.

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
