# Interaction rework proposal

Status: proposed, not approved for broad implementation. The selection, stop-chord,
and scroll-access regressions are separately approved fixes. Conditional waits are
approved for implementation, beginning with window/pixel conditions.

## Why the current workflow feels awkward

The visual shell describes a task library, but many interactions still expose the
underlying input debugger. The user records a task once and replays it often;
opening an editor should not be necessary merely to run a saved recording.

- Library cards open the editor but offer no direct Play action. Explicit Select
  recordings mode used Extended selection: ordinary clicks replaced the selection
  even though the mode itself suggested accumulating a batch.
- The editor nests a workspace scroller, an inspector scroller, and a raw-event
  list. Expanding exact input changes the whole page's geometry and puts competing
  scrollbar tracks along the same edge.
- Add offers raw mouse/keyboard events rather than the intentions users normally
  want to add. The readable action list and low-level authoring vocabulary diverge.
- Timing is split between event delays, action duration, playback speed and soon
  conditional waits. Similar-looking controls describe different concepts.
- Technical sample counts and detailed caveats compete with task intent. Some
  caveats are necessary, but they belong beside the affected operation or in help,
  not as the dominant description of every action.
- Controls with unrelated scopes are distributed between the heading, overflow
  menu, action inspector and transport. Users must discover which setting changes
  a recording, a playback preference, or a global default.

These are interaction problems, not a reason to discard the approved appearance
or change the UI framework. Recent direct Record/Play, remembered preferences,
origin metadata, and recovery changes already establish useful foundations.

## Proposed interaction model

### Library: choose a task, then run or edit

Each recording exposes Play as an explicit button using its remembered settings.
Its name/body opens the editor; playing must never be an accidental consequence of
selecting a card. An adjacent options control changes playback settings without
starting execution. Keep a concise visible summary of repeats and countdown,
especially repeat-until-stopped. Countdown and emergency Stop still apply.

Use visible checkboxes for bulk selection, without requiring a separate Select
recordings step. Checking the first item reveals the contextual count and
Delete/Restore toolbar; each checkbox independently toggles its recording. Keep
card-body opening and explicit Play separate from selection rather than silently
changing their meaning when a batch exists. Escape clears the batch without
opening or running anything. Keep selection scope explicit under search; do not
silently delete hidden items. Restore remains available through Undo and Local
trash. This removes a mode in the proposed rework; the currently approved bug fix
retains explicit selection mode and makes its ordinary clicks work correctly.

Offer a compact list view if a growing library makes large path cards wasteful.
Thumbnails remain optional recognition aids, not the purpose of the library.

### Editor: one sequence, one contextual detail area

Preserve the approved two-column layout rather than adding a permanent third
inspector. Keep the sequence header/Add controls anchored; the sequence owns its
scroll area. The detail pane has a predictable bounded viewport.

Normal details show the selected action's visual, concise editable fields, and
relevant warnings. Exact input opens a dedicated detail tab/view with a clear Back
to action control and its own bounded event table. Do not nest that table inside a
scrolling page. Raw selection and unfinished drafts survive switching views.

At narrow widths, use Sequence / Details navigation rather than piling every pane
into one tall document. Keyboard users need explicit commands to switch panes,
retain selection, and return to the selected action. High contrast, text scaling,
visible focus, and scrollbar hit targets remain acceptance requirements.

### Author actions in the same language used to review them

The primary Add menu should offer useful semantic actions: click, shortcut, pointer
movement, and Wait until. Exact mouse/keyboard event creation moves under Advanced.
This is an authoring proposal, not permission to infer typed text from key events
or silently rewrite captured input. Preserve original event order and exact data.

Conditional waits read like a sentence: "Wait until the Export window is visible"
or "Wait until this pixel changes". Show timeout and stability as options; explain
that waiting does not focus a target or redirect subsequent input. A test command
is explicit and bounded; opening or previewing a recording never observes targets.

Use "Pause before" for a fixed delay, "Execution time" for movement duration, and
"Wait until" for an observed condition. Do not present a wait's timeout as its
known duration. Keep microsecond precision available without six decimals in every
summary. A conditional wait is an action; pointer-origin setup is recording
metadata, not a fake event row.

### Run frequently; edit occasionally

Make Play the primary transport action in a saved recording. Preview is secondary
and visibly simulated. Do not restore obligatory preparation dialogs. Show compact
playback settings for speed, repeat count, countdown and recorded/current starting
point, with scopes made explicit. Editing them never modifies captured events.

Record starts a new task using saved defaults. Append/replace an existing recording
remains deliberate and distinct. On completion, preserve the captured task and let
the user name/tidy it without making a name a prerequisite to recording.

During execution, show only truthful state: countdown, playing, waiting, stopped,
or a specific failure. Stop stays prominent. For waits, show the condition and time
remaining, not an invented percentage-complete estimate.

## Suggested implementation order after approval

1. Direct library Play and checkbox-first selection, with no accidental execution.
2. Stable editor panes and a dedicated Exact input view, preserving draft ownership.
3. Semantic Add actions and consistent timing labels, integrating conditional waits.
4. Compact list view, clearer setting scopes, and optional keyboard efficiency work.

Keep the current palette and spacing system. Validate actual pointer/keyboard
interaction as well as layout; setting SelectedItems directly is not evidence that
successive user clicks work. Automated verification remains non-visible and uses
temporary data/fake engines. Visible application testing stays with the user.
