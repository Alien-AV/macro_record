# Maintenance pass — 2026-09-21

Base: 4a69475. Status: complete, independently reviewed and integrated locally.

This is a bounded cleanup/technical-debt/bug-fix pass after the approved UX rework,
not a redesign or a new feature batch. Prefer reproduced correctness problems and
provable build/maintenance debt over speculative abstractions or dependency churn.

## Parallel scopes

- Shell and persistence: cancellation, shutdown, run leases, library saves,
  trash/recovery and committed preference readouts.
- Editor: selection/draft ownership, authoring, undo, preview, geometry/timing,
  resource lifetime and justified local simplification.
- Native execution: capture/playback semantics, failure handling and ownership;
  the managed playback boundary is included where needed to preserve native-stop
  guarantees across interop errors.
- Build/packaging: SDK-reference warning, reliable native/runtime resources in
  builds and publish output, and stale build/package paths. Preserve supported
  Windows versions and established framework/dependency choices.

## Evidence and delivery gate

Fresh-context workers use isolated repo-local worktrees; independent reviewers use
no worktrees. Review complete committed ranges, fix actionable findings and integrate
linearly. Run combined Debug/Release managed/native suites and verify publish
resources before delivery. The baseline is 679 managed and 102 native tests per
configuration. Keep findings and any deliberately deferred work in this document.

No GitHub writes or push, visible application launch, real input capture/injection,
live condition observations, or changes to real recordings/preferences. Automated
checks use fakes, temporary stores and unactivated controls. Native WinUI has no
web preview or dashboard registration; no preview servers are started.

## Reproduced fixes

- Recording stop: a rejected stop left a pending delay adjustment behind, causing
  a duplicate-key failure on retry or an unintended change to queued input.
  Rejected attempts now discard provisional state; already-completed native
  sessions still await their queued UI drain and apply an accepted delay once.
  Three regressions preserve unknown protobuf fields as well as timing.
  Candidate: c66f5ef; reviewed clean and integrated as 8220ecf.
- Editor: legacy X-button payload zero means X1 in playback, but grouping and
  visual preview disagreed. One editor helper now supplies that interpretation
  to grouping, preview and authoring. Five regression cases compare balanced and
  partly held buttons with unchanged wait validation and exact stored bytes.
  Candidate: 6204136; reviewed clean and integrated as 95c2135.
- Playback ownership: polling followed by a failed abort previously faulted the
  completion task before native termination was known. Engine, workflow and UI
  ownership now remain until a terminal poll or successful stop retry. Failure
  details survive, in-flight wait observation is cancelled, and dispose can
  retry a failed join. Seven managed regressions and one fake-sink native test
  cover the cross-layer lifetime and cleanup-before-completion contract.
  Candidate: afcb921; reviewed clean and integrated as 353483f.
- Packaging: publishing previously succeeded without the native runtime. Publish
  now requires it, adds it through the SDK publish pipeline, and replaces a
  differing DLL even if the destination is newer. `scripts/Test-Publish.ps1`
  exercises missing-source rejection, reused destinations, native/PRI/XBF hashes
  and self-contained runtime configuration and files without launching the app.
  Candidate: 77ade98; reviewed clean and integrated as e0bf60d.

## Cleanup and deliberately unchanged behavior

- Removed the nonfunctional CMake project with machine-specific vcpkg paths,
  C++11 settings and nonexistent source paths, its associated IDE metadata, and
  the orphan native filters file. Supported builds remain Visual Studio MSBuild;
  removed files remain recoverable from Git.
- Corrected coordinate documentation: conversion rejects overflow atomically;
  it does not saturate. Capture-origin metadata is not a pixel/count scale.
- A proposed primary-to-virtual conversion change was discarded after checking
  established tests: virtual mapping deliberately lets estimated paths cross
  monitor boundaries. No coordinate behavior was changed.
- MSB3851 remains visible: native SDK 26100 versus managed target 19041. A
  non-assembly project-reference probe still triggered platform validation.
  No warning suppression, Windows-minimum change, or dependency upgrade was made.
- Native `InternalError` can describe either a worker failure or an exception in
  the ABI wrapper. No additional native production defect was reproduced; that
  existing ambiguity was not expanded into an ABI change in this pass.
- Inno Setup is not installed locally. Installer generation and hosted CI were
  not verified; local solution builds, tests and directory publishing are the
  delivery checks.

## Combined verification

- Independent reviewers found no actionable regressions in all four full worker
  ranges and the publish-check follow-up. Reviewers reran focused fake-engine/native tests and packaging checks;
  no review worktrees were created.
- Coordinator checked that integrated implementation files exactly match the
  reviewed candidates, with no semantic merge drift.
- Combined VS18 solution builds passed in Debug and Release. Each configuration
  passed **694 managed and 103 native tests**, with no skips. This adds 15 managed
  cases and one native test to the fresh 679/102 baseline.
- Ordinary self-contained Release publish passed at `x64/Release/publish`.
  Eight key binaries/resources/icon and all eight XBF files matched their source
  hashes. Native DLL and icon also matched their originating project outputs.
  Published runtime configuration includes Microsoft.NETCore.App rather than
  requiring an external framework.
- Final checks exposed a false-positive in the new publish regression itself:
  `--no-build` after an ordinary solution build could copy a framework-dependent
  runtime configuration despite bundling runtime DLLs. The focused follow-up
  builds and validates a genuinely self-contained package, including matching
  runtimeconfig/deps runtime-pack assets. Candidate 5a147ba was reviewed clean
  and integrated as 5f76de3. Debug and Release publish checks passed again in the
  parent checkout; malformed/stale metadata was independently rejected in review.
- Worker branches/worktrees and generated verification packages were removed;
  worker and reviewer agents were thanked and closed. No previews/dashboard rows
  were created. Removed build metadata remains in Git; temporary test packages
  can be regenerated. The verified Release publish directory is retained for
  manual testing. No GitHub writes, push, or visible application launch occurred.
