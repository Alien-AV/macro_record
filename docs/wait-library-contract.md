# Wait library integration contract

Namespace `ProtobufGenerated` is now owned by assembly `MacroRecorder.Waiting`, referenced by the WinUI app. No WinUI dependency in that library. The initial foundation deliberately rejects the newly defined wait sources until their backends and validators are installed.

## Fixed Delay (editor contract)

`MacroRecorderGUI.Event.DelayEvent(ulong durationMicroseconds)` and `.DurationMicroseconds` (get/set `ulong`), `.TimeSinceLastEvent` retains its existing pre-delay meaning. Input oneof field 5 is `Delay` (`FixedDelay.DurationMicroseconds`, field 1). Duration range zero through 24 hours. Playback and preview consume pre-delay plus duration. Playback speed scales both with the existing rounding rule and no forced minimum; values over 24 hours after scaling are rejected. Explicit zero Delay is valid and remains an editable event. No sink call, no input release during the delay. Only native session shutdown performs ordinary cleanup. Delay-only streams never issue startup releases. Raw legacy timings are unchanged.

New Delay/new-source saves require document version 4. Window/pixel-only waits remain version 3, semantics 1. Raw/version 2/3 imports containing newer actions reject; untouched original byte preservation remains owned by the document workflow. Legacy export rejects new actions.

## Conditions (forms / picker contract)

`WaitCondition.AccessibilityText` field 8, `.OcrText` field 9, `.Memory` field 10; all require semantics version 2. Version 2 is for these sources only; version 1 window/pixel stays unchanged. New sources support IsTrue, BecomesTrue and Changes; never NewWindow. Minimum polling: accessibility 250 ms, OCR 500 ms, memory 100 ms. Timeout/stability bounds are unchanged.

`AccessibilityTextCondition`: `Target` WindowSelector (single window), `Element` AccessibilitySelector (`AutomationId`, numeric UIA `ControlType`), `Ancestors` nearest-parent first, maximum 16; `Source` AccessibleName/TextPattern/ValuePattern; `Predicate` TextPredicate. Element identification is independent of observed text. TextPredicate has Expected, Comparison (TextEquals/TextContains/TextNotEquals/TextNotContains), IgnoreCase, Whitespace (PreserveWhitespace/CollapseWhitespace). At most 32768 UTF-16 characters read; overflow is incomplete, never a negative match.

`OcrTextCondition`: `Region` OcrRegion (`Coordinates`, `Target`, `X/Y/Width/Height`, `ReferenceDpi`), `Language` (one local Tesseract identifier), `Predicate`. Maximum one megapixel and 4096 per dimension, physical dimensions checked again after logical scaling. Desktop regions have no Target. Client regions require one window. Local Tesseract 5 executable and installed tessdata are configured outside the document; no download or network code. OCR does not fall back from accessibility.

`MemoryCondition`: `ExecutablePath` (full path), address oneof `AbsoluteAddress` or `Module` (`Path` full path, exact `FileVersion`, `Offset`), `PointerOffsets` signed 64-bit offsets, maximum 16. Start at absolute address or module base + Offset. Each offset dereferences a pointer at the current address, then adds the offset; final address is read as one scalar. Pointer width comes from the bound target (32/64-bit), never from host assumptions. `ScalarType` Uint8/Int8/Uint16/Int16/Uint32/Int32/Uint64/Int64/Float32/Float64, `Comparison` NumericEquals/NumericNotEquals/Less/LessOrEqual/Greater/GreaterOrEqual, `Expected` invariant decimal string (exact 64-bit integers), `Tolerance` finite nonnegative double (floating equality/inequality only; integer tolerance must be zero). Process binds once per occurrence; no replacement process after exit.

Local opt-in hook: `MacroRecorder.Waiting.WaitServices.LocalSettings.MemoryEnabled`, default false. Full immutable settings snapshot at `.Options`: `WaitLocalOptions(MemoryEnabled, TesseractExecutablePath, TessdataDirectory)`. Nothing in protobuf can grant these permissions or choose the executable/model directory. The application persists local options through its own WaitSourcePreferences and initializer; the reusable library does not persist settings.

Backend descriptions will be `MacroRecorder.Waiting.WaitValidation.Describe(WaitCondition)`, validation `.Validate`, constructors `.NewAccessibilityText()/.NewOcrText()/.NewMemory()`. These constructors produce editable defaults requiring target completion. Shared runner `.Desktop` is used by playback and explicit Test; Preview must never call it.

## Delegated accessibility backend contract

Implement public parameterless `WindowsAccessibilityTextBackend : IAccessibilityTextBackend` in `MacroRecorder.Waiting/WindowsAccessibility*.cs`, plus disjoint tests. The foundation defines the interface and DTOs in `ProviderContracts.cs`.

ReadAsync receives transient WaitWindow (HWND, PID, creation identity), definition and cancellation. Success returns complete text and stable element runtime identity; unavailable/error has no usable text. Revalidate window/process before and after lookup/read. Lookup and choice enumeration are scoped to that window, bounded to 512 elements and depth 16. Exact parent selectors are nearest-first. Multiple matches are errors. No automatic name/text fallback. Password controls must not be read. Text limit 32768 UTF-16 characters (request one extra to detect truncation). All UIA calls, releases and subscriptions, if any, stay on one dedicated MTA with finite UIA transaction timeouts; cancellation must not create abandoned workers. Implement GetChoicesAsync for explicit picker use, with labels from identification fields and ancestors. Construction must not touch UIA until explicit reads/choices. Disposal may wait for outstanding work; the runner retains its shared permit until read, cancellation callbacks and disposal all complete.

The coordinator owns cross-worker integration. Do not edit schema, shared contracts, runner, composition, project files, UI or other provider files in the accessibility worker; request any required project references from the coordinator.
