# Native action editor

The editor is a WinUI view over the original event objects. Opening a macro,
grouping input, selecting a path, and previewing do not modify its bytes, event
order, or delays. `.macro` protobuf fields and the native ABI are unchanged.

## Selection and editing

- The action list supports Ctrl/Shift multi-selection. The inspector changes
  the primary selected action. Add and Delete live above the action list;
  its context menu also offers these commands and Clear all. Clear all is
  also available from Add. Delete removes the selected input.
- Expanding Advanced enters raw selection and selects its first row. Raw rows
  cover all selected actions. Ctrl/Shift selects an exact raw subset. The
  label above the list and Delete's accessible name identify the current scope.
  Capture/refresh never broadens that subset. Collapsing Advanced returns to
  the highlighted action selection. Undo can reopen a restored raw selection.
- Wait-before changes only the first event's delay. Duration scales the other
  delays using cumulative integer arithmetic, preserving their exact requested
  total, count, and order. A zero-duration action distributes a new duration
  evenly across its internal intervals. A single event has no internal duration.
  Overflowing per-event results are rejected before any mutation.
- Action rows are numbered by group, with intent names, icons and concise
  incomplete warnings. Raw event counts, coordinate representation and original
  indices are available in row tooltips and Advanced. The macro heading shows
  its name, action count and total time.
- Display timing is rounded (for example, 279ms or 2.44s); nonzero sub-millisecond
  values use µs. Inspector seconds accept up to six
  decimal places, giving exact microsecond precision. Advanced raw delays expose
  the original unsigned microsecond values. Bulk raw delay editing explicitly
  sets **each** selected delay and therefore changes internal duration too.
- The toolbar has Record, Stop, Play and Abort; Play visibly says Real input.
  Compact widths retain the command labels and real-input cue. File holds
  New/Load/Save; the gear opens recording and playback options (clear before
  capture, loop, and raw delay override). Defaults exist before the flyout opens:
  clear before capture on, loop and delay override off, override delay 5000µs.
  The optional override retains its existing session-bound
  auto-delay behavior. It applies a per-event delay after recording, not a total
  action duration.
- Undo includes timing, geometry, conversion, raw edits, add/remove, and editor
  Clear all. It retains later capture appends. External replacement, deletion,
  reordering, or property changes invalidate history. Applicable recording
  auto-delay also invalidates history when the assigned values already match;
  unrelated tabs and stale recording revisions are unaffected.
  Undo never rolls back the macro content revision. History is bounded
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
  next approach's correction back to its existing endpoint before a button/key
  transition or movement pause. This preserves a downstream drag's original
  button-down anchor and drag samples even inside a modifier-assisted sequence.
  It never removes samples. Coordinates outside signed 32-bit bounds are rejected
  atomically.
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
  always retained. Bounds include the complete stream in each coordinate frame,
  including extrema omitted from display samples. The selected path uses accent
  color and a heavier stroke; other paths are subdued. Clicks use circles, drag
  starts use squares, and drag paths use dashes. Selected segments have open
  start circles and end diamonds; up to eight direction cues follow only known,
  continuous displayed segments. The viewport centers the complete frame with
  margins and has no pixel grid over device counts. The action list and
  coordinate fields provide keyboard alternatives.
- Preview and scrubbing are visual only. The dot holds at recorded positions
  between events; it does not invent movement during waits. The dot is hidden
  before the first known position or when the current event uses another frame.
  The requested slider position is retained independently of integer-microsecond
  rounding, so arrow-key scrubbing advances even for very short recordings.
  View timers and subscriptions stop on unload/disposal. Capture projection and
  action selection append incrementally; eligibility and frame bounds are cached.
  Visual refreshes are batched to 100 ms. A redraw before a pending refresh defers
  until current selection references are restored under the refresh guard. Closed raw
  drilldowns retain no rows; open ones reuse rows during capture. Typed inspector
  drafts survive capture refreshes and Add mouse/key commands while the same
  first input remains selected. Applied edits reset drafts; choosing a different
  action resets them too. Raw drafts survive when their event remains selected.
  Draft tracking uses synchronous
  [WinUI TextChanging](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textbox.textchanging?view=windows-app-sdk-1.8),
  so programmatic field population cannot become an asynchronous false draft.

Window-relative targets are intentionally reserved for a later pass.

## Window chrome and layout

The app retains the native title bar, system menu, drag/resize behavior and
minimize/maximize/close buttons. On Windows 11, all twelve title/caption color
properties are assigned together from the actual light/dark theme, including
inactive, hover and pressed states. Actual theme and system color changes
refresh the palette. Entering high contrast resets every override to Windows'
defaults; leaving it reapplies the current theme. System event handlers are
removed at shutdown.

Desktop high-contrast monitoring uses
[ThemeSettings.CreateForWindowId and Changed](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.system.themesettings?view=windows-app-sdk-1.7)
with the window's own `AppWindow.Id`; system colors use
`UISettings.ColorValuesChanged`. `AccessibilitySettings.HighContrastChanged`
can fail during subscription in an unpackaged desktop process (observed HRESULT
`0x80070490`) even when reading `HighContrast` succeeds. Theme monitoring starts
after XAML initialization. Early XAML theme callbacks wait for that monitor;
callbacks queued before closing check the closed state before touching the title bar.

On Windows 10 the native title bar retains system colors, which can still look
light over dark app content. This is an explicit compatibility limitation:
[Microsoft documents that AppWindow title-bar colors are ignored on Windows 10](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar#colors),
even when customization support reports true. No custom chrome or undocumented
Windows 10 API is used.

The workspace uses the actual scroll viewport height. At wide sizes the action
list, flexible preview canvas and inspector are alongside one another. At medium
widths the inspector moves below; at narrow widths all three stack. Short windows
scroll the complete panels. The preview's slider, button and clock have automatic
rows below a flexible canvas. Inspector position fields appear only when edits
are safe; otherwise a reason is shown. Delay and Duration keep separate Apply
buttons and exact values; technical conversion and raw details live in Advanced.

## User smoke checks after integration

No application launch or real keyboard/mouse injection was used for development
verification. The user will perform these smoke checks after integration:

1. On Windows 11, open in light/dark themes and switch themes with the window
   open. Check active/inactive title text and caption-button normal/hover/pressed
   states. Enable high contrast, switch its palette, then exit into each app
   theme. Check native drag, resize, system menu, close, minimize and maximize.
   On Windows 10, expect the documented system-color title-bar limitation.
2. Resize through wide, medium and narrow layouts, including short windows and
   increased text/display scaling. Reach every field/button using Tab, including
   Stop and Abort, and scroll the action/raw lists. Confirm Play's Real input
   cue is always visible. Reach the preview transport by scrolling short windows.
   Load a known macro via File; check the macro heading, sequential group
   numbers, short times and exact values/counts in Advanced. Save without editing
   and compare the decoded events.
3. Select moves, clicks, drags, scroll groups, a Ctrl+S chord, and an incomplete
   sequence. Check warning explanations, path highlights, open start circles,
   end diamonds, direction cues, circle/square landmarks, dashed drags, and
   exact raw drilldown. A long Shift/Ctrl-assisted mouse gesture should stay compact.
4. Use Preview and scrub with both pointer and arrow keys through waits, zero-time events, relative segments,
   clicks, and drags. Only the preview dot should move. Switch tabs/close a tab
   during preview and confirm it stops. Do not use real Playback for this check.
5. Change wait and duration independently, including zero duration and six
   decimal places. Verify exact raw totals and unchanged event count/order; Undo.
6. For a complete, consistently absolute macro, enter destination coordinates
   and drag the outlined handle. Verify the inherited click position, connected
   following path, and unchanged following destination. Repeat before a
   Ctrl-assisted approach/drag: its button-down anchor and drag must stay fixed.
   Undo both changes.
7. Open an unanchored/relative/mixed-frame recording. Confirm truthful labels,
   hidden position fields with a reason, and no automatic conversion. If testing
   Convert path estimate inside Advanced, acknowledge its assumptions, inspect changed raw values,
   and Undo before saving. Incomplete input must remain conservative.
8. Ctrl/Shift-select actions, expand Advanced, select a raw subset (including
   across selected actions), and remove it. Exactly the visible selected raw
   rows should disappear; Undo restores them. Collapse Advanced and confirm the
   label above the list and Delete tooltip return to actions. Press Delete inside a numeric text
   field: only text should be deleted.
9. Record a dense stream while typing an unapplied timing/raw draft and while
   selecting raw rows. The draft and raw subset must survive appends. Collapse
   Advanced and verify capture remains responsive. Then stop recording.
   With an unapplied Delay or raw-field draft, use Add mouse and Add keyboard
   while the same first input remains selected; verify drafts still survive.
10. Edit, append a recording, and Undo: later capture input must remain. Edit,
    then load/replace/clear externally or apply recording auto-delay: stale undo
    must be unavailable. Confirm clear-before-recording session rollover still
    delivers new-session events to the correct macro.
11. Before ever opening Options, verify clear-before-capture defaults still
    apply. Then use the gear flyout to exercise clear-before-capture, loop and
    optional after-recording per-event override. Exercise macro tabs, reorder,
    Add mouse/key event from the list, multi-select Delete, Clear all/Undo, and
    File's New/Load/Save commands.
    Close the window during preview/capture and check normal shutdown.

## Deterministic verification

Managed tests cover conservation (including randomized streams), stateful
grouping, relative/absolute/frame correctness, integer timing/overflow, explicit
conversion, geometry connection, undo versus capture and external changes,
selection scopes, synchronous draft tracking, reset-before-redraw ordering,
full-stream viewport bounds, visual sampling/landmarks, and normalized scrubbing
from zero/short durations through arbitrarily large totals. Repeated append
batches exercise the full presentation refresh at 100,000 and 1,000,000 events,
with bounded display allocation and append-only selection work. Existing
capture/session/playback tests remain in place. Presentation tests also cover
compact versus exact timing, sequential group numbering, warning completion,
scroll intent, centered viewport mapping/inversion, direction-cue bounds and
frame gaps, responsive panel sizes, native caption palette reset/restoration,
and surviving action/raw drafts after manual insertion rebuilds the projection.
The native test suite uses fake sinks; no native changes are required here.

`DesktopThemeMonitorTests` also exercises the production settings API bindings:
on an STA thread it creates an invisible native top-level window, subscribes to
both real settings events, checks the high-contrast value, and unsubscribes
repeatedly. The window is never shown or activated and is destroyed afterward.
This test does not construct a XAML window, start the application, register
hotkeys, capture/inject input, or change system theme settings. Actual change
delivery and visual colors still require the user smoke checks above.

UI-only verification commands (from the worktree):

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe' MacroRecorderGUI/MacroRecorderGUI.csproj /restore /m /p:Configuration=Release /p:Platform=x64 /verbosity:minimal /nologo
dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Release -p:Platform=x64 --verbosity minimal
```

Visual appearance and native window interaction require the user smoke checks;
the automated suite does not launch the app or inject real input.
