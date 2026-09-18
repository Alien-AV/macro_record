# Native action editor

The editor is a WinUI view over the original event objects. Opening a macro,
grouping input, selecting a path, and previewing do not modify its bytes, event
order, or delays. `.macro` protobuf fields and the native ABI are unchanged.

## Selection and editing

- The action list supports Ctrl/Shift multi-selection. The inspector changes
  the primary selected action; Remove selected removes the selected input.
- Expanding Advanced enters raw selection and selects its first row. Raw rows
  cover all selected actions. Ctrl/Shift selects an exact raw subset. The
  selection summary explicitly identifies the scope used by Remove selected.
  Capture/refresh never broadens that subset. Collapsing Advanced returns to
  the highlighted action selection. Undo can reopen a restored raw selection.
- Wait-before changes only the first event's delay. Duration scales the other
  delays using cumulative integer arithmetic, preserving their exact requested
  total, count, and order. A zero-duration action distributes a new duration
  evenly across its internal intervals. A single event has no internal duration.
  Overflowing per-event results are rejected before any mutation.
- Ordinary timing uses milliseconds/seconds. Inspector seconds accept up to six
  decimal places, giving exact microsecond precision. Advanced raw delays expose
  the original unsigned microsecond values. Bulk raw delay editing explicitly
  sets **each** selected delay and therefore changes internal duration too.
- The recording toolbar's optional override retains its existing session-bound
  auto-delay behavior. It applies a per-event delay after recording, not a total
  action duration. Native Playback is explicitly labeled real input.
- Undo includes timing, geometry, conversion, raw edits, add/remove, and editor
  Clear all. It retains later capture appends. External replacement, deletion,
  reordering, or property changes (including recording auto-delay) invalidate
  history. Undo never rolls back the macro content revision. History is bounded
  to 30 edits and a raw-reference budget, retaining at least the latest edit.

## Coordinate and grouping limits

- Relative motion is **device counts**, not pixels. The display shows an
  independent count trace, starting at zero for each relative segment; it never
  adds counts to an absolute pixel origin for display. Pointer acceleration,
  speed, application sensitivity, and window targets were not recorded.
- Absolute coordinates retain their original primary-screen/virtual-desktop
  frame, including negative desktop coordinates. Only the selected frame is
  drawn. Button/wheel payload X/Y do not create a position; stationary actions
  inherit the last movement position. A drag without a preceding position is
  displayed conservatively and cannot be edited geometrically.
- Geometry editing requires a complete move/drag and consistent absolute
  movement throughout the macro, with no incomplete input sequences. Moving
  the endpoint bends all original samples toward the new target and tapers the
  next path's correction back to its existing destination. It never removes
  samples. Coordinates outside signed 32-bit bounds are rejected atomically.
- An explicit, acknowledged conversion assumes **one count equals one pixel**
  after an existing absolute anchor. This changes replay coordinates and is
  undoable; it cannot reconstruct the recorded cursor path. Earlier unanchored
  counts remain relative. Overflow is rejected. Mixed primary/desktop frames
  remain blocked even after conversion; the editor does not guess a frame remap.
- Neutral movement/scroll groups break at a 250 ms pause, coordinate-frame
  change, wheel direction/axis change, or semantic boundary. Button and key
  state take precedence over the pause heuristic. Modifier-assisted mouse
  gestures and overlapping buttons form compact mixed sequences, not invented
  simple clicks/chords. Missing or unmatched transitions are identified as
  incomplete/raw. Key names describe VK codes, never inferred typed text.
- Display sampling is bounded (1,200 overview points, 512 selected points,
  256 overview landmarks plus the selected landmark). Original events are
  always retained. Clicks use circles, drag starts use squares, and drag paths
  use dashes. The action list and coordinate fields provide keyboard alternatives.
- Preview and scrubbing are visual only. The dot holds at recorded positions
  between events; it does not invent movement during waits. The dot is hidden
  before the first known position or when the current event uses another frame.
  View timers and subscriptions stop on unload/disposal. Capture projection
  appends incrementally and visual refreshes are batched to 100 ms. Closed raw
  drilldowns retain no rows; open ones reuse rows during capture. Typed inspector
  drafts survive capture refreshes until applied or a different action is chosen.

Window-relative targets are intentionally reserved for a later pass.

## User smoke checks after integration

No application launch or real keyboard/mouse injection was used for development
verification. The user will perform these smoke checks after integration:

1. Open in both light and dark themes; resize through wide, medium, and narrow
   layouts. Reach every field/button using Tab and scroll the action/raw lists.
2. Load a known macro. Confirm action counts, raw counts/order, readable times,
   and exact raw values. Save without editing and compare the decoded events.
3. Select moves, clicks, drags, scroll groups, a Ctrl+S chord, and an incomplete
   sequence. Check path highlights, circle/square landmarks, dashed drags, and
   exact raw drilldown. A long Shift/Ctrl-assisted mouse gesture should stay compact.
4. Use Preview and scrub through waits, zero-time events, relative segments,
   clicks, and drags. Only the preview dot should move. Switch tabs/close a tab
   during preview and confirm it stops. Do not use real Playback for this check.
5. Change wait and duration independently, including zero duration and six
   decimal places. Verify exact raw totals and unchanged event count/order; Undo.
6. For a complete, consistently absolute macro, enter destination coordinates
   and drag the outlined handle. Verify the inherited click position, connected
   following path, and unchanged following destination. Undo both changes.
7. Open an unanchored/relative/mixed-frame recording. Confirm truthful labels,
   disabled geometry with a reason, and no automatic conversion. If testing the
   explicit estimate, acknowledge its assumptions, inspect changed raw values,
   and Undo before saving. Incomplete input must remain conservative.
8. Ctrl/Shift-select actions, expand Advanced, select a raw subset (including
   across selected actions), and remove it. Exactly the visible selected raw
   rows should disappear; Undo restores them. Collapse Advanced and confirm the
   selection summary returns to actions. Press Delete inside a numeric text
   field: only text should be deleted.
9. Record a dense stream while typing an unapplied timing/raw draft and while
   selecting raw rows. The draft and raw subset must survive appends. Collapse
   Advanced and verify capture remains responsive. Then stop recording.
10. Edit, append a recording, and Undo: later capture input must remain. Edit,
    then load/replace/clear externally or apply recording auto-delay: stale undo
    must be unavailable. Confirm clear-before-recording session rollover still
    delivers new-session events to the correct macro.
11. Exercise macro tabs, reorder, add mouse/key event, multi-select remove,
    Clear all/Undo, load, save, and optional after-recording per-event override.
    Close the window during preview/capture and check normal shutdown.

## Deterministic verification

Managed tests cover conservation (including randomized streams), stateful
grouping, relative/absolute/frame correctness, integer timing/overflow, explicit
conversion, geometry connection, undo versus capture and external changes,
selection scopes, reusable raw rows, visual sampling/landmarks, and 100,000-event
incremental capture. Existing capture/session/playback tests remain in place.
The native test suite uses fake sinks; no native changes are required here.

Build/test commands: the existing x64 managed `dotnet test` command and VS2026
MSBuild solution builds in Release and Debug. The known native SDK warning
MSB3851 is unrelated to this editor.

Implementation verification: 157 managed tests and 48 native tests passed in
each of Debug and Release; both full solution builds succeeded. UI appearance,
interaction, and actual replay remain for the user's smoke checks after integration.
