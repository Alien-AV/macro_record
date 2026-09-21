# Selection, capture, scrolling, and conditional waits

Status: all three regressions and the first window/pixel wait slice integrated
locally on master through 2e6c741 (2026-09-21), based on c2c0fca.
Reports are local; no GitHub writes or push. No visible application launch or real
input capture/injection is authorized for development verification.

## Regression acceptance

- In explicit recording selection mode, successive ordinary clicks accumulate
  independent selections and another click deselects. Selection is visible and
  accessible. Normal library opening, filtering, select-all, trash restoration,
  and batch deletion retain their existing safety/recovery behavior.
- A confirmed registered stop hotkey excludes its otherwise-unused modifier and
  trigger even with intervening pointer motion. Motion and timing are preserved.
  Genuine modified clicks/drags/typing, ordinary stops, and invalid provenance are
  not mistaken for stop commands. Test high-rate/long motion and bounded resources.
- Expanded exact input has a reachable scrollbar that does not share its hit area
  with an outer scrollbar. Short/narrow and normal desktop sizes remain usable;
  current selection, drafts, click-away focus, and keyboard access survive.

## Conditional-waits delivery

The first usable slice is the shared execution/format/editor foundation plus
window and pixel conditions. Follow docs/state-waits-proposal.md, reconciled with
the current versioned .macro codec and origin metadata. Accessibility text, memory,
and OCR are later phases; no inert provider placeholders should be presented as
working features.

Verify whole-document preflight, old-reader rejection, exact/unknown data
preservation, undo, cancellation, native deadlines, stale responses, repeated
occurrences, post-wait scheduling, and simulated preview. Providers are tested
through fakes, not uncontrolled live desktop observations. Wait-only recordings
must not inject startup key releases. Unsupported waits must not be silently
removed by legacy export.

## Review and integration

Implementation workers use separate repo-local worktrees with fresh context.
Independent reviewers are read-only and need no worktrees. Integrate full reviewed
commit ranges, inspect overlapping files semantically, run combined Debug/Release
managed and native tests, and refresh/verify the published app without launching it.
Broader UX changes in interaction-rework-proposal.md remain proposals.

## Manual acceptance after delivery

The selection/scroll fixes are integrated at bef09bf; capture fixes at
1574ac0, 00eb6bc, and 7dd5f90; the full wait range runs from 96fc541 through
2e6c741. Independent capture, wait backend/format, provider/UI, and cross-feature
reviews have no remaining production findings. Reported findings were fixed and
re-reviewed, including the deterministic UI-test synchronization follow-up.

The coordinator's four additional integration tests cover wait/origin adoption,
bulk trash/restore, recovery and exact export, pointer relocation/speed, prepared
native wait indices, consecutive waits, and finite repeats. Existing capture
tests also verify bounded backlog delivery and failure recovery without depending
on a successful Windows wake-up post.

Final combined verification: full x64 Debug and Release solution builds passed;
601 managed tests and 102 native tests passed in each configuration, with no
skips. The existing MSB3851 managed/native Windows SDK target mismatch warning
remains; it did not fail these builds. No live capture, injection, condition
observation, or visible application activation was performed.

The self-contained Release publish at x64/Release/publish was refreshed with no
MacroRecorderGUI process running. Hash verification matched eight key app/runtime
artifacts and all eight compiled XAML files to the Release build, plus the native
DLL and original mouse.ico source. This verifies distribution contents, not live
application behavior.

- In Select recordings, click three cards without modifiers, uncheck one, and
  delete the remaining two. Undo should restore both. Check keyboard arrows/Space
  and selection scope after searching.
- Record continuous mouse motion while pressing Ctrl+W to stop. The final Ctrl
  press must not remain; motion must. Also record a deliberate Ctrl-click/drag and
  ordinary Ctrl typing to check that genuine modified input remains.
- Expand Exact captured input in a long recording. Drag its scrollbar at normal
  and short window heights; the inspector/page scrollbars must not intercept it.
- Add a window or pixel wait between actions, edit it, undo, save/reopen, and
  explicitly Test it. Cancelled dialogs must preserve unrelated raw drafts.
- Preview waits without observing the desktop. Step consecutive waits using
  Simulate satisfied / Next; restart a wait-only preview and check its cursor
  does not jump to movement after an unresolved condition.
- In a disposable target workflow, verify real playback waiting, timeout, and
  emergency Stop. A wait never brings the target to the foreground. Text/memory/
  OCR providers and target picking are not part of this first slice.

These checks require user-driven visible interaction and were not substituted
with automated input injection. Hidden-control tests verify state/layout and
fake-provider tests verify execution semantics, not live desktop behavior.
