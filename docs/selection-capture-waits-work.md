# Selection, capture, scrolling, and conditional waits

Status: implementation in progress, based on c2c0fca (2026-09-20).
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
