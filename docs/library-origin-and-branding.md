# Library deletion, application identity and playback origins

## Accepted scope

- Delete one recording or select several recordings and delete the selection in
  one operation. Selection and deletion must be distinguishable from opening a
  recording, usable with the keyboard, and clear about the selected count.
- Deletion is recoverable. Never delete an imported/exported source `.macro`
  file, allow a recovery backup to resurrect a deleted item, or let a delayed
  save recreate it. Live recording/playback targets must remain protected.
- Restore the application title **Macro Recorder** and the original `mouse.ico`
  window icon in the main window and run controller. Preserve the current design.
- Treat a newly captured starting pointer position as recording metadata, not
  as an ordinary action. Existing macro events must not be guessed away.
- Remember a playback origin choice per recording: recorded starting point, or
  current pointer sampled after the countdown. Repeats reuse the selected run
  origin. Relative device counts stay relative; genuine absolute coordinates
  are translated safely without pretending counts are screen pixels.
- Legacy recordings require explicit adoption of their first positioning event
  before treating it as metadata. Preserve old behavior by default and explain
  missing or incompatible origins rather than silently misplaying them.
- Keep the `.macro` extension. Version new document contents so older players
  reject unsupported semantics; continue importing legacy `.macro` files.
- State-change waits remain a separate planning exercise, not implementation
  scope for this pass.

## Verification and coordination

Implementation is delegated in isolated local worktrees and independently reviewed
before linear integration. Library lifecycle, window identity and playback-origin
work can proceed in parallel; shared main-window and serialization changes require
an explicit integration check. No GitHub writes or pushes.

Automated tests use temporary libraries, fake engines, fake cursor/clock providers
and invisible diagnostic windows only. Never delete real user recordings, launch
a visible app, capture real input or inject playback during verification. Check
the combined Debug/Release solution, persistence and exported-file compatibility,
then refresh and verify the published executable and its resources.

## Combined regression and manual checks

Automated combined verification must exercise a versioned recording through save,
delete, restore, export, and re-import. Its origin boundaries and untouched bytes
must survive, and restoring the original library ID must retain playback options.
Appending capture to an imported legacy recording must not remove its earlier
events or later capture boundaries. Explicit origin adoption, editing, undo, and
recovery must retain a coherent document. Preview never sends setup input.

The following are user-run smoke checks, not permission to launch the app during
development:

- Select several library cards, use Ctrl/Shift and Select all visible, narrow the
  search, and verify the count and delete operation cover only visible selections.
  Restore a batch through Undo and through Local trash after restarting.
- Confirm normal clicks still open recordings and imported/exported source files
  remain untouched. Check narrow layouts, keyboard access, and partial failures.
- Confirm both windows say Macro Recorder and show the original icon, without
  changing the dark/light title-bar treatment.
- Capture a small pointer gesture: no synthetic positioning action should appear.
  Compare playback from its recorded start versus the pointer position chosen
  during countdown. Repeat must not drift from pass to pass.
- Open a legacy macro: its events remain unchanged until explicit origin adoption.
  Verify adoption/undo/recovery, appended captures, negative monitor coordinates,
  and helpful rejection of destinations outside the applicable screen frame.
