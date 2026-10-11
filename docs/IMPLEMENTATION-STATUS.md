# Implementation status - 2026-10-11

**Preparing public [v0.2.0-preview.2 — check available assets](https://github.com/Zanark/phantom-fudge/releases/tag/v0.2.0-preview.2):**
unsigned Windows11 x64 prerelease, with previous-answer context included. Exact package/publication evidence is pending in
[workstream62](../TODO/62_PUBLIC_PREVIEW_RELEASE.todo). The [website is live](WEBSITE.md); neither fact establishes capture/device/clean-VM qualification.

**Historical Preview 1, October10 01:52 IST (then private):** Setup, Portable ZIP, both PDFs and SHA256SUMS were published
after downloading all five assets and matching their exact bytes. The immutable tag points to
`e25c8d5624e5afd2f50cda80f964e2e32737fce1`; installer metadata records that commit and
`sourceDirty=false`. [The release record](release-0.2.0-preview.1.json) contains exact hashes and sizes.

The final isolated installer lifecycle passed 11 checks across 3,684 inventoried files; all 3,685
Portable entries, including the inventory, matched their expected hashes. The exact launcher passed
10 self-checks, and the exact desktop passed its UI groups and 38 keyboard checks in a serial run.
An earlier concurrent UI attempt returned `InvalidOperationException`; its cause is not established.
That failed attempt is retained rather than being erased by the later unchanged-artifact pass.

The runtime passed 85 focused tests, declared dependency/actual CRT-path gates, real generated-PCM
Vosk/VAD/Whisper execution and natural exit, including Unicode paths. Native helper tests passed 55.
No real audio capture or external model request was made. Current-host, simulated older-version,
shortcut, installed-service and clean-VM limits remain explicit in the release record.
The released book has 371 pages; the source atlas has 645 pages and covers 203 first-party files.
Repository editions may add this publication follow-up; release attachments remain immutable.

## Current source summary

**October10 source evidence, now included in the Preview 2 candidate (not a new test run).** The default-off toggle
immediately left of Ask adds one displayed completed answer as background; clarification retains
the same snapshot through corrections, confirmation and explicit first-interpretation retry.
Keyboard `context on/off` shares the guarded handler. The focused Core suite passed396cases;
the final synthetic UI fixture passed21context checks,38keyboard checks and existing UI groups.
All24WPF images and11README derivatives were refreshed; parent reviewed normal context, all24contact-sheet entries and full minimum question/clarification images.
The first new fixture exposed a compact snapshot-height regression; shorter secondary captions
and a narrower context toggle corrected it before the final pass. No model or audio input was used.
[Workstream59](../TODO/59_PREVIOUS_ANSWER_CONTEXT.todo) records source/edition evidence;
**v0.2.0-preview.1 does not contain this addition**. Its release assets/tag remain unchanged.

Start with the [illustrated current workflow](CURRENT-WORKFLOW.md). It includes the maintained
images and the exact distinction between selecting, recording, sending, covering and stopping.
The dated sections below preserve earlier evidence rather than pretending old packages contain
newer features.

| Current feature | Source behavior and boundary | Guide |
| --- | --- | --- |
| Installer / native launcher | Preview 2 candidate; exact package results pending. Guarded install, repair, update and removal unchanged | [Installation](INSTALLATION.md) |
| Audio device cards | No list scrollbars; explicit overflow pages retain selected endpoint identity | [Setup](CURRENT-WORKFLOW.md#1-choose-audio-device-cards) |
| Red recording dot | Follows active transcription, not startup or input-mode selection; no audio-file save implied | [Capture state](CURRENT-WORKFLOW.md#2-start-transcription-and-watch-the-red-dot) |
| Multi-line question selection | Whole Mic/Computer segments, raw text in display order, no automatic Ask | [Local questions](LOCAL-QUESTIONS.md) |
| Clarify first | Off by default; separate original/reply, explicit correction or Yes, bounded history | [Clarification](QUESTION-CLARIFICATION.md) |
| Previous answer | Included in Preview 2; explicit single completed-answer snapshot, session-only/default-off, no accumulated history | [Follow-up context](LOCAL-QUESTIONS.md#follow-up-with-the-previous-answer) |
| Ask and Clear | Large accent Ask, red-bordered Clear immediately after it; existing guards retained | [Current controls](CURRENT-WORKFLOW.md#components-know-the-controls-before-sending) |
| Reading sizes | Final transcript15; partial13.5/10.5; answer10/9 until a session choice8–24; question18, in WPF units | [Typography](CURRENT-WORKFLOW.md#current-sizes-themes-and-visible-status) |
| Keyboard controls / answer Size | Included in Preview 2, with context commands; explicit local modes and session size8-24; external focus/comfort qualification remains | [Canonical keyboard guide](KEYBOARD-CONTROLS.md) |
| Cover / Uncover | NOTEPAD IS READY; already-started transcription/answers continue on the same HWND | [Cover/privacy](COVER-AND-PRIVACY.md) |
| Privacy requests | Existing capture affinity/tool-window plus accepted Peek request; cached peer still reads retained text | [Feasibility matrix](COVER-AND-PRIVACY.md#feasible-measures-in-practical-order) |
| Diagnostics | Metadata by default; conversation bodies only through explicit Text opt-in | [Local diagnostics](DIAGNOSTICS.md) |

**Check Preview 2 assets before downloading Setup or the complete Portable ZIP**, or run source through
`scripts\Run-PhantomFudge.ps1`. Save/copy required text and close an older instance with X before
updating. The immutable045022ZIP predates the later selection/clarification/action/font/card/cover
changes. Preview 1 is also historical and lacks Previous answer; workstream58 remains complete.
Workstream62 requires fresh exact-package evidence; October10 test counts above are not inherited results for Preview 2.

**Earlier evidence, not a keyboard-increment pass:** the140313synthetic UI fixture covers the then-current combined interface,
including15cover assertions, source selection, cards, font/layout/action, clarification and alpha
guards. It uses fake provider output and synthetic transcript/device data, not real recording.
Peek setter acceptance and affinity readback17 are local API observations. External UIA clients,
recipient captures, actual microphone quality and broad latency distributions remain unqualified.
Earlier provider/speech benchmarks retain their original scope and failures; they were not rerun
for documentation.

The [WPF image manifest](handbook/assets/screenshots/manifest.json),
[README derivative manifest](assets/readme/manifest.json), [source-atlas ledger](source-atlas/coverage.json)
and [handbook manifest](handbook/manifest.json) carry their recorded provenance. The keyboard increment
includes23WPF images and10README derivatives plus refreshed atlas/book editions. Images are separate
synthetic examples, not a continuous meeting or capture-exclusion proof.

## Historical checkpoint - October10 01:11: runtime gates passed before release assembly

Documentation read the frozen component handoffs, lifecycle3 result and implementation paths below.
The latest combined test, candidate2 and dependency-audit results were supplied by the parent;
the final runtime rebuild/identity was supplied by its owner. Documentation did not rerun builds,
tests or binary/hash verification. These are distinct evidence scopes:

| Scope | Latest reported result | What it does not prove |
| --- | --- | --- |
| Native launcher | Parent rerun confirms55 targeted helper tests; self-contained publish and10 exact-EXE self-checks previously passed; final handoff records14files/1,955lines/64annotation blocks | No interactive WPF Close, real installed app/service or capture test |
| Speech runtime | Parent's combined five-module run passes85 focused tests, superseding78; final offline rebuild passes dependency/import/model/inventory checks, real generated-PCM hybrid/VAD/forced Whisper execution and natural exit; Unicode relocation also passes | No audio-device capture, actual service, external provider request, clean VM or recognition-quality/performance qualification |
| Packaged desktop | Earlier UI-smoke receipt passes seven groups and38keyboard checks, with no recording/audio/model requests | Not installed speech or external hotkey/recorder qualification |
| Candidate2 build | Parent-executed full `Build-Installer` with current runtime/native launcher passed, including mandatory real synthetic PCM and clean process exit | Not a clean committed final release build or captured-audio test |
| Inno lifecycle3 | Inspected receipt: all 11 checks pass against 3,682 inventoried files from candidate2 | Current host, random AppId/path, no shortcuts, simulated version record; not genuine older-binary upgrade or clean VM |
| Scoped runtime review | No significant issues reported in the new Unicode/startup/backend scope | Code review, not an independent PCM run or general qualification |
| Speech native dependencies | Parent confirms92 PE dependency records match payload hashes and five actual CRT/OpenMP paths are bundled; owner also reports independent dumpbin comparison and rejection of a global-VC negative child | Current-host declared dependency closure plus observed paths, not a clean VM |
| App/launcher dependencies | Parent reports a separate static closure PASS across 861 PE files | Not an app/launcher loaded-module-path or clean-VM test |
| Final release | Clean committed release build, final inventories/checksums/provenance, generated editions and publication pending | No downloadable final installer yet |

### Runtime replacement and its provenance

The final DLL-qualified component is `.local\installer-runtime-dll-qualified`:
**2,810files / 765,576,675bytes**, including its manifest. Owner-reported
`speech-runtime-manifest.json` SHA-256:
`c62093e8f8375471aacddff7656bfb69936aa8b6fc258865c92f5dd593355e16`.
It supersedes `.local\installer-runtime-delivery` for release evidence. The owner reports unchanged
package/native/model/service bytes, with changes confined to notices, the copied lock and manifest.
This is the qualified runtime component identity, **not candidate2's full payload or a final Setup hash**.
The clean committed release build still determines the final installer inventories and checksums.

The private CPU package is `CTranslate2 4.6.0+phantomfudge2`, built with oneDNN3.1.1 and
Microsoft compiler OpenMP (`vcomp140.dll`), without CUDA/cuDNN/proprietary Intel OpenMP.
`faster-whisper 1.2.1+phantomfudge1` remains the disclosed PCM-only variant. Its upstream wheel,
`audio.py`, unchanged transcription/features/VAD sources and padding helper are hash-bound.
Installed metadata closure explicitly describes the modified graph, not unchanged upstream
dependencies or execution proof. Notices preserve the PyAV codec-review rationale without
claiming that PyAV is inherently nonredistributable.
(tools/Installer/bundle_runtime.py:42-43,246-282,332-376,465-535,581-586)
(tools/Installer/runtime-lock.json:31-51)

Vosk's native package is unmodified. `VoskFactory` verifies its model, then handles a Windows
non-ASCII path with a serialized temporary working-directory change and restores it in `finally`.
The synthetic test creates the actual `HybridFactory`, processes generated numerical PCM through
Vosk and the production finalizer/VAD, and forces a separate Whisper encoder/decoder pass so VAD
cannot skip native computation. A success JSON line is insufficient: the child must exit0 before
the deadline. `Build-Installer.ps1` requires `--verify-synthetic-pcm`.
(services/transcription/recognizer.py:25-63)
(services/transcription/tests/test_packaged_pcm_runtime.py:35-97,126-138)
(tools/Installer/bundle_runtime.py:597-609,682-694)
(scripts/Build-Installer.ps1:30-34)

The rejected Ruy stages and their120/240-second exit hangs remain in the history below. The new
pass does not inherit historical development-build latency/accuracy results, prove meaningful
recognition of generated tones, or complete captured-speech/clean-machine qualification.

### Native dependency gate, not a clean-machine certificate

Restricting `PATH` alone cannot prove that a developer machine's globally installed VC runtime
is unnecessary. `bundle` now always calls `audit_pe_closure` before the import probe. The auditor
reads ordinary imports, delay imports and export-forwarder DLL names, records each PE hash and
requires each non-inbox dependency in the payload's supported search locations. Only an explicit
reviewed Windows11 inbox list is exempt; arbitrary files present in the host's System32 do not
satisfy this static check.
(tools/Installer/bundle_runtime.py:677-694)
(tools/Installer/runtime_audit.py:9-29,32-166)

The shared `DLL_PROBE` selects `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` and enumerates loaded module
paths. It rejects VC runtime DLLs outside the payload and requires `msvcp140.dll`,
`msvcp140_1.dll`, `vcruntime140.dll`, `vcruntime140_1.dll` and `vcomp140.dll` beside bundled
`python.exe`. Both the import probe and generated-PCM test collect this evidence. Windows
Defender's recognized injection is reported separately, not redistributed as a dependency.
(tools/Installer/runtime_audit.py:169-234)
(tools/Installer/bundle_runtime.py:538-594)
(services/transcription/tests/test_packaged_pcm_runtime.py:14,88-97)

The parent confirms the 92-file speech audit matches payload hashes, all five observed CRT/OpenMP
paths are bundled and the separate 861-file app/launcher static audit passes. The final offline
runtime rebuild has now executed the stronger gate successfully, including real PCM and normal
exit after Unicode relocation. Its manifest records `validation.nativeDependencyClosure`,
`validation.dllLoadProbe` and `validation.syntheticPcm.dllLoadProbe`.
(tools/Installer/bundle_runtime.py:677-694)

Functional DLL bytes are reported unchanged from candidate2. These results do not retroactively
change candidate2's build receipt, exhaust every dynamic loading path, certify licensing entitlement
or establish clean-VM qualification. Runtime gate execution is no longer outstanding; the clean
committed final release asset build remains separate and pending.

### Startup and installer integration

The final native handoff supersedes the51-test/nine-self-check checkpoint and the subsequent
four-file annotation-drift report. It reports current source/publish/annotation evidence and10
exact-EXE checks at `2026-10-09T18:44:44.9210386Z`. No additional receipt recheck was performed
for this prose integration.

Startup retains the launcher mutex until the app's main-instance mutex appears or the exact
child is proven exited. After30seconds, **AppStartPending76 is not Success or an automatic exit**.
Pending Close hides progress without releasing the guard; a synchronous watcher preserves it
through early dispatcher shutdown. Confirmed child exit before readiness is AppStartFailed67,
even for child exit0. The new tests use random mutexes and a delayed synthetic child; interactive
WPF Close remains untested.
(src/PhantomFudge.Launcher/ChildProcesses.cs:12-42)
(src/PhantomFudge.Launcher/Program.cs:108-118,186-227,321-354)
(src/PhantomFudge.Launcher/LauncherStatus.cs:20,31,39)

Setup has **no launch action**: finish and close it, then open the app from Start. Setup and
launcher status/error windows are ordinary visible UI, not capture-excluded; install and launch
before sharing. Setup/Uninstall creates the maintenance gate, and normal/notes-only launch checks
it before reveal and again after acquiring the startup mutex.
(tools/Installer/PhantomFudge.iss:14-76,150-161)
(src/PhantomFudge.Launcher/Program.cs:47-78)
(src/PhantomFudge.Launcher/LauncherWindow.cs:18-57)

The current lifecycle3 pass covers the fixture's install/hash/layout/maintenance, same-version
and damaged-launcher/version repair, simulated older-version record, downgrade refusal,
uninstall, retained external sentinel and foreign-directory refusal against candidate2. The fixture
does not start the app/speech service or exercise genuine old binaries; its receipt records no
captured audio and zero model requests. Candidate2's real generated-PCM inference belongs to the
separate build gate, not this fixture. The current version fallback is `WindowsVersion`
under `HKCU\Software\PhantomFudge\Installer\<AppId>`, **not the Windows uninstall key**.
(scripts/Test-Installer.ps1:15-18,38-84,96-104)
(tools/Installer/PhantomFudge.iss:51-52,123-147)

A clean committed release build with final provenance/checksums, the reserved baseline citation
migration, rebuilt HTML/PDF editions and authorized private publication remain parent-owned.
The runtime-atlas owner handles the missing runtime annotations; the parent retains domain,
packaging and book/catalogue annotations. Lifecycle3 does not establish concurrent-launch,
Start-menu, actual speech-service or clean-machine qualification.
No signing identity or project license has been supplied. See the concise
[installation procedure](INSTALLATION.md) and [workstream58](../TODO/58_INSTALLER_RELEASE.todo).

## Installer development history — October9 21:52 to October10 00:09

**Historical checkpoints below retain the evidence and blockers known at each time.** They are
superseded by the final component handoffs above, not current holds on that replacement runtime.
Neither rejected Ruy stage becomes an approved input because the replacement passed.

The owner requested unattended installer/release and README/TODO/book work. The planned version is
[v0.2.0-preview.1](https://github.com/Zanark/phantom-fudge/releases/tag/v0.2.0-preview.1), Windows file
version0.2.0.1, unsigned and Windows11 x64 only. The repository is verified private; release access
inherits it and no visibility change is authorized. This is not a qualified release or a claim of
compatibility with Windows10, ARM, macOS, Linux or every x64 computer.
(tools/Installer/release.json:1-8)

The source recipe uses per-user Inno Setup6.7.3 with lowest privileges, a stable AppId, Start-menu
launch and guarded update/uninstall. It rejects downgrade/unrecognized nonempty destinations and
does not force-close the app. The frozen native maintenance path refuses an open app and implements
authenticated owned idle-runtime shutdown; full installer integration is still pending.
External settings/diagnostics, installed runtime
data and saved notes/exports are retained. Program directories are not personal-content storage.
(tools/Installer/PhantomFudge.iss:10-71,106-159)
(src/PhantomFudge.Launcher/Program.cs:47-100)

**22:30 packaging-review refinements:** compact `install-manifest.json` is separated from
`payload-files.json`, the full path/size/SHA-256 inventory excluding its own self-hash. Hidden
entries and reparse points are inspected; the fixture compares payload filenames exactly and
checks installed bytes. Repair/update is being integrated with a fresh Setup-extracted maintenance
helper rather than a damaged installed launcher, with numeric Windows-version fallback in the
uninstall registry. Native integration and successful repair evidence remain pending.
(scripts/Build-Installer.ps1:48-90)
(tools/Installer/PhantomFudge.iss:51-52,106-146)
(scripts/Test-Installer.ps1:40-66)

The test script uses a random isolated AppId and destination, omits shortcuts/automatic app launch
and simulates an older numeric version record in the current fixture. It is not a genuine
old-binary upgrade, clean-VM installation or hardware qualification. No passing fixture result
is claimed from reading the script.
(scripts/Test-Installer.ps1:15-18,31-38,67-69,89-98)

The intended payload includes self-contained app/launcher, private Python, speech code and verified
Vosk/Whisper small.en assets; end users need no PowerShell7, system Python or .NET SDK. Normal launch
prepares an idle local service, not audio capture; `--notes-only` bypasses speech preparation.
No provider CLI/model or login is bundled. New profiles remain SnapshotOnly and existing root
answer/keyboard settings remain in use. Docker is still separate and last.
(scripts/Build-Installer.ps1:25-44)
(src/PhantomFudge.Launcher/Program.cs:117-196)
(src/PhantomFudge.Launcher/ChildProcesses.cs:48-72)

**22:35–22:36 bounded component evidence, reported by the implementation owners:**23launcher tests
and all nine published-launcher self-test booleans pass; the self-test receipt is
`.local\installer-release\launcher-build\self-test.json`. The runtime owner reports57targeted
manager/path/hybrid mock tests passing. These are component/mock results, not installed-copy
startup, fresh-helper repair, speech inference or clean-VM qualification.

The in-progress runtime recipe selects pinned embedded Python3.13.14 and proposes patched
`faster-whisper1.2.1+phantomfudge1` without encoded-file decoding/PyAV, and
`CTranslate2 4.6.0+phantomfudge1` rebuilt CPU-only with Ruy. CUDA/cuDNN/Intel runtime binaries and
PyAV/FFmpeg are excluded by the recipe; package modifications and notices are intended to be
recorded in the build manifest. The service consumes in-memory PCM arrays in source, but this is
not an unchanged full stack: older developer-build recognition timings and accuracy results do
not qualify the proposed binaries. Final output, native ABI/import and licensing checks remain pending.
(tools/Installer/runtime-lock.json:1-11)
The Ruy recipe described here is historical; current source implements its oneDNN replacement.
(tools/Installer/bundle_runtime.py:246-282,332-376,465-535,687-694)

Runtime paths now separate installed weights from the mutable user workspace. Process-identity
checks permit removal of confirmed-dead/recycled receipts while live/unknown ownership refuses.
Native launcher maintenance uses authenticated loopback HTTP, not a new Python maintenance verb.
User configuration/token/model data are not silently reset; integrated contract validation and
fresh-helper repair remain pending.
(tools/Installer/runtime-contract.json:4-24,33-45)
(src/PhantomFudge.Launcher/MaintenanceOperation.cs:48-118)

**22:43 compiler provenance, reported by the packager:** the official Inno Setup6.7.3 download
has a valid Authenticode signature for **Pyrsys B.V.** and matches the pinned SHA-256
`9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`.
This authenticates that downloaded build-tool artifact, **not Phantom Fudge Setup**. No signing
identity has been supplied for this application's installer; it remains an unsigned preview.
Private repository/tag/release access is unchanged.
(tools/Installer/release.json:6-7)

Fresh Setup-extracted repair with registered numeric-version fallback is still untested.
The fixture's `/DInstallerTest=1` omits normal shortcuts/post-install launch; its older-version
record is simulated, not backed by a built older binary. Neither compiler provenance nor
component tests closes those installer qualification gaps.
(scripts/Test-Installer.ps1:31-38,62-69,96-98)

**22:45 launcher freeze, reported final evidence:**46targeted helper/transport tests, self-contained
publish and the exact published EXE's self-test pass. Headless `--validate-install` returned0for
a synthetic valid layout,65for a manifest larger than16KiB and65for corrupted metadata.
Receipts are `.local\installer-release\launcher-build\self-test.json` and
`.local\installer-release\launcher-build\headless-validation.json`. No app, service, audio or
model startup was used for those checks.

`--validate-install installation-root` validates the ownership marker, compact metadata and
app/Python/manager/model-directory layout. It is not a full file-hash, ABI or model-inference test.
`--maintenance [installation-root]` uses native authenticated HTTP, shared byte locking and
instance/PID/idle/creation-FILETIME checks; it can operate without healthy old Python binaries
or compact metadata, but still requires the marker. The fresh bootstrap is the complete
self-contained launcher directory extracted by Setup, not a lone executable.
(src/PhantomFudge.Launcher/Program.cs:25-58,94-100)
(src/PhantomFudge.Launcher/LauncherContract.cs:14-23,79-105)
(src/PhantomFudge.Launcher/MaintenanceOperation.cs:48-118)
(src/PhantomFudge.Launcher/OwnedServiceClient.cs:20-84)
(tests/PhantomFudge.Core.Tests/InstalledMaintenanceTests.cs:76-108,210-239)

Launcher citations were refreshed against that source freeze. Full same-version Setup repair,
installed runtime/ABI, exact payload/licensing and isolated installer lifecycle remain pending;
the46helper tests do not close them. Older preexisting-source citation values remain unchanged
until the planned one-shot migration.

**22:52 final contract-aligned revalidation:** the launcher owner again reports 46 tests,
self-contained publish and exact published EXE self-test exit 0/all nine booleans passing.
The source now matches `runtime-contract.json`: manager prefix `-I -B -X utf8`, 32-random-byte
lowercase-hex token generation and a 180-second launcher deadline. Existing valid Base64URL tokens
are explicitly tested as preserved. Receipts in the same launcher-build directory were refreshed;
native maintenance/headless-validation contracts and the untested full-Setup boundaries are unchanged.
(src/PhantomFudge.Launcher/ChildProcesses.cs:15-28,48-71)
(src/PhantomFudge.Launcher/LauncherContract.cs:31-36)
(src/PhantomFudge.Launcher/Program.cs:13,228-274)
(tests/PhantomFudge.Core.Tests/InstalledLauncherTests.cs:135-155)

**22:54 intermediate runtime result, reported by the runtime owner:** payload v3 contains 2,799 files /
749,352,236 bytes. Embedded Python 3.13.14 imports, dependency closure, CPU-int8 support and every
pinned model hash pass. No service or inference was started. **Do not package v3:** the final
user-model path amendment and a new offline rebuild are still being verified.

The amended contract gives an existing per-user `.local\models\<pinned-name>` priority over
bundled weights, with the same pinned verification; explicit provisioning writes only to
that preserved user directory. Launcher argv/environment remain unchanged.
(tools/Installer/runtime-contract.json:7-14,20-22)

The official PyPI file CDN failed TLS; the runtime owner reports that the public Tsinghua mirror
provided exactly hash-locked wheels. This provenance is disclosed in the runtime manifest.
GPU/Intel/PyAV binaries are excluded and supplemental Vosk native-dependency notices were added.
The intermediate import/hash pass is not final redistribution/license approval, a service/inference
test, installer acceptance or a new performance result.
(tools/Installer/bundle_runtime.py:108-126,505-534,674-676)

**23:00 final runtime handoff, ready for installer assembly:** the runtime owner delivered
`.local\installer-runtime-stage` with **2,801 files / 749,919,714 bytes**, including
`speech-runtime-manifest.json`. Its SHA-256 is
`1f8d8396f4b2538138632f1b72251bda0ec46073a937a9d0ff99f4a60c8012f7`.
The final source includes the preserved-user-model amendment; earlier v1/v2/v3 scratch payloads
were deleted and are not packaging inputs.

This stage was subsequently superseded by the 23:15 recovery handoff below.

| Evidence | Result and limit |
| --- | --- |
| Final fully offline rebuild | Owner reports embedded Python 3.13.14 imports, dependency closure, CPU-int8 support and every pinned model hash passing |
| Independent runtime inventory verification | Owner reports every inventory entry matched its file length/SHA-256 |
| Relocation | Owner reports the complete import/dependency/hash probe passing after moving the payload to a path containing spaces and U+00E9 |
| Regression suite | Owner reports 70 combined bundler, installed-path, management and accurate-engine tests passing |
| Documentation cross-check | Independently read the final manifest and confirmed its SHA-256, 2,800 inventoried files / 749,363,978 bytes plus the 555,736-byte manifest, package variant versions and recorded probe results; did not rerun the build or tests |

The probe imports native bindings and checks available compute types and pinned bytes; it does
**not** start a real service, open an audio endpoint, load a speech model or perform inference.
No .NET build, install/uninstall, clean Windows VM or new recognition-performance result is implied.
The manifest identifies runtime bytes, not the as-yet-unbuilt Setup artifact.
Current counterparts of the import-only probe and manifest writer:
(tools/Installer/bundle_runtime.py:538-594,687-694)
(services/transcription/tests/test_installed_runtime.py:47-91)
(services/transcription/tests/test_runtime_bundle.py:79-161)

The exact staged runtime retains third-party notices, patch disclosures and regenerated package
records. Mirror provenance and upstream Vosk reproducibility limits remain explicit in
[third-party notices](THIRD-PARTY-NOTICES.md). These are engineering provenance, not legal
certification or a selected Phantom Fudge license.
(tools/Installer/bundle_runtime.py:316-390,465-535,687-694)

**23:15 recovery hardening superseded the earlier stage; now held by the23:40 blocker:** the then-current runtime input was
`.local\installer-runtime-stage-recovery`, **2,801 files / 749,919,744 bytes**, with manifest SHA-256
`da6ea2fb1d16eac97bddc1b4e9bba4c16841a3560bebc4162c7ee47896e70d3d`.
The owner reports **72 tests**, a rebuilt offline payload, every file/model hash and the
spaced/Unicode relocation probe passing. Documentation independently confirmed this manifest
hash, recorded probe results and totals; it did not repeat the build/tests.

The manager now persists the child PID before querying creation time. If inspection fails, that
PID remains available for later cleanup only after proving the process absent. Confirmed-dead or
recorded-creation-time-proven PID reuse advances normal startup automatically; live or ambiguous
initializers remain protected. The added tests exercise the real `start` function with mocked
process/port observations, **not an actual reboot, service or inference**. Package versions and
the CPU/PCM variant boundaries are unchanged. Do not package the superseded `installer-runtime-stage`.
(services/transcription/tests/test_installed_runtime.py:98-198)

**23:14–23:15 launcher hold, subsequently resolved:** partial token/receipt handling and PID-only
recovery required another verification pass. The earlier 46-test checkpoint remains historical;
native explicit-root authenticated-HTTP maintenance was already implemented and remains
independent of old Python/app binaries. No Python maintenance verb was introduced.

**23:25 final native recovery refreeze:** the launcher owner reports **50 targeted tests**,
self-contained publish and the exact published EXE's self-test passing. Documentation independently
read the refreshed handoff and the self-test receipt: exit 0, all nine checks true, timestamp
`2026-10-09T17:54:20.8682469+00:00`. It did not rerun the test suite or executable.

Both token and receipt absent succeeds without a listener probe. Receipt-only refuses.
Token-only—normal after successful stop—validates the private token and uses a credential-free
one-second TCP probe; only connection refused proves the port unused. Occupied or uncertain
state refuses. PID-only stale cleanup requires proven process absence; live legacy state still
requires authenticated identity, and uninspectable state remains protected.
(src/PhantomFudge.Launcher/MaintenanceOperation.cs:52-118,121-134)
(tests/PhantomFudge.Core.Tests/InstalledMaintenanceTests.cs:31-108,149-172)

The refreshed launcher atlas reports 14 files, 1,728 physical lines and 54 contiguous authored
blocks; maximum block/focus sizes are 62/12. Documentation independently matched all 14 source
hashes and the line total to that annotation. This is **per-file validation**, not full
atlas/handbook compilation. Runtime/launcher citations now target the frozen source.
The earlier headless layout results retain their original bounded checkpoint; this new report
does not claim an additional headless run or full Setup acceptance. Use current source for the
parent's release-version rebuild, not an earlier copied launcher.

**23:39 launcher freeze held for a separate installer gate:** the launcher owner began adding a
check for `Local\PhantomFudge-Installer-Maintenance` before normal/notes-only reveal or startup;
maintenance and layout validation bypass that gate. Implementation, targeted verification and
republish were pending at that checkpoint, now superseded by the23:54 source freeze below.
No change to native HTTP maintenance or the runtime contract is claimed from this work.

**23:40 runtime publication blocker, reported by the runtime owner:** an authorized local
synthetic-PCM probe completed real Vosk recognition, the production Whisper final/VAD path and
forced CTranslate2 encoder/decoder computation. The Ruy-backed child then **hung during process
exit**, exceeding 120- and 240-second test deadlines after printing success. Earlier import-only
qualification did not expose this failure. No capture device, service or external audio/model API
was used. This is not a clean-exit pass, accuracy benchmark or installer qualification.

**Do not package or ship `.local\installer-runtime-stage` or
`.local\installer-runtime-stage-recovery`.** Their manifest identities and earlier results above
remain provenance, not current assembly approval. The runtime owner is investigating cleanup/backend
behavior; a corrected exact payload and repeatable inference-to-clean-exit evidence are required
before the parent resumes installer acceptance/publication. This documentation update records the
owner's report, not an independently rerun probe; detailed probe receipts and any replacement
manifest remain pending. Package-patch provenance/licensing amendments also await the new source freeze.

**23:54 launcher gate source freeze:** the launcher owner reports51targeted tests and a
self-contained publish passing. Source inspection confirms a gate check before coordination/reveal
and a second check after acquiring the startup mutex. Detection returns70 with generic status and
no WPF window/child startup; maintenance and layout validation bypass it. The new test uses a
randomly named mutex and exercises presence, disposal and mode selection, not a real installer run.
(src/PhantomFudge.Launcher/Program.cs:25-78,209-212)
(src/PhantomFudge.Launcher/LauncherContract.cs:152-165)
(src/PhantomFudge.Launcher/LauncherStatus.cs:15,33)
(tests/PhantomFudge.Core.Tests/InstalledLauncherTests.cs:176-195)

Launcher behavior, diagram and new-file citations were refreshed against this production source.
The new exact-EXE self-test, annotations and handoff were pending at that checkpoint; their
verification follows. Runtime publication remains blocked independently.

**October10 00:05 launcher receipts verified:** the refreshed handoff records51targeted tests and
self-contained publish passing. The exact-EXE self-test receipt records exit0/all nine checks true
at `2026-10-09T18:07:25.8502546Z` (October9 23:37:25IST); this is the receipt's execution time,
not the later documentation-verification time. Documentation matched both published EXE/DLL
hashes to the handoff and all14source files to the launcher annotation. Contiguous coverage is
1,768physical lines/56blocks, maximum block/focus62/12, with no duplicate source ownership.
The receipts are `handoff.json`, `self-test.json` and `atlas-validation.json` under
`.local\installer-release\launcher-build`.

These are independently inspected receipts, hashes and bounds, not rerun tests or full
atlas/handbook compilation. Earlier headless-layout checks remain historical; they were not
rerun after the gate-only change. Full installer acceptance must still verify that Setup and
Uninstall hold the exact `Local\PhantomFudge-Installer-Maintenance` gate from before native
maintenance through replacement/deletion until installer exit. The random-mutex component test
does not establish that integration, and this launcher checkpoint does not release either held
runtime payload.
(src/PhantomFudge.Launcher/Program.cs:47-78)
(src/PhantomFudge.Launcher/LauncherContract.cs:152-165)
(tests/PhantomFudge.Core.Tests/InstalledLauncherTests.cs:176-195)

**October10 00:09 qualification update:** the parent confirms the recipe now uses
`CreateMutex('Local\PhantomFudge-Installer-Maintenance')` for the entire Setup/Uninstall lifetime.
Source inspection confirms creation in both initializers and the frozen native checks/bypass
behavior; the isolated full lifecycle run is underway, with no completed receipt yet. This is
implemented integration, not a passing lifecycle result.
(tools/Installer/PhantomFudge.iss:72-76,150-160)
(src/PhantomFudge.Launcher/Program.cs:25-78)

The parent's packaged self-contained desktop `--ui-smoke` run at
`.local\installer-release\packaged-ui-1` passed. Documentation inspected `result.json` and
`keyboard-controls.json`: overall/seven named regression groups pass, all38keyboard checks are
true, recording/audio capture are false, and model requests are zero. This confirms synthetic
packaged-desktop behavior, not installed speech, real shortcut comfort or capture exclusion.

Runtime publication remains **BLOCKED**. The parent independently reports a120-second timeout
after successful inference JSON at native process exit, and a second review identifies Vosk
Unicode model-path handling through narrow native path operations under ACP1252. Runtime repairs
are underway; no fix or replacement payload is qualified here. Acceptance now explicitly requires
the real `HybridFactory` to load both models from a Unicode path, process synthetic PCM and exit
naturally. Import-only relocation and printed success before a hang do not satisfy this gate.
The factory's actual composition is Vosk plus the Whisper finalizer.
(services/transcription/accurate.py:121-129)

**Final authored check found subsequent native source changes:** `ChildProcesses.cs`,
`LauncherStatus.cs`, `Program.cs` and `InstalledLauncherTests.cs` no longer match the inspected
launcher annotation hashes. The new application-start handoff source is therefore outside the
verified51-test/nine-self-check checkpoint. The two installer-presence checks remain at47–48 and
76–77, but new tests/publish/atlas validation and affected citation review must follow the next
owner freeze. The launcher owner has been notified; no earlier receipt is claimed for these new bytes.
(src/PhantomFudge.Launcher/ChildProcesses.cs:12-42)
(src/PhantomFudge.Launcher/Program.cs:179-228)

**Evidence pending at the00:09 checkpoint:** runtime exit/Unicode-path remediation and replacement-payload verification,
new launcher handoff-source verification/citations,
complete installed launcher/runtime integration, exact installer payload/license inventory and
checksums, installer lifecycle validation, remaining installer-recipe citation refresh, source-atlas/handbook
editions and actual GitHub prerelease publication. Source inspection and authored procedures do
not satisfy those gates. The01:11 integration above records completed component and current-candidate
fixture checks while retaining clean committed release-build/publication gates.
[Workstream58](../TODO/58_INSTALLER_RELEASE.todo) owns completion status;
the [installation chapter](INSTALLATION.md) owns the concise user procedure.
The initial authored-doc checks found221valid local links and39new-guide citation occurrences across
14source files with valid line bounds. Both new accessible diagrams parse/render locally; quoted
frontmatter and book/catalogue structure pass. No generated editions were written by those checks,
and evolving source still requires final citation review.

The existing23WPF/10READMEimages still illustrate the unchanged desktop app; none is presented as an
installer screenshot or installation test. SmartScreen/policy blocks must be respected. Third-party
notices are bundled by the build; the owner has not chosen a project license.

## October9 19:36: keyboard control and answer size — DONE locally

The owner approved keyboard-first interaction rather than cursor locking/hiding/warping, plus an
answer-size slider. Current source adds Ctrl+Alt+K / Keys activation, local Control/Select/Edit/Read/
Command/Search/Devices/Help modes, explicit Enter review then Ctrl+Enter submission, deterministic
built-in commands and a best-effort foreground return. Ordinary mode changes and masking preserve
ongoing capture/provider work. `:devices` refuses busy audio/answers and otherwise opens setup,
closing Q/A; `:stop` and `:cancel` remain different operations.
(src/PhantomFudge.App/MainWindow.Keyboard.cs:16-28,65-175,218-370;
src/PhantomFudge.App/MainWindow.KeyboardSelection.cs:110-159;
src/PhantomFudge.App/MainWindow.KeyboardDevices.cs:42-71.)

The shortcut schema now allows A–Z except E and F1–F11, with F12 rejected and fixed Ctrl+Alt+NoRepeat.
Corrupt/conflicting shortcut configuration retains the Keys fallback; changes require restart.
Answer size is 8–24 for this app session, with 9 compact /10 normal defaults until chosen; a choice
survives resize, not restart. The [guide](KEYBOARD-CONTROLS.md) documents exact JSON and usage.
(src/PhantomFudge.Core/KeyboardShortcutConfiguration.cs:7-28,31-114;
src/PhantomFudge.App/MainWindow.AnswerFont.cs:9-43.)

**202234 first integrated pass, reported at20:32:** the implementation owner reports **all33keyboard
checks and the existing UI fixture passed**, alongside **297 Core tests** (including the F12 fix)
and **18 docs-tool tests**. The imported illustration set contains **23 WPF images /10 README
derivatives**. The three new keyboard images were also visually inspected during documentation
handoff: selection/current-row distinction, a command before execution, and Read with Size14.
They are synthetic examples, not real audio/model or recipient-capture evidence.

The size 24 / 470 × 560 correction is included in the first integrated pass:
compact Select/Search temporarily hide Q/A for transcript reading, then restore
the intended expanded state without close/cancel. Other compact keyboard modes prioritize Q/A;
chosen size 14+ with a visible answer does so even outside keyboard control. Wide columns and
capture/provider lifetimes are unchanged. See [compact behavior](KEYBOARD-CONTROLS.md#compact-windows-the-active-task-gets-the-reading-space).

**20:36 runtime freeze and final exporter:** the implementation owner reports the full UI fixture
passed after the review corrections. `LoadKeyboardShortcut` now catches invalid configuration,
including `InvalidDataException`. Edit remains armed after submission; async interpretation changes
the reply/original target only to a visible editable field, with focus retargeting limited to the
already-active foreground app. The synthetic checks use a natural dispatcher turn, not a forced
mode reset. Compact Select reading-space and passive size24fit assertions also passed, and the
23WPF/10READMEimage set is current.
(src/PhantomFudge.App/MainWindow.Keyboard.cs:38-63,127-139,301-306;
src/PhantomFudge.App/MainWindow.KeyboardDiagnostics.cs:21-28,77-81,142-148,170-193.)

**Final evidence:** fixture203556passed all38keyboard assertions and existing UI regressions,
including configuration fallback, natural-dispatcher editor recovery and minimum-size reading.
The one-shot migration updated359citation ranges across32documents with four changed endpoints
reviewed. All177first-party source files have authored atlas coverage; full atlas and handbook
HTML/PDF editions were built and inspected. The new chapter has three UI images and two diagrams.
No real audio/model call, current-user app restart, new package, commit or push occurred.
Workstream57 is **DONE** for implementation and maintained documentation, not platform qualification.
Real foreground return, shortcut collisions, non-US layouts, accessibility, owner UX and recipient
capture/cursor behavior are not qualified by source inspection or synthetic test definitions.

## Historical October9 10:33: optional question interpretation

Current source adds **Clarify first**, off on launch. Interpret the chosen question, review the
model's proposed meaning, then explicitly send Yes or a correction. The original draft is retained
separately from the reply. Fixed task metadata and this question's bounded clarification exchanges
are the only additional context; normal Ask remains history-free. See
[usage and limits](QUESTION-CLARIFICATION.md) and [workstream45](../TODO/45_OPT_IN_QUESTION_CLARIFICATION.todo).
Combined source/UI evidence is `.local\handbook-capture-20261009-105245`: all20selection and
16clarification checks pass, together with the existing question/answer/opacity/cursor/layout guards.
The final combined affected Core run passed270tests; the earlier provider-focused run passed227.
Only fake providers/transports and synthetic content were used. No live-model wording or latency
claim is made. The full illustrated handbook has been rebuilt and reviewed for this source handoff.
No new ZIP is planned:045022 predates both clarification and direct multi-line selection.

## Historical October9 10:07: source-only multi-line transcript selection

The owner requested direct Mic/Computer transcript clicks, then explicitly added multiple lines.
Current source implements whole-segment toggles without Ctrl, displayed-history-order composition,
raw text without generated prefixes, and independent question editing without stopping transcription.
Q/A is expanded by default. Selection opens it if needed, closes source/setup/Notes panels and takes
deliberate focus; it does not freeze a snapshot or submit a model question.
(src/PhantomFudge.App/MainWindow.TranscriptSelection.cs:85-147;
[`InitializeQuestionView` / `SetQuestionEditing`](../src/PhantomFudge.App/MainWindow.Questions.cs).)

Edited drafts reject further direct selection with inline retained-draft status until **Clear
question**, preserving highlights rather than destroying corrections. Partial identity/selection
survives revisions/finalization before editing. Main visible history retains selectable structured
rows after the256-entry/64000-character chooser eviction; the unchanged250000-character
final/session display-buffer limit still applies. Archived unfinished rows remain provisional;
separators are not selectable. New finals follow the scrollbar only when already at the end.
Native ListBox keyboard/automation selection uses the same composition path.
(src/PhantomFudge.App/MainWindow.TranscriptSelection.cs:13-147;
[`TranscriptBuffer`](../src/PhantomFudge.Core/TranscriptBuffer.cs).)

All selection composition goes through `QuestionDraft.ReplaceSelection`: one capture session,
at most256entries and8192UTF-16 units including newlines, atomic rejection and no truncation.
The legacy **Choose source → Mic / Computer / All → Use selected** replacement path remains.
Selected rows keep shared background alpha, white/black text and thin accent outlines.
([`QuestionDraft`](../src/PhantomFudge.Core/QuestionDraft.cs);
[`UseQuestionSelection_Click`](../src/PhantomFudge.App/MainWindow.Questions.cs);
[`TranscriptRowStyle`](../src/PhantomFudge.App/MainWindow.xaml).)

**Final source validation:** `result.json` and `transcript-selection.json` in
`.local\handbook-capture-20261009-105245` report PASS. All20direct-selection checks are true,
including retained draft after Clear transcript; existing question/answer, minimum/responsive
layout, alpha, cursor and style guards also pass. The receipts report zero real model/cloud
requests and no recording/audio capture. The parent additionally reports **118PASS** for the
targeted existing Core question/transcript suite; the later combined run passed270.
The final exporter refreshed **17WPF +5READMEimages**, including the clarification flow.
The actual [multi-selection image](handbook/assets/screenshots/transcript-multiselect.png) is linked
in the guide and handbook; these are synthetic renders, not desktop/meeting captures.

**Runtime is frozen; implementation and synthetic validation are complete.** Final changes after
the first smoke made retained-edit status visible when Q/A was hidden, added the twentieth
clear-transcript/draft regression and reset the synthetic fixture for readable answer images.
Citation migration, full handbook/illustration review and current editions are complete for
this local source handoff; **TODO44 and45 are DONE** within their scoped evidence boundaries.
(src/PhantomFudge.App/MainWindow.TranscriptSelectionDiagnostics.cs:11-145;
[workstream44](../TODO/44_TRANSCRIPT_CLICK_SELECTION.todo).)

**Delivery:** updated source via `scripts\Run-PhantomFudge.ps1`; no new ZIP is planned for this
small change. Save/copy needed content, close the old app with **X**, then run the checkout.
The045022ZIP below remains an immutable historical quality candidate and **does not contain
click selection**. No real mic/model/org access, service operation or new login is part of this
selection/documentation update. Owner usability, accessibility and recipient capture/cursor gates
remain open; synthetic UI success does not qualify them.

## Historical October9 quality correction and045022candidate

The current source implements the expanded monitor-clamped reading workspace, exact white/black
reading text, transparent answers, quiet Details, local hybrid ASR and direct prepared Agency answers.
See [quality results and limits](QUALITY-UPDATE.md) and [diagnostic retention](DIAGNOSTICS.md).

| Validation boundary | Recorded result |
| --- | --- |
| Source UI | Fifteen synthetic WPF images; responsive/minimum/recovery/alpha assertions. Final source identity is in the screenshot manifest. |
| Local speech | Final106 service + 18 benchmark tests; native46 groups and actual C#→hybrid synthetic PCM integration reported by the runtime implementer. |
| Speech quality | Synthetic WER 12.04→4.40%, CER 6.37→0.263%; final median/p95 2.736/3.017 s. Actual microphone unqualified. |
| Agency H exact text | Three complete prepared answers: 4066/4176/2992 ms; 33 compiled correctness checks passed. Cold preparation about 20–31 s, not under five. |
| Provider regression | Earlier127 targeted fake-provider checks; parent final full Core345/345 passed after all three review fixes. |
| Diagnostics | Parent's metadata-only export check: 1979 app + 1 speech records, no Text, no upload. |
| Documentation | Ten existing parser tests pass; full source-rebased edition/freshness tracked by handbook manifest. |

Historical quality candidate: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261009-045022.zip` (unsigned, local only; no click selection).
Candidate SHA256: `D24E8D2C5A1AF30E3F72FDA07F535B43D730420A81CE67115645605B7667D553`.
Candidate size/files: 76,383,294 bytes; 508 files.
Exact-package fixture: passed all synthetic UI/question/answer checks, including capture-before-cleanup and alpha26/255; modelRequests0, cloudRequests0, no recording/audio.
Package audit: eleven required service files, eight identical script pairs and five public JSON receipts present; no weights, secrets, private profiles, PCM, context stores, logs or debug symbols.

The parent provisioned selected models and upgraded only the authenticated owned idle service,
observing ready/hybrid-small.en/inactive/diagnostics available. Documentation does not restart it.
The old running app is not force-closed: save/copy needed content, close with X, then launch the update.
Prepared answers are not a universal five-second SLO. All 48 correction-study client submissions,
earlier wrong/slow answers and speech failures remain in public sanitized receipts. No new model
prompt, live microphone/playback, organization-data connector, auth flow, upload, commit or push is
part of documentation or candidate validation. TODO13/32 and broader owner/hardware gates remain open.

## Historical installed Agency answer preview

The21:24 request adds selectable installed answer providers while keeping direct Azure future.
Common streaming state/UI, constrained installed-provider adapters and setup are implemented.
One owner-approved direct Copilot smoke attempt failed at the customization/tool gate; no successful
model answer is claimed and no automatic retry was sent. Its model-call outcome could not be ruled out.
The owner subsequently rejected the organization-authentication popup and requested existing Agency
background prompting with no org-data access. The owned login was stopped; app configuration was
reset to SnapshotOnly. A separately authorized Agency-only synthetic prompt then returned PHANTOM_OK
with exit0, tool_count0, no tool requests, both builtin MCPs disabled and no new login flow.
The one observed run took69.881seconds; it is not a performance benchmark. See answer-provider-smoke.json.

Historical local ZIP: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-235421.zip`,
76,252,854bytes/489files, SHA256
`88D98E7CAFA56B067A521571778D0F661A879DDCBF5EBBE9D412987D2BFA5A69`.
Core268tests and source/exact-package synthetic UI pass, including early streaming, exact snapshot,
cancel/stale output, clear, invalid setup and minimum answer layout. No real model or audio was used
by UI tests. Agent-specific recorded-event replay/process tests passed without another cloud call.
Setup now selects Agency locally; open Question and Reload setup in this new preview.
No owner app/service was force-stopped. Direct Copilot cloud remains unvalidated; real local-model
quality/latency and recipient capture qualification remain open. Earlier candidate receipts follow.

The historical15:05 research was followed by the17:37 implementation authorization. **TODO28 became local code**:
structured question selection/typing and immutable Ask snapshots existed. **That snapshot-only slice sent nothing**.
Source commit, Azure auth/client and streamed answers remain future work. No cloud latency result,
model deployment, account lookup or data upload occurred. See LOCAL-QUESTIONS.md and the latest
question-preview receipt at the end; older packages below remain historical, not overwritten.

Latest14:01 input extension: Computer, Mic or Both after explicit Start. The owner's request
supersedes the original never-microphone product scope. Separate WASAPI tracks, source-prefixed v2
transport and independent Python/Vosk decoders produce labelled finals and independent partials.
No chat access or speaker-identity inference exists. OS mic indicators/echo remain external limits.
See DUAL-SOURCE-AUDIO.md. Development checks opened no real mic/render endpoint and played no audio.

Earlier dual-source evidence: Core41tests, native46synthetic groups, backend71tests and4manager-upgrade
guards pass. Real generated dual PCM through Python yielded Computer11/Mic8partials and3finals each,
correct keywords/source separation and tail drain. Actual C#v2->Python yielded Computer13/Mic10partials
and2finals each with expected phrases. These prove protocol/model integration, not physical mic,
device permissions, echo quality or microphone-indicator/capture behavior.

A review-found stop/restart race was fixed: Start/mode/clear stay blocked through cleanup/final
draining, preventing late old-session IDs from discarding new-session text. Synthetic UI regression
also covers modes, both passive device selectors, source labels, themes and prior note safeguards.

Earlier dual-source bundle:
`artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-145231.zip`,
76,184,066bytes/485files, SHA256
`BC89867277F3E8C8BAA179BE74B7F517A98D6E09CBE35534AFB21E12E127C846`.
Exact packaged UI passes with `recordingStarted:false`; evidence is `.local\packaged-dual-source-ui`.
The default source-launch build is refreshed. No live microphone/output endpoint was opened,
no chat content was read, and no user process/service was stopped for this update.

Latest UI update: transcript-first with Notes collapsed, default non-activating Present and
standard Arrow cursor. DeepSeaFoam is default; sun/moon switches Harbor Daylight and Plain B/W
resets the neutral theme. Canonical palette provenance and the unresolved recipient-cursor report
are in CURSOR-AND-THEMES.md. No universal no-reveal guarantee is claimed.

Core33tests and local synthetic UI regressions cover palettes/contrast, default foreground
preservation, MA_NOACTIVATE3, Arrow policy, primary/collapsed layout, lossless notes, alpha and
scrolling. Passive device selection explicitly avoids ListBox's focus requirement. No audio was
captured or played, no user process was stopped, and no live sharing recording was made.
The app no longer opens an automatic unprotected protection-failure message box.

Current UI bundle: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-123245.zip`
(76,167,343 bytes;484 files), SHA256
`75544D20F3C5CB70E9F8DF5E4B99AD4BBFA6E55AB61F52FE33274F80FED8F1EC`.
Exact packaged UI regressions pass, including passive device selection and scrolling. Evidence:
`.local\packaged-transcript-first-ui\result.json`. No new unprotected positive-control capture
or live meeting recording was run while the owner's existing app was active.
The already-running app was left untouched; save/close it before opening this new bundle.

Latest architecture correction (owner03:25): **Docker is not required.** Native C# GUI/audio
already uses Python recognition; normal setup/start/stop now provisions and manages native Python
directly. The app launcher starts/reuses that service, with NotesOnly available and no automatic
audio capture. Docker recipes are retained only for optional final-stage packaging after native release.
Task22 tracks native runtime;23 contains the requested mute-independent recognizer comparison.
Both are complete within their scoped evidence:37service/management tests, normal launcher/recovery/
owned-shutdown and real silent C#->Python ASR pass.32synthetic cases/164.07s compared all3engines:
SAPI45.89%WER/10.139s, Vosk8.22%/41.754s, Whisperbase.en4.79%/25.745s warm decode.
See SPEECH-BENCHMARK.md for normalization, model-load/RTF, TTS limitations and live-latency caveats.
Whisper is a stronger quality candidate, not yet substituted for the current true-streaming Vosk path.

Earlier native bundle:
`artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-035959.zip`,
SHA256 `DED2F7231064F3705F5E5F248F751671DF0B741F9E941A3908999F456C2D7426`.
It includes the C# app, Python service source, pinned requirements/model manifest and normal
setup/start/stop/launch scripts. No interpreter, model weights, venv or token is baked in.
An isolated extracted copy successfully created its own venv/token from cached public model data,
started native Python and the packaged app, recovered/minimized/hid/closed through PID-scoped
window enumeration, and stopped the owned service. The validation tree/processes were removed.
This does not claim a clean physical machine or container test.

Latest rendering change: Background now adjusts10-100% (default55%), with solid note/transcript
text, opaque header/setup controls and no click-through. Local layered-WPF proof retains affinity17
and zero background differences in16protected samples. UI alpha26/255, solid text, stable HWND,
caption/resize/editor hit tests and removed acknowledgments pass; task21 tracks remaining remote/
hardware/readability acceptance. Earlier opaque packages below are historical.

Current local ZIP: `artifacts\phantom-fudge-0.1.0-preview-translucent-20261008-032051.zip`.
SHA256: `6BA27F4CF2DD5F64F86F02263594394694AF9A80FB03E5DE3948B536B4BDA1B8`.
Default launch build is refreshed. Exact packaged UI/alpha/native hit-region and layered GDI
probes passed; native minimize/relaunch/hide/close also passed. No audio capture/playback/upload occurred.

Preserved October8 03:04 owner change: permission and recipient rehearsal are handled externally.
Both checkboxes and all associated Start/Present/reset confirmations are removed; explicit
Start/Stop, technical checks and unsaved-content warnings remain. The updated default build
and `artifacts\phantom-fudge-0.1.0-preview-no-prompts-20261008-030741.zip` are local.
ZIP SHA256: `2B0ED8D92BB325EA657E875DA9323605725D6A2B1F7A1E28663D3640E27285BD`.
Updated synthetic UI behavior passed with no audio started. The earlier package below is historical.

## Implemented

- .NET 10.0.112 WPF/Core solution, normal-user Windows 11 x64 application, layered tool-window
  style, pre-show capture affinity, safe generic title, and native composition flush.
- Plain UTF-8 notepad editing, find/wrap/font size, explicit New/Open/Save/Save as, unsaved
  confirmations and in-memory transcript export. No automatic note/recovery/audio persistence.
- Minimal drag/resize chrome, global hide/Edit shortcuts, current-user named-pipe second-launch
  reveal/reset and explicit read-only/non-activating Present mode without acknowledgment gates.
- Selected Computer/Microphone/Both WASAPI inputs using NAudio 2.2.1, opened only after explicit Start.
  Separate source tracks; device loss stops and there is no automatic default-device switching.
- Strict literal-loopback authenticated WebSocket transport, bounded PCM queue, partial/final
  schema checks, silence handling, startup cancellation, final draining and resource cleanup.
- Protected transcript pane with explicit Start/Stop, per-source meters,
  in-memory cap and source-labelled export. Selected output includes other apps and audible sidetone.
- Offline English Vosk service, explicit verified-model/token/native-Python provisioning, health/auth/
  instance-scoped lifecycle and protocol/resource gates. Dockerfile/Compose are optional future packaging.

## Historical initial-preview evidence (current dual-source evidence is above)

| Scope | Actual result | Boundary |
| --- | --- | --- |
| Core tests | 25 passed | File/text/transcript policies, including preservation of unfinished recognition |
| Audio client harness | 20 synthetic groups passed | Auth/schema/queues/cancellation/stop/device-notification logic |
| Python service suite | 37 passed | Protocol/fault plus authenticated native-lifecycle/ownership tests |
| Real local speech engine | Synthetic speech produced expected words and partial/final events | Native Python, not Docker; no real meeting quality benchmark |
| C# socket -> Vosk | Real synthetic PCM recognition and final drain passed | Transport/model integration, not hardware capture |
| User-approved actual playback loopback | 11 partials, 3 finals, 37 nonzero-meter updates, expected phrase matched, zero faults, capture stopped | Default render endpoint on this Windows machine; no microphone or audio file |
| Synthetic native UI | Edit/Present state, text preservation, protected styles, three render endpoints, local layout renders | In-process rendering is not capture exclusion |
| Native recovery/close | Blank app minimized/recovered, hidden/recovered, then native-close exited cleanly | Own temporary processes only, no audio |
| Synthetic cropped GDI proof | Positive control 100000 differing pixels;16 protected samples matched underlying baseline exactly | Specific BitBlt samples, not continuous first-frame proof or any meeting backend |

Native environment: Windows 11 build26200 x64, .NET Desktop10.0.12. Local synthetic evidence
is retained in `.local\capture-proof-initial` and `.local\ui-smoke-initial`, not public personal
desktop screenshots. The getter returned affinity17 for this opaque HWND; this does not settle
every platform's documented getter restrictions.

The actual playback test was explicitly approved by the user. It played generated English
speech and routed default-render loopback through the real local recognizer. Raw PCM remained
in memory. The temporary native validation service was stopped afterward.

Read-only review found and fixed confirmation/save reentrancy, invalid endpoint parsing,
unfinished-recognition loss on restart, reentrant native Close, and minimized-window recovery.
Targeted regression coverage and the final packaged UI/GDI probes passed after those fixes.

## Earlier local packaged artifact

- ZIP: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-024518.zip`
- SHA-256: `518B597F1DCCA3C151EB2366420A27AD366E4AB79FDD4B15C8DA8D9353543E47`
- Size: 76,123,503 bytes;469 files; self-contained .NET/Core Desktop runtime10.0.12.
- Packaged native assembly SHA-256:
  `091B321749551F96B45182219917B878954F7FFD7DB273DF6D2095E76E4E6DA3`.
- Exact packaged synthetic UI and16-frame GDI probes passed; evidence lives in
  `.local\packaged-ui-smoke` and `.local\packaged-capture-proof`.
- Archive includes dependency notices; no private context, token, raw audio or debug symbols.
- This is local and unsigned, not uploaded, clean-machine certified, or a public release.

## Required but not completed

- **Deferred Docker execution:** not a native runtime/release dependency. Container build/mount/network/
  offline/resource/shutdown behavior remains unexecuted and is intentionally left until the final stage.
- Teams, Zoom, Meet and OBS qualification, plus full transcript-pixel and popup/IME capture matrix.
- Complete first-frame/failure evidence; alternative GPU/DPI/headset/HDR/ARM64 and long-session soak.
- Owner usability, real-meeting transcription quality/latency, accessibility and presentation-focus approval.
- Configurable hotkeys, opt-in recovery and persisted placement are unfinished target-plan features.
  Current defaults are explicit saves, no crash recovery, fixed shortcuts and recenter-on-relaunch.
- Final license/signing/distribution choices and public release authorization. No commit or push performed.

The owner went offline and required no further questions/uploads or risky system changes.
No additional playback, Docker installation, live meeting capture, or external publication was performed.

Native transcription19/runtime22 remain release gates; Docker20 does not. Preparing the service
is not claiming it ran in a container. The local preview can be developed/tested while native gates remain open; it must
not be advertised as universally uncapturable or as a finished qualified release.

## Documentation edition

The handbook adds source-grounded chapters, diagrams, a public JOURNAL and a local HTML/PDF pipeline.
The documentation-only `--documentation-screenshots` fixture renders actual WPF controls with
synthetic text/device labels, without starting capture. Its images are not desktop screenshots or
capture-exclusion evidence. The current preview ZIP above was not replaced for this documentation task.
Edition provenance/counts live in `handbook\manifest.json`; maintenance is required by workflow06.

## Historical local-question snapshot slice (owner17:37)

Four Core types supply structured source/session/utterance/revision identity, bounded history,
selected/typed8192-character drafts, user-edit detachment and immutable deep snapshot copies.
Native events carry a per-Start local GUID before dispatch, rejecting stale callbacks without changing
v1/v2 wire schemas. Legacy transcript display/export is unchanged.

At that handoff the inline Question panel defaulted to Mic filtering and required row selection/Use selected,
and permitted separate question typing without stopping audio or enabling notes editing. Ask froze
exact displayed text locally; no provider/network was connected in that slice. Copy/Clear and unsaved-exit handling
were explicit. Armed live-Mic source boundaries still await29commit; approved Azure connection30 precedes31.

Current evidence:137Core tests (96new +41existing),46native synthetic groups, and source/exact-package
UI checks pass. UI covers source/session isolation, no implicit Ask, frozen partial/final revisions,
LF/CR/CRLF fidelity, oversize/empty rejection, typing independence, theme preservation, clear and
470x560layout. No real input endpoint was opened. The first source UI run failed its preexisting
foreground-preservation assertion; an identical-source repeat and later runs passed. The cause was
not established; no remote cursor, first-frame or owner acceptance is inferred.

Historical local ZIP: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-183107.zip`,
76,205,175bytes/486files, SHA256
`C5062B6B94F3ABC63901960F1572F1A9141EA128C2CA513E6D26C4C5C3FAB27A`.
Packaged synthetic evidence: `.local\packaged-question-ui\result.json`. Twelve source-generated UI
images are maintained with provenance; they are not desktop/meeting captures. Existing owner
processes/services were not stopped. No upload, publication, Azure operation or real meeting access.

## October9 11:07: annotated source reference

Documentation now includes a [searchable source atlas](source-atlas/index.html) and
[476-page annotation PDF](source-atlas/Phantom-Fudge-Source-Atlas.pdf):163first-party files,
23686physical lines and956authored explanation blocks. Each physical line belongs to one
non-overlapping block; complete escaped source is available in HTML, while the PDF prints
explanations and focused excerpts.33generated evidence files,8lockfiles and18byte-identical
PowerShell mirrors are classified separately in the [coverage ledger](source-atlas/coverage.json).

Seven new handbook chapters explain important statements, call chains, invariants, bounds,
state transitions, failure preservation and test negative controls. The full handbook includes
65diagrams; final edition counts and hashes live in its manifest. The atlas's local reader supports
identifier/content search and stable source-line links. Both editions include freshness gates.
This is documentation/tooling work: all runtime and non-documentation-tool source hashes remained
unchanged. No application, service, audio, model, authentication or release operation was performed.
Mechanical line coverage is not a claim of complete semantic understanding or external-library
qualification. [Workstream46](../TODO/46_ANNOTATED_SOURCE_REFERENCE.todo) records the delivery.
