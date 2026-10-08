# Proof-first development workflow

**Current stage: local implementation started October 8; no qualification gate is implied passed.**
The procedural catalogue is in `TODO\WORKFLOWS`. Root workstream statuses, not procedure
definition statuses, are the authoritative progress record.

## Delivery gates

| Gate | Workstreams | Exit evidence |
| --- | --- | --- |
| G0 / M0: decisions and research | 01, 02 | Approved required behaviors/configurations, accepted privacy wording, stack/spike scope |
| G1 / M1: capture proof | 03, 21 | Synthetic-text current-rendering HWND proof, actual API/lifecycle results, recipient recordings |
| G2 / M2: usable core | 04-10,19,22 | Native C# UI/audio plus native Python recognition, no Docker |
| G3 / M3: hardening | 11,12,21,23 | DPI/accessibility/failure/soak/rendering and measured engine results |
| G4 / M4: qualified candidate | 14 then 13 | Package hash, exact-version capture matrix, no required content leaks |
| G5 / M5: handoff | 15 then 17 | Honest guide/support table, owner UX acceptance, approved distribution and release artifacts |
| G6 / M6: optional, last | 20 | Containerize the already-complete headless Python service if later requested |

`16_DEVELOPMENT_LEDGER.todo` spans the lifecycle. `18_FUTURE_SCOPE.todo` is deferred and
does not block v1. No calendar or effort estimate is asserted before the capture proof.

October8 03:25 correction: required transcription19 depends on native Python runtime22, not Docker20.
Docker is only eventual containerization and comes after the native app/release. Preserve the
headless model/protocol boundaries now without imposing container infrastructure on normal startup.
Require real native speech/loopback and transcript-pixel qualification before native release.
Benchmark23 compares actual engines on identical direct PCM while muted; no speaker/mic use.

## Critical path

```text
01 decisions + 02 research
  -> 03 capture proof (STOP if required paths cannot meet the bounded contract)
  -> 04 project foundation
  -> 05 window shell
       -> 06 capture policy
       -> 07 editor -> 08 persistence
       -> 09 hotkeys/recovery
  -> 10 presentation mode (06 + 07 + 09)
  -> 22 native Python runtime + 19 live transcription (Docker not required)
  -> 11 DPI/accessibility + 12 diagnostics/tests
  -> 21 translucency + 23 offline recognizer comparison
  -> 14 packaged candidate
  -> 13 final capture qualification
  -> 15 user/support guide
  -> 17 release acceptance
  -> 20 optional containerization, last
```

Parallel work is possible after the shell boundary is stable: editor/storage, recovery,
and capture-policy adapters can have separate owners. The same owner controls shared
window-lifecycle code until its contract is settled. Do not let separate agents create
competing HWND/style/state implementations.

## G1 spike: prove the risky behavior first

Build only an isolated, synthetic-text prototype after the stack decision is approved.
Create a top-level window in the current rendering mode before showing it, configure tool-window styles,
apply `WDA_EXCLUDEFROMCAPTURE`, and implement a recovery shortcut. Record actual setter
and getter behavior and every auxiliary HWND. Compare with an ordinary unprotected
synthetic positive control.

Record startup, drag, resize, show/hide, Edit/Present focus, and monitor transitions with
OBS plus at least one actual remote meeting path. Smoke-test all four approved product
families before committing to a full core build; exhaustive final qualification comes later.
If accounts/hardware are unavailable, record BLOCKED and obtain an explicit narrowed
proof gate rather than pretending a local recording covers all products.

Go only if the mandatory configurations appear feasible, failures are understood, and
the owner accepts the bounded claim. A normal process being discoverable is not a defect
to "solve" with stealth machinery. If full-desktop exclusion fails on a required path,
consider sharing a specific presentation window/tab or a separate unshared display/device.
Those alternatives change the product contract and require approval.

## Feature delivery loop

Owner preference as of October 8: permission and recipient preflight are performed outside
the app. Never add checkboxes, dialogs or repeat acknowledgments for either workflow.
Keep explicit Start/Stop, technical failure checks and unsaved-data confirmation.

1. Select a ready task; confirm its dependencies and open product decisions.
2. Record scope and acceptance, set IN_PROGRESS, and isolate ownership of shared files.
3. Add deterministic behavior coverage where appropriate, then implement the smallest
   complete vertical slice using existing helpers and chosen tooling.
4. Run the smallest relevant existing test/build checks. Desktop behavior needs an
   interactive Windows session; do not substitute headless CI.
5. For HWND/styles/rendering/popups/framework changes, rerun affected capture scenarios
   before reusing a previous compatibility result.
6. Review real UX with the owner where acceptance asks for it. Record approval separately
   from technical pass/fail.
7. Update docs, task evidence, index, and ledger. Set DONE only when the scoped definition
   of done is met; otherwise leave IN_PROGRESS/BLOCKED with an exact next action.

Actual native and service commands now exist in AGENTS.md, PREVIEW-GUIDE.md and the service README.
Keep synthetic playback opt-in. Do not relabel native Python runs as Docker evidence, or a local
GDI probe as Teams/Zoom/Meet/OBS qualification.

## Evidence and privacy discipline

Use synthetic notes and a clean desktop for capture tests. Include the source tree or
commit identity, exact package hash, OS/app versions, native results, frame counts, controls,
and observed limitations. Keep sensitive logs/captures outside tracked artifacts.

A task receipt records actual actions, not intended file paths. Do not mark release DONE
because a ZIP exists, a source build passes, or a README says supported.

No branch name, remote permissions, license, signing certificate, CI secret, or published
destination is assumed. Resolve them in the relevant tasks. Never upload real speaker
notes or user desktop recordings to CI or third-party services.

## Release flow

1. Freeze a candidate source revision, runtime/dependencies, packaging recipe, and scope.
2. Produce the distributable and hashes; inspect included files for notes, secrets, private
   context, debug dumps, temporary files, and unintended tooling.
3. Run clean-machine installation/launch/recovery/uninstall tests at normal privileges.
4. Qualify the exact package through `QUALIFICATION.md`, including all required products.
5. Publish only exact supported combinations; label untested modes and known limitations.
6. Obtain owner usability and distribution approval. Signing and reputation are separate:
   never promise signing removes every SmartScreen or enterprise policy warning.
7. Deliver only through an authorized destination; retain immutable artifacts and evidence.
   If publication is not authorized, mark local handoff accurately rather than pushing.

## Change and regression policy

Any capture leak in a supported row is a release blocker. Remove or suspend the affected
support claim, provide a hide/stop-sharing fallback, and reproduce with synthetic data.
Do not ask the user to keep using real notes while investigating.

OS/app/browser/GPU/runtime/rendering upgrades invalidate the applicable old result until
retested. Do not auto-update in the middle of a presentation. Rollback must preserve document
data and respect schema compatibility; an old binary is not automatically safe with a new
data file.

Maintain the ledger and current-base offload at milestones. Sibling stores remain read-only.
