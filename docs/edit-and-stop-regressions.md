# Click-away edits and recording stop shortcuts

## Acceptance checks

- Clicking blank space outside an auto-applying editor field leaves the field
  and applies a valid edit. Wait, execution duration and destination edits keep
  their existing precision and validation. Explicit raw-input Apply remains
  deliberate; moving focus must not silently apply an unfinished raw draft.
- Clicking inside a field keeps normal caret placement and text selection.
  Moving between fields, selecting another action, dragging a path and invoking
  playback must not apply a draft to the wrong action or use a stale value.
  Invalid input must not silently become a valid-looking saved value.
- A recording stopped with its registered keyboard shortcut must not gain an
  incomplete Ctrl event from that command. Genuine recorded modifier use and
  non-shortcut stop paths must retain their input, ordering and timing.
- Stop-command filtering belongs to capture/command handling, not the action
  editor or file loader. Existing recordings and intentionally incomplete input
  are not retrospectively cleaned up.
- Preserve start-shortcut suppression, ordered capture completion, queued UI
  delivery, content revisions and independent recording sessions.

## Verification boundary

Development verification uses automated tests and builds only: no visible app
launch, real recording, global-input injection or playback. Headless tests must
distinguish pure focus policy and capture-stream behavior from actual Windows
pointer/focus delivery, which still needs a user smoke test.

For that smoke test, edit a wait and click blank inspector space, then repeat
with Enter, Tab and a different action. Check valid values persist and invalid
values remain explained. Record ordinary movement and stop with Ctrl+W; inspect
the last action. Repeat with genuine Ctrl-modified input before stopping, and
with the currently selected emergency-stop shortcut.

## Integrated verification (2026-09-20)

Both isolated implementations and their interaction were independently reviewed.
The UI review's shortcut-routing and raw-draft preservation findings are fixed
and covered by regression tests. Integration retained every worker commit;
the auto-merged main-window routing was checked against the reviewed changes.

- Full x64 Debug and Release solution builds pass. The pre-existing MSB3851
  managed/native Windows SDK target mismatch warning remains.
- Each configuration passes 444 managed and 63 native tests, with none skipped.
- The managed suite includes a separate invisible WinUI process with fake engines:
  it verifies editor commits, command gating, selection and draft preservation.
  It never shows/activates the window. Physical pointer focus, Tab traversal and
  popup routing remain user smoke checks, not claims of this hidden diagnostic.
- Self-contained Release publish was refreshed. Seven application/runtime files,
  all eight compiled XAML files and the native build output match the published
  copies by SHA-256.
- No real recording, input injection, visible app launch, push or GitHub write.

Capture changes integrated at `85bcac7`; UI changes at `01e4b6d`, `220f680`,
`de0ee16` and `909dd45`. The resulting executable is
`x64/Release/publish/MacroRecorderGUI.exe`.
