# Direct recording and playback

## Interaction contract

- Record and Ctrl+Q immediately prepare a new, automatically named recording and
  start its cancellable countdown. No name or settings form is required.
- Play and Ctrl+E immediately prepare playback of the selected recording using
  its saved speed, repeat and countdown settings. No confirmation form is required.
- Adjacent options controls change preferences without starting a run. Cancel
  discards changes. Recording defaults are shared; playback preferences belong to
  the recording and survive switching documents and restarting the application.
- The playback summary exposes speed, repetition and delay before the user starts.
  Repeat-until-stopped is explicit and conspicuous, never an invisible mode.
- Preview is visual only and keeps captured timing. Play sends real input to the
  target application after the countdown; its tooltip/accessibility text explains
  this distinction. Stop remains available through the controller and shortcut.
- Record into an existing document remains an explicit command with deliberate
  append/replace semantics. New empty recording remains available for manual edits.

## Safety and compatibility

Every requested run owns cancellation before its first asynchronous operation,
including preference/library loading and saves. Stop or close prevents a pending
continuation from creating a controller or beginning capture/playback. The selected
recording and run settings must remain consistent across preparation awaits.

Preferences do not alter macro events, recorded timing or the `.macro` wire format.
Existing recordings without preferences default to speed 1, one repetition and a
three-second countdown. Invalid or unreadable preference data must not silently
activate unexpected settings or an infinite loop. Persistence errors stay visible.

## Verification

Use fake engines, temporary stores and automated builds/tests. Never activate the
application or send real input during development verification. Independently
review the final implementation before integration and keep reports local.

The user smoke test should check direct Record/Play and their keyboard shortcuts;
options Apply/Cancel without execution; persistent settings for two different
recordings; visible until-stopped state; countdown Stop; and the distinct visual
Preview action. Check narrow layout and keyboard access to the options controls.
