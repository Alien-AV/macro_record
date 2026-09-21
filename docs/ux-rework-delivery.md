# Full interaction rework delivery

Approved 2026-09-21. Base: 37e358d. Status: implementation in progress.
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
