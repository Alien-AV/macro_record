# App-local portable OCR

The Windows x64 distribution includes `ocr/tesseract.exe` and
`ocr/tessdata/eng.traineddata`, alongside notices, source/build provenance and a
SHA-256 inventory. The waiting library defaults to these app-base paths. The
bundle requires no global Tesseract installation, PATH modification, cloud OCR
or runtime model download. This packaging change does not alter wait execution
or local-settings policy.

## Explicit preparation

From the repository root, with the existing Visual Studio C++ tools installed:

```powershell
powershell -NoProfile -File scripts/Prepare-Ocr.ps1
```

This is the only preparation step allowed to download OCR assets. It builds the
official pinned vcpkg port with the `portable-ocr` manifest feature and
`x64-windows-static`, using at most four compiler jobs. It uses a separate
`artifacts/ocr-vcpkg` install root, leaving the normal native dependency set and
protobuf override unchanged. All generated binaries, models and test fixtures
stay under ignored `artifacts`; none are checked into Git. vcpkg may obtain its
own build tools as part of this explicit development step.

The script discovers Visual Studio using `vswhere`; `-VcpkgExecutable` and
`-Dumpbin` can identify another installed toolchain. Every preparation reconciles
the installed packages through vcpkg; there is no skip-build shortcut. The CLI's
installed SPDX provenance must match the exact official port tree and upstream
archive hash, and identify the actual executable's SHA-256. Model hashes, import
auditing, version checks and notices are also mandatory. Existing prepared bundles are moved to
`artifacts/ocr-previous-<id>` before replacement, not deleted.

Run preparation before the solution build in CI as well. Normal builds and
publishes only validate/copy files; they never call the preparation script.
The GUI project imports `packaging/Ocr.targets`. Both build and publish fail
clearly if the source bundle is absent, modified, incomplete or incompatible
with the checked-in lock/manifest. `-p:OcrBundleDirectory=<directory>` selects
another **verified** prepared bundle; it is not a bypass. A stale destination
cannot conceal a missing source. Differing destination assets are replaced even
when their timestamps are newer. Unexpected files left in an output `ocr`
directory fail validation rather than silently joining the distribution.

## Pins and portability audit

`packaging/ocr.lock.json` records:

- vcpkg baseline `f0c8848cde589649bca3ab3649668540403786ce`;
- Tesseract 5.4.1, baseline-selected port tree
  `72c4092a52446487c69f9927c5b22a8fddfb638c`, and the port's upstream SHA-512;
- English `tessdata_fast` commit
  `87416418657359cb625c412a48b6e1d6d41c29bd`, model SHA-256
  `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`,
  and the separately verified model license hash.

The official [pinned port](https://github.com/microsoft/vcpkg/blob/f0c8848cde589649bca3ab3649668540403786ce/ports/tesseract/portfile.cmake)
verifies the source archive and installs the CLI with `vcpkg_copy_tools`.
The [pinned model](https://github.com/tesseract-ocr/tessdata_fast/tree/87416418657359cb625c412a48b6e1d6d41c29bd)
uses the LSTM engine (`--oem 1`). English is the only bundled language.

CRT and dependency libraries are static. The pinned Tesseract source defaults
`OPENMP_BUILD` to off; preparation independently audits normal and delay-loaded
PE imports using `dumpbin /DEPENDENTS`. Only a bounded Windows inbox DLL list
and Windows API-set imports are accepted. `vcomp`, `libomp`, dynamic VC runtimes
and other non-inbox dependencies fail preparation. No DLL is silently borrowed
from the developer's PATH. Fixture execution sets PATH to Windows System32 and
removes ambient `TESSDATA_PREFIX`.

Explicit minimal features for curl/libarchive/libwebp/tiff suppress unnecessary
transitive defaults (including libiconv). All installed target-package notices
are copied into `ocr/licenses`, with vcpkg SPDX/ABI metadata and package status
under `ocr/provenance`; Tesseract and model Apache-2.0 notices are mandatory.
Keep these directories with redistributions. Source versions/hashes are pinned,
but binaries need not be byte-identical across Visual Studio/vcpkg tool versions;
the receipt records the actual prepared bytes and build provenance.

## Noninteractive verification

```powershell
powershell -NoProfile -File scripts/Test-OcrPackaging.ps1
powershell -NoProfile -File scripts/Test-Publish.ps1 -Configuration Debug
```

The first script checks missing executables/models/notices, corruption, stale
receipts, unexpected files and non-inbox DLL imports. It renders its own text
image in memory and invokes only the bundled CLI on PNG and top-down BGRA BMP
fixtures. A managed regression test runs the same checks and additionally uses
the backend's binary BMP-on-stdin protocol under .NET 10. The publish check also validates source
failures against an existing destination, replacement of newer corrupted output
assets, all published bundle hashes and actual OCR from the published directory.
No app window is shown; no desktop capture, global registration, input injection,
saved user settings or recordings are involved.
