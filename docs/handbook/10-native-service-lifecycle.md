---
title: "10. Native service lifecycle: readiness without accidental capture"
description: "Trace installed startup handoff, explicit capture, verified hybrid provisioning, owned idle upgrades and authenticated shutdown."
outline: deep
---

# 10. Native service lifecycle: readiness without accidental capture

## Why process ownership is a protocol problem

“Something is listening on port 8765” is insufficient evidence that Phantom Fudge may reuse or stop it. The listener could belong to another checkout, lack the installed client's speech version, run the wrong recognition configuration, or be transcribing an active session. `manage.py` treats a local service as owned only after combining a workspace receipt, authenticated runtime response, expected service identity, management version, and matching instance UUID. It does not convert a discovered PID into permission to terminate a process. (`services\transcription\manage.py:94-165`, `read_record`, `owned_info`; `services\transcription\manage.py:186-200`, `stop_owned`.)

The second boundary is **ready service versus recording application**. Initialization provisions dependencies and weights. Start makes an idle recognizer service available. The application still requires explicit Start before endpoint acquisition, and transport readiness precedes native capture. Keeping these operations distinct makes a launcher convenient without turning it into an automatic recorder. (`scripts\Initialize-Transcription.ps1:119-165`, Python setup; `scripts\Start-Transcription.ps1:8-15`, native start; `src\PhantomFudge.App\MainWindow.xaml.cs:652-700`, `Start_Click`; `src\PhantomFudge.App\Audio\LoopbackTranscriber.cs:102-115`, `StartCoreAsync`.)

## Installed preview: a different entry point, the same capture boundary

The planned v0.2.0-preview.1 Windows11 x64 installer replaces end-user PowerShell/Python setup
with a native Start-menu launcher and a bundled runtime/model payload. Candidate2 built and
lifecycle3 passed bounded current-host checks; the clean committed release build and publication
remain **pending**. Follow
[Installation](../INSTALLATION.md) for the short procedure and
[implementation status](../IMPLEMENTATION-STATUS.md) for evidence.
(scripts/Build-Installer.ps1:30-44)
(tools/Installer/PhantomFudge.iss:23-30,62-76)

`Program.Main` first refuses normal/notes-only launch when the installer-maintenance gate is
present, before revealing an existing same-user app or touching files/service state. Fresh launch
rechecks the gate after acquiring its startup mutex; detection exits70 through generic status,
without creating WPF UI or child processes. Maintenance and validation bypass this gate so Setup
can still perform its own checks. A fresh normal launch starts the manager through the installed Python interpreter,
with fixed arguments and offline environment values, then opens the app. `--notes-only` skips
speech preparation; it neither shuts down a separate service nor disables providers.
Installed mutable runtime data belongs to
`%LOCALAPPDATA%\PhantomFudge\InstalledRuntime\.local`, outside the replaceable program tree.
(src/PhantomFudge.Launcher/Program.cs:25-78,127-213)
(src/PhantomFudge.Launcher/LauncherContract.cs:152-165)
(src/PhantomFudge.Launcher/LauncherStatus.cs:15,34)
(src/PhantomFudge.Launcher/ChildProcesses.cs:48-105)
(src/PhantomFudge.Launcher/LauncherContract.cs:56-84)

Setup never starts the app: finish and close it, then launch from Start. Setup/launcher progress
and errors are ordinary visible UI, not covered by the notes HWND's capture-exclusion request.
Launch before sharing. Once the app child starts, the launcher keeps its operation mutex until
the main-instance mutex appears or that child is proven exited. After30seconds, status76 means
**pending**, not success; Close hides progress while monitoring continues, including a guard
retained through early dispatcher shutdown. This prevents maintenance from replacing an
initializing app merely because a timer elapsed.
(tools/Installer/PhantomFudge.iss:14-76)
(src/PhantomFudge.Launcher/LauncherWindow.cs:18-57)
(src/PhantomFudge.Launcher/ChildProcesses.cs:12-42)
(src/PhantomFudge.Launcher/Program.cs:108-118,186-227)

Maintenance refuses an open app or concurrent launcher operation, takes the shared runtime lock
and uses authenticated loopback shutdown only after matching the installed ownership receipt.
It does not require a Python `maintenance` verb or working old Python binaries. Update/repair uses
a fresh helper directory extracted from Setup so damaged old launcher files do not prevent repair;
the explicit-root native path requires the installation marker but not a healthy compact manifest.
Uninstall invokes
the installed launcher. Neither path may stop another
source workspace's listener. The installer keeps external settings/diagnostics and saved
notes/exports for reinstall; it is not a data wipe. Candidate2's lifecycle3 receipt passes 11 checks
against 3,682 inventoried files, including repair/uninstall and retained external data. This
random-AppId/path fixture omits shortcuts and does not start the app or speech service; its
simulated older-version record is not a genuine old-binary upgrade or clean-VM test.
(src/PhantomFudge.Launcher/Program.cs:50-58,94-100)
(src/PhantomFudge.Launcher/LauncherContract.cs:87-105)
(src/PhantomFudge.Launcher/MaintenanceOperation.cs:48-118)
(src/PhantomFudge.Launcher/OwnedServiceClient.cs:20-58)
(tools/Installer/PhantomFudge.iss:143-161)
(scripts/Test-Installer.ps1:15-18,31-84,96-104)

Partial ownership does not authorize shutdown. A retained token alone permits only a credential-free
port-presence check; a receipt without its token refuses. No token and no receipt is the untouched
case and does not probe another workspace's listener. See the
[installed reference](19-api-configuration-reference.md#installed-launcher-reference) for the exact cases.
(src/PhantomFudge.Launcher/MaintenanceOperation.cs:52-75,121-134)

The installed model contract keeps explicit provisioning in the mutable user's
`.local\models` directory. An existing pinned-name user model takes precedence over the bundled
weights and must pass the same manifest verification; only absence selects the bundled path.
This prevents update/uninstall from overwriting deliberate user model data. Targeted path tests
cover precedence and user-only provisioning without inference.
(tools/Installer/runtime-contract.json:7-12,20-22)
(services/transcription/tests/test_installed_runtime.py:74-91)

Normal startup also recovers confirmed stale ownership after reboot or PID reuse; it is not a
manual-repair-only path. The child PID is persisted before querying its creation time, so an
inspection failure cannot discard the known PID. A PID-only record can later be cleared only
after proving that process absent; live/ambiguous initializers remain protected. Regression tests
call the actual manager start path with mocked process/port observations, not a real reboot or
speech service.
(services/transcription/tests/test_installed_runtime.py:98-198)

The parent confirms85 focused runtime tests, superseding78; the final offline rebuild passes real generated-PCM hybrid inference,
including production VAD, a forced Whisper encoder/decoder pass and natural exit after Unicode
relocation. Vosk remains unmodified; `VoskFactory` uses a locked, temporary working-directory
adapter for Windows non-ASCII model paths. These checks resolve the earlier runtime blockers,
not captured-speech quality, clean-VM or new-backend performance qualification.
(services/transcription/recognizer.py:47-77)
(services/transcription/tests/test_packaged_pcm_runtime.py:35-97,126-138)

## Source/legacy journey: provision, launch, select, stop

| Entry point | Supported switches | What it changes |
| --- | --- | --- |
| `Initialize-Transcription.ps1` | `-DownloadModel`, `-SkipPythonSetup`, `-Engine`, `-WhisperModel`, `-Vocabulary` | Token, selected verified models, config/receipts, and normally the service-local Python environment; no service or capture start. |
| `Start-Transcription.ps1` | `-Docker` | Default: invoke native `manage.py start`; Docker is an explicit alternate branch. |
| `Run-PhantomFudge.ps1` | `-NoBuild`, `-NotesOnly`, `-PassThru` | Build when appropriate, start/reuse service unless notes-only, launch or reveal the app. |
| `Stop-Transcription.ps1` | `-Docker` | Default: authenticated native manager stop; optional Docker branch is separate. |
| `manage.py` | `start`, `stop`, `status` | Serialized native runtime lifecycle and status inspection. |

These are source-supported interfaces, not commands executed while writing this chapter. (`scripts\Initialize-Transcription.ps1:1-11`, parameters; `scripts\Start-Transcription.ps1:1-15`, native branch; `scripts\Run-PhantomFudge.ps1:1-55`, launcher; `scripts\Stop-Transcription.ps1:1-22`, stop branches; `services\transcription\manage.py:301-314`, `main`.)

```mermaid
---
config: {"theme":"base","themeVariables":{"primaryColor":"#2d333b","primaryBorderColor":"#6d5dfc","primaryTextColor":"#e6edf3","lineColor":"#8b949e","clusterBkg":"#161b22","clusterBorder":"#30363d"}}
---
flowchart TB
    accTitle: Three independent lifecycle boundaries
    accDescr: Provisioning enables a service, service readiness enables an explicit capture request, and capture stop precedes idle service shutdown.
    subgraph P["Provisioning"]
        I["Explicit initialization"] --> V["Token and verified models"]
        V --> E["Local Python environment"]
    end
    subgraph S["Service ownership"]
        E --> L["Lifecycle lock"]
        L --> O["Ownership and fingerprint"]
        O --> R["Ready idle service"]
    end
    subgraph A["Application session"]
        R --> U["Explicit Start"]
        U --> C["Selected capture"]
        C --> F["Stop and final drain"]
    end
    F --> H["Authenticated idle shutdown"]
    H --> X["Port closes; receipt removed"]
    style I fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style V fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style E fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style L fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style O fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style R fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style U fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style C fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style F fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style H fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style X fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style P fill:#161b22,stroke:#30363d,color:#e6edf3
    style S fill:#161b22,stroke:#30363d,color:#e6edf3
    style A fill:#161b22,stroke:#30363d,color:#e6edf3
    linkStyle default stroke:#8b949e
```

The diagram's shutdown edge is deliberate: managed shutdown rejects an active speech session. Service readiness can also be reused without traversing provisioning again. (`services\transcription\server.py:247-260`, `managed_shutdown`; `services\transcription\manage.py:203-220`, `start`.)

## Provisioning without hidden repair

Initialization creates a token only if one is absent, using 32 cryptographically random bytes rendered as lowercase hexadecimal. That is 256 generated bits represented by 64 ASCII characters—not a hard-coded credential. Exclusive file creation prevents an existing token from being overwritten through this path. On Windows, the script replaces the token file's DACL with a current-user full-control rule and disables inherited permissions, without changing ownership/audit sections that could require elevation. (`scripts\Initialize-Transcription.ps1:42-76`, token generation and ACL setup.)

Existing token content must match the accepted 32–512-character URL-safe pattern. The setup script does not silently rotate invalid credentials; it requests explicit repair. The manager reads and validates the token, while the server prefers the configured token file over an environment token. These are source-defined storage operations; this chapter neither reads nor cites a user's generated token. (`scripts\Initialize-Transcription.ps1:74-76`, validation; `services\transcription\manage.py:34-41`, `token`; `services\transcription\server.py:75-85`, `load_token`.)

Model provisioning is similarly explicit. The provisional Vosk manifest specifies a 41,205,931-byte English archive and observed SHA-256 identity. Setup checks byte count and checksum before extraction, rejects unexpected extracted content, and refuses to overwrite a failed existing model directory. Runtime performs its own manifest verification; a setup receipt is not a substitute for checking the bytes currently present. That archive size does not describe the additional Whisper weights. (`services\transcription\model.json:2-10`, model metadata; `scripts\Initialize-Transcription.ps1:26-39`, `Test-Model`; `scripts\Initialize-Transcription.ps1:78-118`, archive/receipt path; `services\transcription\recognizer.py:25-46`, `verify_model`, `VoskFactory`.)

Hybrid adds a separately pinned Whisper model. `provision_whisper` accepts `small.en` or `base.en`, verifies the exact file set, lengths, hashes, and nonsymlink paths, and can reuse a verified benchmark cache. Missing bytes are downloaded only with explicit `--download`; invalid existing files cause a repair error. Exclusive `.partial` creation and verification precede replacement, and cleanup removes only a partial created by that invocation. This is explicit setup, not background request-time repair. (`services\transcription\provision_whisper.py:19-44`, manifest/verification; `services\transcription\provision_whisper.py:47-107`, `provision`, CLI.)

Python setup creates a virtual environment if necessary, hashes the maintained requirements file, and decides whether restore is needed from that receipt and an import check. Restore runs pinned installation followed by `pip check`; successful setup does not itself start the service. A requirements hash receipt is a restore optimization, not a claim that every installed wheel is cryptographically attested on each launch. (`scripts\Initialize-Transcription.ps1:119-165`, environment provisioning; `services\transcription\requirements.txt:1-20`, dependency pins.)

## Serializing the launcher without trusting process names

`lifecycle_lock` is Windows-specific. It opens a workspace lock file, ensures at least one byte exists, and repeatedly attempts a nonblocking `msvcrt` lock on that byte. The acquisition deadline is 50 seconds with 100 ms polling. Start, Stop, and Status share this lock, so competing manager invocations cannot simultaneously rewrite the ownership receipt. This serializes cooperating managers for the workspace; it is not an operating-system-wide monopoly on the port. (`services\transcription\manage.py:66-91`, `lifecycle_lock`; `services\transcription\manage.py:203-207`, `start`; `services\transcription\manage.py:276-295`, `stop`, `status`.)

The receipt contains schema version 1, an instance UUID in canonical hexadecimal form, and a PID field. `write_record` writes a temporary sibling and replaces the receipt. `owned_info` authenticates the listener and compares service name, management version 1, and instance UUID. Crucially, it does **not** use the receipt PID as ownership proof or a kill target: process IDs can be reused, whereas the authenticated instance value participates in the shutdown contract. (`services\transcription\manage.py:94-165`, receipt/ownership methods; `services\transcription\manage.py:226-228`, initial receipt; `services\transcription\manage.py:259-262`, PID update.)

Management requests use literal `http://127.0.0.1:8765`, a two-second timeout, no proxy, and a redirect handler that fails rather than forwarding credentials. Reading 4,097 bytes detects whether a response exceeds the 4,096-byte allowed payload. Parsing also requires a JSON object. These are bounded control-plane responses, not transcript downloads. (`services\transcription\manage.py:22-31`, base URL and `NoRedirects`; `services\transcription\manage.py:44-57`, `request`.)

## Fingerprint-aware reuse and verified idle replacement

| Existing listener state | `start()` decision |
| --- | --- |
| No matching receipt | Refuse adoption. |
| Wrong token, service identity, management version, or UUID | Refuse reuse/change. |
| Owned but `ready` is not exactly true | Fail; require explicit stop/setup repair. |
| Owned, ready, speech v2 and matching configuration fingerprint | Reuse; do not restart merely because it is active. |
| Owned, ready, stale speech capability or fingerprint, explicitly idle | Verify selected models/dependency availability **before** authenticated shutdown and replacement. |
| Owned, ready, stale capability/fingerprint, active or ambiguous | Refuse restart; ask the user to stop transcription. |
| Owned idle stale instance, replacement model missing/invalid | Leave the existing service running; require explicit provisioning, never legacy fallback. |

Ownership, speech capability, and engine identity are separate tests. `supports_current_audio` looks for integer `2`, while `supports_current_engine` compares `configFingerprint`. Management `protocolVersion: 1` remains current. Requiring `active is False` is stricter than accepting a missing or false-like field; `validate_start` checks both model families and the hybrid dependency before taking a healthy old idle instance down. (`services\transcription\manage.py:155-220`, ownership/capability/start chain; `services\transcription\server.py:218-244`, `runtime_info`; `services\transcription\configuration.py:60-69`, `verify_config`.)

The fingerprint is more than the string `small.en`. `metadata` includes pipeline version, engine, pinned model revision, compute type, thread count, vocabulary identity, and a digest of its named runtime/dependency/manifest files. Technical vocabulary contributes its own digest. Sorted compact JSON makes the identity deterministic. Thus a ready v2 Vosk listener can still be stale, as can a hybrid listener with old code or vocabulary. This is not an attestation of every checkout file or installed wheel. (`services\transcription\configuration.py:38-57`, `metadata`.)

The following JavaScript-shaped pseudocode translates the manager's Python decisions:

```javascript
function startNative() {
  const credential = readValidatedToken();
  withLifecycleLock(() => {
    const config = loadValidatedConfig();
    const receipt = readValidatedReceipt();
    if (portInUse()) {
      const info = authenticateOwnedInstance(credential, receipt);
      if (info.ready !== true) throw new RepairRequired();
      if (supportsSpeechV2(info) && fingerprintMatches(info, config)) return;
      if (info.active !== false) throw new StopCaptureFirst();
      verifyModelsAndDependency(config); // Before touching the old service.
      shutdownOwnedAndWaitForPort(credential, receipt);
    } else if (receipt !== null) {
      clearOnlyConfirmedDeadOrRecycledProcessReceipt();
      // Live, uninspectable or incomplete ownership still refuses.
    }
    verifyModelsAndDependency(config);
    const instance = newUuidHex();
    writeReceiptBeforeSpawn(instance);
    spawnDetachedService(instance);
    waitForAuthenticatedReadinessVersionsAndFingerprint();
  });
}
```

The real manager keeps its receipt after a slow or failed startup instead of claiming success or killing a process by name. On a later operation with no listener, it may remove a receipt only after process inspection establishes that the recorded process is dead or its PID has been reused. Live or uncertain ownership still refuses. (`services\transcription\manage.py:224-273`, `start`.)

## The cold-start chain

After verifying the selected configuration's models and dependency, `start` generates a fresh instance UUID and writes the receipt before spawning. An existing receipt with no reachable service requires process-identity inspection: confirmed death or PID reuse permits cleanup, while a live/unknown instance or insufficient ownership data refuses a second startup. The service uses the current Python interpreter, detached process/new process group, no stdin, closed inherited descriptors, and a generic local log. The manager polls for up to 45 seconds; readiness requires authenticated instance identity, speech v2, and the selected fingerprint, not merely a listening socket. (`services\transcription\manage.py:224-273`, `start`; `services\transcription\configuration.py:60-69`, model verification.)

```mermaid
---
config: {"theme":"base","themeVariables":{"primaryColor":"#2d333b","primaryBorderColor":"#6d5dfc","primaryTextColor":"#e6edf3","lineColor":"#8b949e","actorBkg":"#2d333b","actorBorder":"#6d5dfc","actorTextColor":"#e6edf3","actorLineColor":"#8b949e","signalColor":"#8b949e","signalTextColor":"#e6edf3","noteBkgColor":"#161b22","noteBorderColor":"#30363d","noteTextColor":"#e6edf3"}}
---
sequenceDiagram
    accTitle: Native cold start does not start capture
    accDescr: The manager verifies selected models before writing ownership and spawning, then checks readiness and fingerprint without starting capture.
    autonumber
    participant L as Launcher
    participant M as Manager
    participant R as Receipt
    participant S as Python service
    participant A as Native app
    L->>M: start under lifecycle lock
    M->>M: Verify selected models and dependency
    M->>R: Write new instance UUID
    M->>S: Spawn detached with explicit paths
    S->>S: Verify and load selected models
    M->>S: Authenticated runtime query
    S-->>M: Instance, ready, versions, fingerprint
    M->>R: Record reported PID
    M-->>L: Ready, no audio started
    L->>A: Launch with local configuration
    A->>A: Wait for explicit Start
    A->>S: Speech session
    A->>S: Stop and drain
    L->>M: Separate service stop
    M->>S: Shutdown with matching instance
    S-->>M: stopping
    M->>M: Wait for listener to disappear
    M->>R: Remove receipt
```

`production_app` loads the selected factory on an executor before returning its aiohttp application. Hybrid verifies/loads Vosk and local Whisper; an error leaves the factory unavailable rather than downloading a replacement or selecting legacy Vosk. `/health` reports 503 when unready; authenticated runtime management can still describe the unavailable instance after it starts listening. (`services\transcription\server.py:201-207`, `health`; `services\transcription\server.py:658-683`, `production_app`; `services\transcription\accurate.py:75-81`, local-only Whisper; `services\transcription\accurate.py:121-129`, `HybridFactory`.)

### Configuration is explicit, not discovered from a meeting

| Configuration | Consumer and behavior |
| --- | --- |
| `PHANTOM_TRANSCRIPTION_TOKEN_FILE` | Server token source; manager supplies it. |
| `PHANTOM_TRANSCRIPTION_TOKEN` | Server fallback token source; launcher also passes a token to the app. |
| `PHANTOM_TRANSCRIPTION_MODEL_PATH` | Required explicit model directory for the production server. |
| `PHANTOM_TRANSCRIPTION_HOST` | Manager fixes `127.0.0.1`; server allows `127.0.0.1` or `0.0.0.0`, not arbitrary names. |
| `PHANTOM_TRANSCRIPTION_INSTANCE` | Managed service instance UUID; enables matching-instance shutdown. |
| `PHANTOM_TRANSCRIPTION_URI` | Launcher supplies the app's literal-loopback speech URI. |
| `OPENBLAS_NUM_THREADS`, `OMP_NUM_THREADS` | Manager sets both to `1`. |
| `PHANTOM_TRANSCRIPTION_WORKSPACE` | Mutable configuration/receipt workspace override; not a replacement for authenticated native receipt ownership. |
| `PHANTOM_TRANSCRIPTION_MODELS_ROOT` | Separate installed model root; avoids storing shipped weights inside mutable user data. |
| `PHANTOM_TRANSCRIPTION_ENGINE` | Explicit engine override, validated as `hybrid-whisper` or `vosk`. |
| `.local\transcription-config.json` | Schema 1; defaults: hybrid-whisper, small.en, four threads, technical vocabulary. |
| Offline model flags | Manager sets Hub/Transformers offline mode and disables Hub telemetry/implicit token use. |

The server binds port 8765; there is no production port environment setting in `main`. Config permits integer thread counts 1–8, model `small.en`/`base.en`, and vocabulary `technical`/`none`; initialization writes four threads rather than exposing a thread-count switch. Unknown config keys fail. The local file path describes the interface, not a file inspected or copied into this chapter. Token values are omitted. (`services\transcription\server.py:75-85`, `load_token`; `services\transcription\server.py:695-712`, `main`; `services\transcription\manage.py:229-242`, environment; `scripts\Run-PhantomFudge.ps1:43-55`, app environment; `services\transcription\configuration.py:8-35`, validated configuration.)

Runtime status also distinguishes recognition readiness from diagnostic availability. Metadata-only JSONL rotates at 1 MiB plus three backups and contains allowlisted events/counters, never speech text or audio. A write failure sets logging unavailable rather than claiming successful persistence; the generic startup log is a different artifact. The schema is explained by the public [service README](../../services/transcription/README.md), not by copying any generated log. (`services\transcription\diagnostics.py:12-43`, schema/failure state; `services\transcription\diagnostics.py:45-77`, `emit`.)

## Shutdown: authority and idleness are rechecked

The manager first requires explicit idle state, then posts `/v1/shutdown` with Bearer authentication and `X-Phantom-Instance`. The server independently rejects browser origins, bad auth, queries/bodies, missing management ownership, mismatched UUIDs, and active transcription. That second active check closes the gap between the manager's earlier runtime query and the actual shutdown request. (`services\transcription\manage.py:186-194`, `stop_owned`; `services\transcription\server.py:247-257`, `managed_shutdown`.)

On acceptance, the server marks itself stopping immediately, schedules managed exit after 0.2 seconds, and responds `{"status":"stopping"}`. The manager then waits up to 12 seconds for the port to become unused, removing the receipt only after that observation. If the port remains occupied, the receipt remains and the operation fails. No arbitrary OS termination is used as a timeout fallback. (`services\transcription\server.py:258-260`, `managed_shutdown`; `services\transcription\manage.py:193-200`, `stop_owned`.)

The timeouts are per phase, not one promised shutdown duration: management HTTP has a two-second timeout, listener disappearance has a twelve-second polling budget, and lock acquisition has its own fifty-second budget. A receipt with no reachable listener is not erased merely because the port is closed: `stop` requires evidence of a dead or recycled recorded process before cleanup, and preserves live/unknown ownership. (`services\transcription\manage.py:50-51`, `request`; `services\transcription\manage.py:77-86`, lock wait; `services\transcription\manage.py:195-200`, listener wait; `services\transcription\manage.py:276-287`, `stop`.)

## Application launch and deployment tradeoffs

`Run-PhantomFudge.ps1` first recognizes an existing app at the expected executable path and requests reveal without building or restarting the service. Otherwise it builds a source checkout unless `-NoBuild`, skips build for a packaged executable, optionally starts the service, and launches the app with token/URI environment configuration. `-NotesOnly` skips service startup; it is not a separate microphone implementation. (`scripts\Run-PhantomFudge.ps1:10-55`, launcher.)

Native Python is the normal path. `-Docker` is explicit, optional, and last—not a prerequisite or the evidence for this hybrid upgrade. The maintained [Dockerfile](../../services/transcription/Dockerfile) explicitly selects legacy Vosk and copies named helper modules; [Compose](../../compose.yaml) remains a separate deployment recipe. It was not executed for this update, and hybrid readiness must not be inferred from it. The start wrapper requires an existing engine rather than installing Docker. This speech service continues to advertise v1/v2; its readiness is distinct from the application's separate Ask/provider lifecycle and does not measure answer latency. (`scripts\Start-Transcription.ps1:8-36`, native/Docker branches; `services\transcription\server.py:218-244`, `runtime_info`; `services\transcription\manage.py:168-170`, `supports_current_audio`.)

## Evidence and operational checkpoints

Management tests cover unauthenticated queries, browser origins, wrong instance UUIDs, unmanaged shutdown, active-session refusal, and idle shutdown. A dual-source test confirms that two recognizers still occupy one active runtime and prevent management stop until final drain completes. Manager unit tests reject missing/mismatched receipts and redirects. These are maintained test definitions, not newly executed results. (`services\transcription\tests\test_management.py:38-90`, management cases; `services\transcription\tests\test_management.py:92-122`, dual/session controls; `services\transcription\tests\test_management.py:126-150`, `ManagerTests`.)

The native lifecycle script exercises blank-window reveal/minimize/hide/close and refuses to touch an existing app or service. It is a different evidence layer from service authentication, and neither layer qualifies live microphones, meeting capture exclusion, or acoustic quality. No lifecycle script, service, installation, or hardware probe was run for these chapters. (`scripts\Test-NativeLifecycle.ps1:16-22`, preconditions; `scripts\Test-NativeLifecycle.ps1:69-103`, lifecycle checks; `docs\QUALIFICATION.md:1-7`, qualification boundary.)

The October 9 production-factory check used real local models on **ephemeral ports**, reporting ready, diagnostics available, zero faults/drops, and an 85-second maximum stop envelope. Its receipt explicitly says port 8765 was untouched. Separately, the later operator handoff reports that the parent actually upgraded the authenticated, owned, idle 8765 instance and observed ready hybrid-small.en with no active audio. That later action is handoff evidence, not something this documentation pass repeated or something the earlier test receipt proves. Readiness still does not qualify an actual microphone. (`tools\SpeechBench\results\synthetic-quality-20261009.json:12706-12742`, production-factory check; `tools\SpeechBench\results\quality-validation-20261009.json:15-17`, validation exclusions.)

### Reader checkpoints

1. Why is a matching PID weaker evidence than the authenticated instance contract used here?
2. A ready listener supports speech v2 but has a stale fingerprint and is active. Why must the launcher refuse replacement?
3. What remains on disk when startup exceeds 45 seconds, and why is that not a successful ready result?
4. Distinguish stopping capture, finishing recognition, stopping the Python service, and closing the native window.
5. A stale owned idle service works, but small.en fails verification. Why must verification happen before shutdown?

### Source-reading route

Follow [initialization](../../scripts/Initialize-Transcription.ps1), [native start wrapper](../../scripts/Start-Transcription.ps1), [manager ownership](../../services/transcription/manage.py), [server management endpoints](../../services/transcription/server.py), [application launcher](../../scripts/Run-PhantomFudge.ps1), and [management tests](../../services/transcription/tests/test_management.py). Together they describe a local, explicitly owned service—not an automatic recorder or an external process supervisor.
