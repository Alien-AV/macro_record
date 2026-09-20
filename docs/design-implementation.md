# Polished desktop design

The implementation target is the original **Independent macro recorder UX design**
prototype, not the later standard-WinUI comparison. WinUI remains the native UI
framework; the approved appearance uses intentionally styled controls.

## Visual specification

- Narrow dark navigation rail with a recording-library entry and bottom-aligned
  appearance/settings commands.
- A compact breadcrumb and recording heading, truthful save status, Undo, and
  New recording. Preserve native caption buttons, dragging and resizing; match
  the title bar to the active theme.
- A two-column editor: readable action sequence on the left; selected action,
  pointer path or input visualization, timing/position fields, and expandable
  exact input on the right. Avoid a separate permanent third inspector column.
- Searchable library cards with real names, summaries and path thumbnails.
- A separate visual-preview mode with current/next action, a scrubber and
  action timeline. Preview never records or sends input.
- Explicit recording/playback dialogs and a compact run controller with state,
  elapsed time and Stop. Playback is clearly identified as controlling other apps.
- Light/dark palettes, restrained borders, rounded controls, Segoe typography,
  and the spacing/hierarchy of the approved HTML. High contrast and keyboard
  focus must remain usable rather than being forced into a fixed palette.

The reference HTML lives in the independent design task at
`C:/Users/Alien_AV/Documents/Codex/2026-09-18/propose-an-independent-ux-and-visual/outputs/design-proposal.html`.
Its outer design-study navigation and fictional desktop backdrop are not app UI.

## Behavioural acceptance

- Preserve `.macro` protobuf compatibility, raw event order, microsecond delays,
  event-selection scope, undo, safe geometry editing and coordinate frames.
- Display observed key input, not inferred typed text. Relative device counts
  must never masquerade as screen pixels; unavailable geometry stays unavailable.
- Keep import/export and advanced raw editing accessible. No mock save indicators,
  hard-coded recording cards, fake progress, inert settings or simulated run buttons.
- Persist library entries/drafts safely. Save errors retain recoverable data and
  must not display success; reopening must not silently discard unsaved edits.
- Speed/repeat/countdown controls must not mutate recorded events. Cancellation,
  native playback errors and shutdown must prevent any later queued playback.
- Recording and playback are mutually exclusive. Preserve suppression of the
  recording-start hotkey releases. Never change displayed shortcuts without
  changing registration and checking conflicts.
- Stop remains reachable throughout countdown and real execution. Closing a run
  controller must not leave capture/playback running invisibly.
- Retain the desktop-compatible theme monitoring introduced after the startup
  regression. Do not use `AccessibilitySettings.HighContrastChanged`.

## Verification policy

Implementation is isolated in worker worktrees and reviewed before integration.
Use automated fake-engine/temporary-storage tests, builds, and non-visible resource
checks. Do not launch the recorder or display native test windows. Full visual and
real-input smoke testing remains with the user. Reports and commits stay local;
no GitHub publication or push is part of this task.

## Screenshot-driven polish acceptance

- Recording/playback dialogs give normal settings sufficient room, reserve a
  scrollbar gutter when needed, and keep the stop shortcut discoverable. Primary
  actions use blue; Cancel is neutral. Focus and high-contrast states remain clear.
- Library thumbnails represent meaningful recorded movement rather than only an
  initial positioning sample. Coordinate frames remain separate and accurately
  labelled; recordings without a useful path get an honest input summary.
- New recordings have useful distinct default names. Renaming is discoverable,
  cancellable, and uses the existing persistence/error-handling workflow. Existing
  recording names are not rewritten. Onboarding copy belongs to the empty library.
- The inspector distinguishes selected movement from contextual movement. An
  action without movement must not appear to own an unrelated trace.
- Wait-before and action duration are labelled separately and relevant timing
  fields are immediately available. Single-event actions do not expose an
  irrelevant disabled duration. Exact microsecond editing and Undo are preserved.
- Secondary text is readable; detailed technical explanations are progressively
  disclosed without hiding safety warnings. Event labels use correct plurals.
- The run controller has a legible steady-width timer, concise truthful state,
  and prominent Stop. It does not invent playback progress or iteration counts.

## Implemented and verified

Integrated editor, library/workflow services, shell, and screenshot-driven polish
were independently reviewed. All actionable findings were fixed before final
approval, including sparse movement lost by display sampling, a hidden geometry
safety warning, and cancellation during initial library loading. Final verification
on 2026-09-20 after the polish integration:

- Full x64 Debug and Release solution builds pass. The existing MSB3851 Windows
  SDK target-version mismatch warning remains unchanged.
- Each configuration passes 350 managed tests and 48 native fake-input tests.
- An isolated diagnostic loads production application resources but overrides
  production startup. The compiled editor exercises mixed-input selections and
  preview at widths 1100, 700 and 420 in light/dark themes. Populated library cards
  cover movement and keyboard fallback. The compiled dialog is measured with
  overflowing fields at 488x600 and 280x400; its safety footer stays in bounds and
  its scrollbar gutter is checked. Main window construction uses fake engines,
  a temporary library and no global hotkeys; main/controller HWNDs are verified
  invisible. No window is shown or activated.
- Self-contained Release publish is refreshed and checked against build hashes,
  including all eight compiled XAML resources. No push or GitHub update was performed.

These checks do not establish pixel fidelity, full production startup, live target
focus, screen-reader interaction or real recording/playback behaviour. Those remain
the user's manual smoke test. No recorder was launched or real input captured/sent.

Current boundaries: appearance and emergency-shortcut selections are session-local;
use the recording stop hotkey to avoid capturing a controller click. Preview uses
captured timing. Relative motion and standalone trailing waits retain the existing
format limitations. Native caption affordances remain Windows-controlled.
