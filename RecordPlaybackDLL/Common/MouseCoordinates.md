# Mouse coordinates and payloads

Absolute `x`/`y` values in newly recorded macros are physical desktop pixels.
Raw Input absolute values are normalized from 0 through 65535 and are translated
using primary bounds or virtual desktop bounds according to `MOUSE_VIRTUAL_DESKTOP`.
Virtual bounds include their possibly negative left/top origin. Bounds are read
in a temporary per-monitor DPI awareness context and the previous context is
restored. Initial cursor anchors use `GetPhysicalCursorPos` and virtual mapping.
Replay selects the same desktop, subtracts its origin, clamps out-of-bounds
coordinates, and uses 64-bit arithmetic to normalize them. Interior positions
select an integer inside the pixel's normalized interval; endpoints use 0 and
65535. For pixel offset `p` and dimension `s`, the inclusive integer interval is
`ceil(p * 65536 / s)` through `ceil((p + 1) * 65536 / s) - 1`, capped at 65534
because 65535 is reserved for the last pixel. Selecting the middle integer in this
interval preserves every pixel for dimensions up to 65536, including widths such
as 38400 where truncating the continuous centre can select the preceding pixel.

Desktops wider/taller than 65536 pixels cannot represent every pixel exactly.
Representable pixels still round-trip exactly. If a pixel's interval is empty,
replay selects the nearest representable pixel, with ties choosing the lower
normalized coordinate. This quantization is unavoidable; bounds and endpoints
remain clamped without overflowing even at the limits of the stored coordinates.

`mappedToVirtualDesktop` controls absolute replay bounds and
`MOUSEEVENTF_VIRTUALDESK`; it has no effect on relative moves or button-only events.
Movement is emitted only for absolute packets or nonzero relative deltas. Empty
raw packets do not reset elapsed event time. A compound raw packet emits movement,
button transitions, vertical wheel, then horizontal wheel. Only its first emitted
action carries the elapsed time. Raw Input does not supply an internal ordering
for simultaneous actions, so this order is deterministic.

## Unchanged protobuf representation

No field numbers or wire types change. `wheelRotation` (uint32 field 4) holds the
`SendInput.mouseData` bit pattern: signed wheel delta for `Wheel` (0x800) or
`HorizontalWheel` (0x1000), and `XBUTTON1` (1), `XBUTTON2` (2), or their mask for
`XDown`/`XUp` (0x80/0x100). New captures split actions whose payloads would collide.
Buttons and wheel events never introduce movement. Replay also splits compound
stored action flags before submitting a batch, and reports partial submission as
failure without retrying the batch.

Old zero-payload X actions mean X1: the old recorder only captured button 4 and
omitted its payload. This is a bounded compatibility fallback. Legacy files remain
readable, but these historical ambiguities cannot be repaired automatically:

- Absolute values may be physical pixels, DPI-virtualized pixels, or normalized
  raw device values. There is no units/version field to distinguish them.
- Old initial cursor anchors omitted virtual mapping. Coordinates alone do not
  prove the intended desktop or original monitor arrangement.
- Old wheel recordings omitted the wheel action flag and stored negative deltas
  without sign extension. A nonzero payload alone is not proof of a wheel event.
- A legacy X action combined with a wheel payload can have lost button identity.

Loading does not guess or migrate any of these values. Explicitly marked wheel
events replay their exact stored 32-bit payload. The X1 zero-payload fallback is
the only implicit legacy interpretation.

## Relative conversion and limits

Relative recordings contain raw device counts, not measured physical cursor
displacement. Windows pointer speed/acceleration can change relative replay.
Summing those counts into pixels estimates a path; it cannot reconstruct an exact
physical path. Conversion requires a preceding absolute move, ignores button/wheel
positions, saturates sums rather than overflowing, and marks converted moves as
virtual-desktop positions so they can cross monitors. Unanchored relative moves
remain relative. Original absolute moves retain their mapping and provide a new
anchor. No monitor layout or acceleration settings are stored in a macro.

## Acceptance cases for a later authorized interactive session

Automated tests use constructed raw packets, supplied bounds, and fake sinks only.
No application launch or actual input is needed for those tests. When interactive
testing is separately authorized, check these scenarios:

1. Primary display at 100%, second display at 150% or 200%, placed left and then
   above primary: absolute capture/replay should reach matching physical positions
   including negative coordinates and all desktop edges.
2. An absolute Raw Input device in primary and virtual modes: the full normalized
   range should span only the selected surface, without a second normalization of
   already-normalized raw values.
3. Start recording on a secondary display, then use relative movement across the
   display boundary: the initial anchor should remain on that display. Evaluate
   relative and converted playback separately with acceleration both on and off;
   exact cursor-path agreement is not guaranteed by count summation.
4. Scroll both directions on vertical and horizontal wheels, including small
   high-resolution deltas, and press/release X1 and X2, including while moving.
5. Replay old pixel-based macros and zero-payload X1 actions. Ambiguous legacy
   normalized/scaled absolute files may need explicit user correction.

## Win32 references

- [RAWMOUSE coordinate, wheel and button semantics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse)
- [MOUSEINPUT normalized coordinates and shared mouseData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [Physical cursor position](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getphysicalcursorpos)
- [Thread DPI awareness context](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext)
- [Per-monitor awareness and physical pixels](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
