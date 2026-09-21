# Full interaction rework delivery

Approved 2026-09-21. Base: 37e358d. Status: implemented, reviewed and integrated.
The full scope is docs/interaction-rework-proposal.md, retaining the approved
visual design and WinUI framework. This is local-only work; no GitHub writes,
push, visible app launch, live capture/injection, or live wait observation.

## Parallel implementation and acceptance

1. Library and run controls: direct explicit Play and adjacent settings on cards
   and compact list rows; body opens editor independently of checkbox selection;
   always-visible checkboxes and contextual count/delete/restore; Escape clears;
   search cannot silently delete hidden items; existing trash/Undo and protected
   dirty/running targets retained. Compact list option; concise per-recording run
   preferences including indefinite repeat/countdown. Play is primary, Preview
   secondary and simulated. Recording-default and per-recording scopes explicit;
   new Record remains direct, append/replace deliberate, Stop truthful/prominent.
2. Editor: bounded sequence/detail panes with anchored controls, no outer nested
   page scrollbar; dedicated Exact input view and clear return, independently
   bounded event table; selection and unapplied drafts survive view switching.
   Narrow Sequence/Details navigation and keyboard pane switching preserve focus.
   Timing labels distinguish Pause before, Execution time, and Wait until;
   technical details recede without hiding important warnings. Conditions read
   naturally, with timeout/stability/options clearly separate from known duration.
3. Authoring: Click, Shortcut, Pointer movement, Wait until as primary Add actions;
   raw mouse/keyboard construction under Advanced. Explicit authoring inserts
   balanced input at the intended boundary with undo and validation. Never infer
   text or rewrite recorded input. Preserve origins, waits, unknown fields, and
   exact bytes where unaffected.

## Verification and integration

Fresh-context implementation workers use coordinator-created repo-local worktrees.
Read-only reviewers use no worktrees. Review complete actual commit ranges, fix
findings, integrate linearly, then verify combined Debug/Release managed/native
suites. Cover narrow/short layouts, visible focus, keyboard selection/navigation,
high contrast/text scaling, and command safety using non-visible compiled-control
checks and fakes. Such checks do not substitute for user-driven pointer testing.
Refresh and hash-check the self-contained Release publish without starting it.
Track final coverage, test counts, residual limits, and integration commits here.

## Review record

- Semantic model: independent full-range review found an X-button state mismatch;
  zero-valued legacy payload now means X1, matching native playback. Five regression
  rows cover all three authoring APIs. Rereview clean; integrated as f155cac and
  995c47d.
- Cross-feature authoring tests: fc12077 covers save/export, unknown protobuf data,
  origins/recovery, Undo, bulk trash/restore, and target-specific run preferences
  with fake engines and temporary stores. Both configurations passed these checks.
- Coordinator hidden handoff test review caught assertions inside a production
  catch-all. It now records callback observations and asserts outside that boundary,
  so incorrect targets/settings cannot be swallowed as ordinary UI feedback.
- A separate acceptance audit found no omitted proposal items across the candidate
  branches. Its documentation corrections distinguish trailing conditional waits
  from unsupported trailing fixed delays and describe explicit raw Apply/Discard.
- Editor review found hidden raw deletion scope, primary-row draft loss after
  out-of-order selection, and unselected Advanced Add events. All three have
  regression checks and clean independent rereview. Integrated full editor work
  as 8758085 and 5090adb, without duplicating the already-integrated model commits.
- Library review found oversized checkbox layout, stale settings after partial
  saves/cancellation or rejected close, and disabled controls after timer-driven
  completion. All paths have regression checks and clean independent rereview.
  Full library range integrated as 3239206, 86b6453 and eccd257.
- Combined hidden-shell verification passes with the integrated editor and library:
  pending raw/condition drafts and modal ownership block both card Play/options;
  applying the draft releases the gate while preserving the requested target,
  remembered preferences and active editor. The injected handoff never enters
  the real controller or input engine.

## Final verification — 2026-09-21

- Full x64 Debug and Release solution builds passed. The existing MSB3851 managed
  19041/native 26100 SDK target warning remains unchanged.
- Both configurations passed 679 managed tests and 102 native fake-input tests,
  with no skipped managed tests. The combined hidden-control checks use fake
  engines, isolated stores and unactivated windows; controller/input handoffs are
  intercepted by mandatory test callbacks.
- Self-contained Release publish refreshed at x64/Release/publish. Eight selected
  binaries/resources/icon files and all eight compiled XAML files match build
  hashes; the native DLL and original mouse.ico also match their source outputs.
- Implementation worktrees/branches were cleaned after integration; reviewers used
  no worktrees. Commits preserve the work. Workers and reviewers were closed.
  No preview servers or dashboard entries were created.
  No GitHub writes or pushes, visible app launch, real capture/injection, or live
  desktop condition observations occurred.
- Actual physical/routed keyboard and pointer behavior, screen-reader interaction,
  pixel fidelity and real OS text scaling/high contrast remain user smoke tests.
  Hidden layout/resource checks are not a substitute for those interactions.

## User interaction checks

Automated verification does not show or activate the application. The remaining
physical interaction checks are:

- Check several cards in succession, open a different card body, and verify that
  neither checkbox nor body starts playback. Try the same controls in compact
  list and Local trash. Search must drop hidden selections; Escape clears them.
- Use a card's Play with another document open. Its own remembered speed, repeats,
  countdown and pointer origin must apply; completion returns to the library.
  Options must never start a run, and Stop must remain reachable during countdown
  and conditional waiting.
- Resize the editor while editing a field. Sequence/Details and F6 must preserve
  selection and draft ownership. Exact input and Back to action retain raw drafts;
  Play/Add wait for explicit Apply or Discard. Test scrollbar dragging at narrow
  and short sizes, increased Windows text size, and high contrast.
  Select a later raw row before an earlier one, type a draft, and return: the
  primary row and its draft must remain the same. Delete from Sequence must target
  selected actions, not a remembered raw subset hidden in Details.
- Add each semantic action, edit its timing, then Undo. Inspect Exact input if
  desired: balanced clicks/shortcuts, explicit coordinate frame for pointer moves,
  and no changed surrounding capture. Wait until must show a condition and options,
  not an estimated execution duration.
