# Repository working contract

This repository contains an unqualified native Windows preview and a local speech service.
Do not invent build commands, passing tests, compatibility results, or user approvals.

## Before changing anything

1. Read `README.md`, `TODO\00_INDEX.todo`, and the selected workstream.
2. Read `docs\PRODUCT.md`, `docs\ARCHITECTURE.md`, and `docs\QUALIFICATION.md` for affected behavior.
3. If this machine has a designated `.agent-context`, hydrate it according to the local
   continuity protocol. Public development must not depend on private paths or memories.
4. Preserve existing edits. Implement only an authorized task whose dependency gates are met.

## Non-negotiable distinctions

- Capture exclusion is not a security boundary, process hiding, or guaranteed picker omission.
- A successful native API call is not evidence of recipient-side exclusion.
- An OCR miss is not proof that a frame contains no readable text.
- A build/test pass is not human UX approval.
- Desktop research, a successful spike, and a qualified release are different milestones.
- Keep all unknown compatibility rows `NOT_TESTED`; never fill them from assumptions.

## Implementation guardrails

- Follow `TODO\WORKFLOWS\01_FEATURE_DELIVERY.todo`.
- Keep notes out of a visible surface until its protection setup succeeds.
- On detected protection failure, hide/blank notes and show a generic actionable error.
  This is an application failure policy, not a guarantee against unobservable capture bypasses.
- Use one native top-level surface; the owner requested adjustable background-only translucency.
  Preserve solid text, working hit regions and capture affinity on the layered WPF window.
  Do not reduce whole-window opacity or add click-through. No remote web content, hooks into sharing
  software, injected overlays, kernel drivers, process concealment, or elevation requirement.
- Keep real notes, filenames, clipboard data, and personal desktop captures out of logs,
  tests, repository artifacts, telemetry, and CI uploads.
- Transcription supports selected Computer, Microphone, or Both inputs after explicit Start.
  The14:01 owner request supersedes the old microphone prohibition. Do not read chat apps or
  silently substitute inputs. Native local Python inference is the normal path; Docker is optional and last.
  No cloud fallback or automatic audio capture. Starting the idle service is not starting capture.
- Preserve normal accessibility in Edit mode. Do not fake privacy by breaking assistive technology.
- Any rendering, popup, native-window, or framework upgrade can invalidate capture evidence.
- TODO28's local draft/snapshot slice is implemented; cloud/commit TODOs29-37 remain planned. Do not provision Azure,
  sign in, upload audio/questions, or invoke a model merely to resume. Explicit Ask must
  freeze selected text; no automatic full-meeting prompt, chat reading or implicit cloud-ASR fallback.

## Tracking and handoff

Use numbered `.todo` files, explicit dependencies, dated evidence, and the four defined
statuses. Update the index and development ledger with the same change. Record blockers
and superseded decisions without erasing history. A procedure marked DONE means its
definition is complete, not that an application build or release has occurred.

Implementation and synthetic tests are authorized. No commit, push, public release, signing identity,
Docker installation, or live meeting with real notes is authorized merely by these documents.
The owner corrected the architecture: do not require or install Docker for ordinary operation.
Preserve container-neutral interfaces, then leave Docker work until after the native application.

## Existing local commands

- `dotnet build .\PhantomFudge.slnx -c Release`
- `dotnet test .\tests\PhantomFudge.Core.Tests -c Release`
- `dotnet run --project .\tests\PhantomFudge.Audio.Tests -c Release`
- `.\services\transcription\.venv\Scripts\python.exe -m unittest discover -s .\services\transcription\tests`
- `.\scripts\Run-PhantomFudge.ps1`

Development tests must not open the user's live microphone or record a meeting implicitly.
Use synthetic PCM for dual-source integration. Playback testing requires explicit user consent and paused unrelated audio:
`.\scripts\Test-PlaybackTranscription.ps1 -AllowPlayback`. No other test implicitly opens capture.

## Living book and journal

- Read `docs\handbook\README.md` and `docs\DOCUMENTATION-MAINTENANCE.md` before feature work.
- Update affected handbook chapters, exact source citations, primary contracts and diagrams in the
  same change. The contributor changing behavior owns its documentation; proposals stay labelled.
- Refresh `scripts\Export-HandbookScreenshots.ps1` synthetic UI images for visible UI changes.
  Never substitute user notes, real meeting audio, chats or desktop screenshots for the fixture.
- Append significant decisions, corrections and evidence to root `JOURNAL.md` and the TODO ledger.
  Public documentation must not depend on private context stores.
- Run `node --test .\tools\Docs\test-docs.mjs` when changing the pipeline, then
  `.\scripts\Build-Handbook.ps1 -AcceptReviewedSource` after source review and
  `.\scripts\Build-Handbook.ps1 -Check`. Final documentation delivery includes HTML and PDF.
- Review changed rendered pages and illustrations. Hash/range checks cannot establish semantic accuracy.
  Keep new/changed `.ps1` scripts byte-identical to their `.txt` twins.
- Follow `TODO\WORKFLOWS\06_LIVING_DOCUMENTATION.todo`; this obligation continues after workstream38.
- Keep the root README image-first and concise. Put setup/explanations in docs\OVERVIEW.md and
  focused guides; link them through docs\README.md. Preserve brief local-Ask/capture-limit wording.
  Refresh theme-safe README image derivatives with scripts\Export-ReadmeImages.ps1 after UI image changes.

## Local question boundaries

- The current Ask action freezes a local snapshot only; no provider or outbound request is connected.
- Preserve GUID-stamped capture callbacks, structured source identity and exact displayed-text snapshots.
  Never parse display labels or automatically add unselected segments/notes/chat context.
- Question typing has an independent focus transition; do not route it through global Notes Edit,
  which stops audio. Both remain protected by the same window policy, with deliberate typing focus.
- Accurate armed-Mic spans require29commit. SourceRefs are local provenance; a future Azure request
  must use ExactText, not serialize the entire snapshot and its original selected text.
