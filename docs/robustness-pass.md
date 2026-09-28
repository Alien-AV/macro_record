# Coverage-led robustness pass — 2026-09-28

Base: e2e6f14. All four candidates independently reviewed, integrated, and
verified together in Debug and Release.

Scope: reproduced bugs, meaningful missing failure-path tests, and small fixes
that preserve the approved UX and data contracts. No framework/dependency
upgrades, broad abstractions, live desktop observation or actual input injection.
Work and reporting remain local; no GitHub writes or push.

## Baseline and measurement limits

The last accepted suite contained 694 managed and 103 native tests. This pass's
first managed coverage run passed 693 but the hidden WinUI child failed with
exit 0xC000027B, also reproduced without coverage after an incremental build.
The cause was the test child inheriting its packaged host's resource identity.
The test-only resource-map fix restores ordinary hidden checks; the normal test
is not skipped or weakened to obtain a green result.

A separate core coverage run explicitly excluded that one child-process test
and passed all 693 selected tests. This provides a repeatable measurement of
in-process managed code, not a claim to cover WinUI interaction or real Win32
behavior. Existing hidden control tests and native fake-sink tests are separate
verification layers. Generated protobuf/XAML code is not a target for percentage
improvements. Counts below union sequence-point line numbers per source file.

| Target | Baseline covered lines | Rationale |
| --- | ---: | --- |
| NativeRecordingTransport | 0 / 37 | Capture tests previously replaced the adapter entirely. |
| WindowPixelObserver / WindowsWaitDesktop | 32 / 119 | Predicates tested; native observation and failure handling mostly untested. |
| GlobalHotkeys | 67 / 87 | Native-only lines plus replacement/validation branches. |
| DesktopThemeMonitor | 21 / 28 | Subscription rollback and callbacks. |
| EditorFieldCommit | Not measured in-process | Covered through the hidden child, not ordinary unit tests. |

Core persistence/editing code already had high line coverage; test count alone
does not prove its boundary cases correct. Workers should prefer behavioral
assertions about ownership, failures, exact data and resources over executing
uncovered getters. Native wrappers remain unmeasured unless exercised through
safe fake API seams; no real observation is authorized for this pass.

## Verification commands

Build the mixed solution using VS18 MSBuild, then run the ordinary managed and
native suites in both Debug and Release as documented in README. For comparable
core coverage, using the existing test SDK's installed collector:

```powershell
dotnet test MacroRecorderGUITests/MacroRecorderGUITests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --filter 'FullyQualifiedName!~HiddenControlsCommitBeforeCommandsAndPreserveDraftsAndSelection' --collect 'Code Coverage;Format=Cobertura' --results-directory artifacts/robustness-coverage --verbosity minimal
```

The exclusion is for this measurement only. The full ordinary suite, including
the hidden child, remains an integration gate. No collector package or global
tool was installed. Independent reviewers use existing worker checkouts without
new worktrees; complete reviewed ranges are integrated linearly before combined
tests and self-contained publish/resource validation.

## Findings and candidates

- **Shortcut validation:** `AddHotKey` accepted a null handler, registered it,
  then failed during dispatch. A one-line pre-registration guard fixes the
  reproduced failure. Eight new cases exercise validation, timestamps,
  replacement/rollback, retained emergency-stop provenance, disposal and late
  dispatch. Candidate 3afe0a7 was independently reviewed clean and integrated
  as 82a00eb; the coordinator's 10 focused Debug tests passed.
- **Pixel waits:** a target that became cloaked after enumeration could supply
  the color of the desktop behind it. A small sampling seam permits fake-only
  tests of current visibility/identity and DC/DPI cleanup. Current visible,
  non-minimized, non-cloaked state is required before and after sampling.
  Sixty added cases cover observation, coordinates, occlusion and failures.
  Candidate 3a3342a was independently reviewed clean and integrated as 1e82b0b;
  the coordinator's 66 focused Debug tests passed.
- **Hidden UI harness:** fresh builds reproduce a resource-map failure when
  the child inherits a packaged host's identity. Explicitly selecting the
  test's compiled PRI restores normal hidden checks without launching the app,
  changing the foreign working directory, or weakening assertions. Additional
  hidden scenarios cover invalid coordinate drafts and retry after save failure.
  Coverage instrumentation still disrupts a compiled-template check; the
  comparable core measurement excludes this child and normal full tests do not.
  Candidate 2c04e27 was independently reviewed clean and integrated as f35c055.
  The first combined Debug run passed all 762 tests, including this diagnostic.
- **Capture boundary:** reproduced malformed-input and subscriber exceptions
  now request a stop and fail the session at its actual native terminal boundary,
  never reporting a successful partial capture. Invalid/oversized callback
  buffers are rejected before allocation or unmanaged reads. Callback delegates
  remain rooted until a successful native join. Disposal retry remains possible
  through the transport, capture owner, and VM; teardown no longer detaches
  callback destinations before the join succeeds. The small native API seam
  allows fake-only failure and ownership tests. Candidate 07ed7df was independently
  reviewed clean and integrated as b479247. The reviewer passed 50 focused
  adapter, recording/finalization, and playback lifecycle tests in each config.

## Added regression coverage

88 new managed cases: 60 wait-observation/geometry/sampling, 20 capture adapter
and ownership, and 8 hotkey/emergency-stop. The existing hidden UI diagnostic
also gains coordinate draft and failed-save recovery scenarios; these do not
inflate the MSTest case count. Tests check rejection before native reads,
failure without premature completion, retryable ownership, exact protobuf bytes
including unknown fields, undo counts, and resource release.

No native production changes were needed for the demonstrated bugs. The native
contracts were source-reviewed and the existing 103 native cases retained.

## Final verification and coverage

- Full solution builds: Debug and Release passed.
- Ordinary managed suites: **782 passed in each configuration**, zero failures
  or skips, including the expanded hidden UI diagnostic.
- Native suites: **103 passed in each configuration**.
- Comparable Debug core coverage run: **781 passed**; only the hidden child is
  excluded from this measurement, not from the ordinary suites.
- `scripts/Test-Publish.ps1`: passed in Debug and Release. Missing native source
  is rejected even with stale destination files; alternate native binaries,
  PRI, all eight XBF resources, and self-contained runtime metadata are checked.
- Full-range whitespace check passed. No production app was launched or shown.

| Source file | Baseline covered lines | Final covered lines |
| --- | ---: | ---: |
| NativeRecordingTransport | 0 / 37 (0%) | 82 / 87 (94.3%) |
| WindowPixelObserver, including WindowsWaitDesktop | 32 / 119 (26.9%) | 60 / 125 (48.0%) |
| GlobalHotkeys | 67 / 87 (77.0%) | 70 / 88 (79.5%) |
| RecordingCapture | 99 / 107 (92.5%) | 99 / 107 (92.5%) |
| DesktopThemeMonitor, unchanged | 21 / 28 (75.0%) | 21 / 28 (75.0%) |

Changed denominators reflect the fixes and small injectable API boundaries.
Within the mixed observer file, the observer's 25 lines and sampler's 27 lines
are all exercised; the unexecuted real Windows enumeration/API wrappers explain
the modest whole-file percentage. The adapter class itself exercises 82 of 83
lines; its four real native-forwarding lines are deliberately not invoked.
These are line measurements, not claims of exhaustive path/race coverage.
The unchanged RecordingCapture percentage also illustrates why behavioral
failure tests matter even where line coverage was already high.

Local results: `artifacts/robustness-final/managed-Debug.trx` and
`managed-Release.trx`; Cobertura under `artifacts/robustness-coverage/`.
The comparable baseline is under `artifacts/robustness-baseline-core/`.

## Deliberate limits and handoff

- No real desktop enumeration, pixel observation, native capture/injection,
  global hotkey registration, or production startup was exercised. Native ABI
  ownership is source-reviewed and tested through fakes, not induced OS faults.
- A non-null native pointer with an otherwise valid size must still designate
  readable memory; managed metadata checks cannot establish that property.
- Failed native join intentionally retains its managed callback root until a
  successful explicit retry; no finalizer attempts to join native threads.
- Coverage instrumentation still interferes with one compiled-template check
  in the hidden child. Ordinary uninstrumented checks pass in both configs.
- Existing MSB3851 managed/native Windows target-version warning remains;
  no framework, dependency, or platform-target changes were made.
- Worker/reviewer channels and the four temporary worktrees/branches were
  cleaned after review and integration. No GitHub writes or pushes were made.
  Generated verification packages are disposable/reproducible; source work is
  retained in the four integrated commits listed above.
