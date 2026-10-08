# Capture qualification plan

**Product matrix: NOT_TESTED for all meeting/OBS qualification candidates.**
October8 local synthetic GDI/UI and output-transcription probes exist; see IMPLEMENTATION-STATUS.md.
Those probes do not populate PASS for any Teams/Zoom/Meet/OBS backend.
This is a test design, not a compatibility certification. Documentation research cannot
populate PASS results.

## Evidence model

Record four independent observations:

| Dimension | Required observation |
| --- | --- |
| API configuration | Setter outcome/error, HWND identity/generation, requested affinity, DWM state |
| Shell presence | Taskbar, Alt-Tab, Task View, tray, and native title behavior |
| Discovery | Whether pickers/enumeration list the window; distinguish listing from captured contents |
| Captured pixels | Remote participant frames and applicable recordings, including transitions |

`GetWindowDisplayAffinity` success/value/error is an additional diagnostic, not a substitute
for any row. Its documented layered-window restriction needs the spike described in
`FEASIBILITY.md`; a failed getter on an opaque window is not automatically a failed setter.

Outcome vocabulary: `NOT_TESTED`, `PASS`, `FAIL_CONTENT_LEAK`, `FAIL_ARTIFACT`,
`BLOCKED`, `UNSUPPORTED`. Version changes make previous PASS historical and the new row
NOT_TESTED. Never silently treat a missing licensed recording feature as PASS.

## Baseline matrix

All rows start NOT_TESTED. Split each selectable backend/optimization into its own record.
Never infer a backend from a product name or from an "Automatic" menu item.

| Row | Product/path | Scope | Required result/status |
| --- | --- | --- | --- |
| T01 | Teams desktop, whole screen | Each intended shared monitor | NOT_TESTED |
| T02 | Teams desktop, presentation window | Notes overlapping and outside that window | NOT_TESTED |
| T03 | Teams web in approved Edge/Chrome | Screen/window options actually offered | NOT_TESTED; extension beyond desktop baseline |
| Z01 | Zoom desktop, whole screen | Each exposed capture/optimization mode | NOT_TESTED |
| Z02 | Zoom desktop, presentation window | Overlap, minimize/restore, mode switching | NOT_TESTED |
| Z03 | Zoom, region/portion or optimized video | Only if offered in tested version | NOT_TESTED; report actual availability |
| G01 | Google Meet in Chrome, entire screen | Browser and selected monitor pinned | NOT_TESTED |
| G02 | Google Meet in Chrome, window | Presentation window overlapped by notes | NOT_TESTED |
| G03 | Google Meet in Chrome, tab | Tab including/excluding audio; overlay elsewhere | NOT_TESTED |
| G04 | Google Meet in Edge | Separate records for offered screen/window/tab modes | NOT_TESTED |
| O01 | OBS Display Capture | Automatic and every exposed backend separately | NOT_TESTED |
| O02 | OBS Window Capture | Automatic, BitBlt, and Windows Graphics Capture where exposed | NOT_TESTED |
| O03 | OBS direct selection of Phantom Fudge | Window list, preview, output recording | NOT_TESTED; absence, blank, or blocked selection recorded |
| O04 | OBS Game Capture / injected capture | Explicit adversarial-boundary investigation only | UNSUPPORTED for v1; no universal claim |
| S01 | Windows Snipping Tool / screenshots | Still and recording functions where available | NOT_TESTED; secondary regression suite |
| S02 | Test harness WGC | Synthetic display/window capture | NOT_TESTED; diagnostic, not product qualification |
| S03 | Test harness DXGI duplication / GDI / PrintWindow | Each API recorded separately if implemented | NOT_TESTED; diagnostic, not product qualification |
| X01 | RDP, VDI, remote-assistance sessions | Separate remote-desktop policy and rendering path | UNSUPPORTED for v1 unless separately approved |
| X02 | External camera, HDMI capture, privileged/in-process inspection | Outside OS capture-affinity assurance | UNSUPPORTED; expected privacy boundary |

Desktop Teams/Zoom, Meet Chrome/Edge, and OBS display/window configurations are the planned
required product families. G0/G1 must enumerate actual builds/modes and agree the finite
required set. Optional rows cannot compensate for a failed required row. A product family
cannot be labelled supported based only on a tab/window mode if screen sharing was not qualified.

A picker may list an excluded window. Record that honestly; no-listing is not a universal
release promise. For directly selected notes, no readable note content is required, but
blank/refused capture is acceptable if documented. For whole-desktop captures, a black rectangle,
shadow residue, or frozen region fails the intended "notes absent, underlying presentation
usable" experience even if text itself is unreadable.

## Environment dimensions

Qualify at least the primary x64 Windows 11 machine and one materially different supported
GPU/display configuration before a broad public claim. Otherwise label the result as a
single-machine preview. Record edition, release, build/UBR, OS support status, GPU/driver,
monitor count, resolution, scaling, HDR, refresh rate, remote/local session, runtime,
package architecture/hash, and app/browser version.

Cover single-monitor and dual-monitor layouts, negative monitor coordinates, 100/125/150/200%
scaling combinations, monitor hotplug/docking, primary-display changes, sleep/resume, lock/unlock,
and Windows virtual desktops. HDR/high-refresh and ARM64 remain explicitly unqualified if
the equipment is unavailable. Do not imply a Cartesian-product test was run from a few samples.

## Controlled recording procedure

1. Use a clean synthetic desktop and note: a unique large canary string, alternating colors,
   high-contrast border, and an underlying moving clock/pattern. No real presentation or notes.
2. Start an independent recipient or recorder first. Record the product, version, backend,
   settings, and selected source. Save a configuration screenshot free of personal data.
3. Establish a **positive control** in an isolated test build: a deliberately unprotected
   synthetic note appears in the same capture path. The production app must not expose a
   "disable protection" switch. If the path excludes overlaps by construction, use a separate
   target-selection/display control and label the limitation; do not call tab isolation an
   affinity test.
4. Turn protection on while the synthetic window is hidden, record native results, then show.
   Confirm locally visible notes and independently absent recipient text. A local screenshot
   that omits the notes cannot prove the presenter saw them; use tester attestation or an
   authorized synthetic-only camera view.
5. Record 60 seconds at steady state and at least ten repetitions of each applicable
   transition below, including startup while sharing is already active.
6. Inspect all recorded transition frames using an existing decoder/player or a subsequently
   approved harness. Track decoded frame count, gaps, rate, and start/end timestamps.
   Canary/OCR checks may assist, but visual inspection is required for clipped/faint glyphs,
   border/shadow artifacts, stale frames, and the underlying presentation.
7. Inspect a separate recipient's live view and meeting recording separately. Local preview
   is diagnostic only. Local/cloud recording availability depends on product/account/policy;
   unavailable evidence is BLOCKED or explicitly excluded, never fabricated.
8. Stop capture, preserve synthetic-only evidence with hashes and findings, and update the
   exact matrix row. File a defect for even one observed content-leak frame.

A passing finite capture cannot prove that an unsampled or future frame never leaks.
State this limit with every release result.

## Transition and leakage scenarios

| ID | Scenario | Pass condition |
| --- | --- | --- |
| C01 | Cold launch; warm relaunch; second instance | No pre-affinity note frame; no duplicate unprotected surface |
| C02 | Show/hide, minimize/restore, close/reopen | Protection established before note reveal every time |
| C03 | Drag/resize over shared content; select/scroll text | No text, border residue, or stale presentation region |
| C04 | Enter/leave Present; toggle topmost/theme/font | No handle replacement bypass or exposed intermediate surface |
| C05 | Restart sharing; change source; app/backend upgrade | New configuration requalified; no reuse of stale PASS |
| C06 | Monitor/DPI/HDR change, dock/undock | Notes hidden during unqualified transition; correct recovery position |
| C07 | Lock/unlock, suspend/resume, session change | No automatic unverified reveal; recover deliberately |
| C08 | Settings/find, context menu, tooltip, IME/suggestions | No note-bearing secondary HWND leaks; unsupported surfaces blocked in Present |
| C09 | Open/save, unsaved prompt, error dialog | Present mode refuses file dialogs; no note/path in generic errors |
| C10 | Clipboard history/paste; accessibility/screen reader | Boundaries documented; app does not leak through its own preview/notifications |
| C11 | API failure, DWM/capability failure, handle disposal | Notes remain hidden; actionable non-note error; no silent downgrade to WDA_NONE |
| C12 | Hotkey collision, Explorer restart, off-screen recovery | User can recover/exit without relying on a taskbar button or broken shortcut |
| C13 | Fullscreen slides, slideshow clicks, keyboard advance | Notes remain usable without accidental focus theft; unsupported exclusive mode named |
| C14 | Crash and restart with recovery data | Content restored only after protection; no WER/log note snippets intentionally emitted |
| C15 | Hover/click/select/drag text, controls and border | Local arrow/foreground observations AND recipient cursor shape/visibility recorded independently |
| C16 | Transcript-first, Notes expand/collapse, all three themes | No data loss/HWND replacement/unprotected surface; actual captured frames remain qualified |
| C17 | Computer/Mic/Both selection, denied mic, device loss and concurrent speech | No automatic capture/fallback; correct source labels, final drain and cleanup |
| C18 | OS microphone privacy indicator and speaker echo | Indicator visibility/echo are recorded as external limits, not hidden or claimed eliminated |
| C19 | Question selection, independent typing, local snapshot and unsaved exit | Same protected HWND, no unintended audio stop or extra context; repeat recipient/IME/clipboard boundaries |
| C20 | Provider setup, streamed answers, Cancel/Hide/clear/close and late callbacks | Explicit destination; no tools/org data or auto-login; partial/failed answers labelled; actual recorder behavior separately observed |

C15 is currently open for the owner's reported cursor disappearance. Record screen versus
window/tab source, cursor-capture setting/backend and app version. A visible local cursor or
MA_NOACTIVATE result does not prove the recorder includes that cursor. No fake cursor rendering
or recorder tampering is an acceptable substitute.

Crash dumps created outside app control and external IME/accessibility/clipboard surfaces
remain outside the capture-only guarantee. Do not claim secure erasure or total OS containment.

## Result record template

```text
Run ID / timestamp / timezone:
Tester:
App source revision or tree hash:
Packaged artifact SHA-256 / architecture / runtime:
OS edition / release / build / support status:
GPU / driver / monitors / DPI / HDR:
Sharing app / browser version / capture method / optimizations:
Source selected / recording type / recipient setup:
Window generation / style flags / setter result / native error:
Getter result / value / error / diagnostic interpretation:
Positive-control evidence and any control limitation:
Scenario IDs / repetitions / captured fps / frame counts / gaps:
Local visibility evidence:
Remote live-view evidence:
Recording evidence:
Picker / taskbar / Alt-Tab / Task View observations:
Outcome / defect IDs / uncertainty:
Evidence relative paths / hashes:
Reviewer and UX approval (separate):
```

Store real captures privately. Commit only deliberately synthetic, sanitized examples and
result summaries after review. A current support table must point to the exact release
artifact and evidence; it must never inherit PASS from a different build by default.
