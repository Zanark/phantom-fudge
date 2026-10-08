# Proposed architecture

**Architecture target plus implemented preview.** Windows 11 WPF/.NET 10 implementation began
October 8. See IMPLEMENTATION-STATUS.md for actual code/evidence versus unfinished release goals.
Source IDs below resolve in [FEASIBILITY.md](FEASIBILITY.md).

The15:05 request adds a **planned, not implemented** explicit question-to-Azure-answer extension.
Read [AI-ANSWER-ARCHITECTURE.md](AI-ANSWER-ARCHITECTURE.md) for the complete researched design.
Current local-only ASR/runtime behavior remains unchanged. The proposal sends only selected frozen
text on Ask, keeps audio local by default, streams to the same HWND and leaves Docker optional/last.
The source registry for this extension is AI-ANSWER-RESEARCH.md, not the older capture-feasibility IDs.

## Why this shape

Use an ordinary, process-owned, top-level HWND with quiet tool-window behavior. Windows
capture affinity works on that HWND [S01]; avoiding an HWND does not produce a magic
uncapturable desktop surface. A WPF window with `WindowChrome` can retain native
move/resize regions without a conventional frame [S08].

Prefer one window and one note. Keep settings, search, errors, and status inside its visual
tree. This minimizes additional native surfaces that require separate exclusion/lifecycle
handling. Native and framework-created auxiliary windows still need inventory and testing.

October8 03:25 correction: required transcription uses an authenticated native Python speech service.
C# owns the GUI/WASAPI capture and streams PCM to literal loopback; Python owns recognition.
Docker is an optional final-stage package of that service, never a native operation dependency.
No cloud service, browser renderer, remote content or telemetry is needed. Single-instance recovery
remains narrowly bounded per-user IPC. See TRANSCRIPTION.md.

October8 14:01 adds explicitly selected microphone dictation alongside Computer output. The client
sends independent source-tagged PCM tracks in one v2 WebSocket; Python owns a decoder per source.
No mixing, chat scraping, speaker identification, automatic input fallback or cloud ASR is introduced.
Source labels/partial state are preserved separately; see DUAL-SOURCE-AUDIO.md for the versioned contract.

## Components and responsibilities

| Proposed component | Owns | Must not do |
| --- | --- | --- |
| `AppHost` | Startup, shutdown, lifecycle notifications, per-user instance ownership | Show persisted text before window initialization |
| `OverlayWindow` / view model | Plain-text editor, grip, resize, inline panels, mode controls | Create arbitrary note-bearing popups |
| `WindowSurfaceRegistry` | Owned HWND generations and sensitive-surface inventory | Assume parent affinity automatically covers every popup |
| `CapturePolicy` | Capability checks, setter, diagnostics, invalidation and reveal permission | Claim capture pixels were independently verified |
| `NativeWindowAdapter` | Typed Win32 calls, style changes, last-error capture, event hooks | Inject code into another process or clear errors silently |
| `PresentationController` | Edit/Present/Hidden states and focus policy, without acknowledgment prompts | Infer that a meeting started/stopped from window names |
| `HotkeyService` | Registration, conflicts, unregister/re-register and recovery commands | Use a global keystroke hook or record typed keys |
| `NoteDocument` | Text, dirty state, undo boundaries, encoding/newline metadata | Own platform capture policy |
| `NoteStore` | Explicit file I/O and approved opt-in recovery | Autosave to arbitrary source files or discard a failed save |
| `PlacementStore` | Position/size in logical units plus monitor context, safe bounds | Restore an unreachable grip after monitor changes |
| `Diagnostics` | Bounded redacted event codes and environment facts | Log text, clipboard, paths, note hashes, or dumps by default |
| Python `manage.py` | Native service startup/reuse/status/idle shutdown with local receipt and authenticated instance identity | Require Docker, kill unrelated processes or interrupt active transcription |
| Python recognizer adapter | Convert PCM into partial/final events independently of GUI/deployment | Infer that programming language alone improves accuracy |

Keep domain logic independently testable without an interactive desktop. Use small interfaces
for native and filesystem failure injection, not a generic plugin architecture.

## Proposed repository layout

```text
src\
  PhantomFudge.App\                 WPF entry point, views, native adapters
  PhantomFudge.Core\                document/state/placement/persistence policies
tests\
  PhantomFudge.Core.Tests\          deterministic unit and file-failure tests
  PhantomFudge.Windows.Tests\       interactive HWND/focus/lifecycle tests
spikes\
  CaptureProof\                    isolated synthetic-text proof, before app polish
tools\
  CaptureHarness\                  only if needed for repeatable public-API capture
docs\
TODO\
```

The preview now implements App/Core, xUnit Core tests, an executable audio-protocol harness,
and CaptureProof. Python service/native lifecycle live under services/transcription; optional final-stage
Docker packaging is a separate recipe plus root Compose file. Not every target component above is a class.
Prefer a minimal project count and no application framework dependency without need.

## Window construction and reveal contract

1. Start with no visible sensitive content. Do not use an auto-shown `StartupUri` window
   followed by a late `Loaded` protection call.
2. Construct the view with `ShowInTaskbar=false`, `AllowsTransparency=true`, a generic
   title, transparent Window background and an alpha-tinted root Border with custom chrome.
   This supersedes the initial opaque baseline after the owner's translucency request.
   `WindowStyle=None` alone loses native
   frame behaviors; use `WindowChrome` deliberately [S08, S09].
3. Create the HWND without showing via `WindowInteropHelper.EnsureHandle()` [S07].
   Attach `SourceInitialized`/message hooks before creation; make the initialization
   routine idempotent so one handle generation is not confused with another.
4. Preserve unrelated native style bits, add `WS_EX_TOOLWINDOW`, and ensure
   `WS_EX_APPWINDOW` is not forcing a taskbar entry [S03]. Apply style changes correctly.
   Reinspect actual styles rather than assuming managed properties reflect P/Invoke changes.
5. Confirm the supported OS/session and DWM composition, then call
   `SetWindowDisplayAffinity(hwnd, 0x11)` for the owned top-level HWND [S01].
   Capture the native error immediately on failure; do not silently retry with WDA_NONE
   or WDA_MONITOR as equivalent success.
6. Attempt getter readback for diagnostics [S02]. Record value/error and handle type.
   Its documented layered-window restriction means failed readback on an opaque window
   is not a universal reason to add transparency or declare setter failure. A successful
   contradictory readback invalidates reveal; an unavailable getter follows the behavior
   explicitly resolved by the spike. If that behavior is unresolved, do not promote the design.
7. Establish at least one working recovery path before allowing hide/no-taskbar operation.
8. Only after the current HWND generation is configured, bind note content and show.
   Async file completion must re-check the generation and reveal permission.

The spike must record actual startup frames, hidden-handle behavior, auxiliary HWNDs,
setter/getter results, and focus behavior for this exact WPF configuration. A plausible
sequence in this document is not proof it works.

Electron's Windows wrapper documentation notes that changes take effect on a subsequent
desktop composition [S20]. Do not interpret a successful setter or getter as a synchronous
capture-frame barrier. Resolve first-show/composition ordering experimentally before promotion;
an arbitrary sleep is not acceptable evidence of protection.

## Two independent state machines

Capture setup:

```text
UNINITIALIZED -> PREPARING_HIDDEN -> AFFINITY_CONFIGURED
                         |                 |
                         v                 v
                   FAULT_HIDDEN <---- INVALIDATED

New HWND generation always returns to PREPARING_HIDDEN.
AFFINITY_CONFIGURED means the setup succeeded, not "all sharing is safe".
```

Interaction:

```text
HIDDEN -> EDIT -> PRESENT -> HIDDEN
           ^         |
           +---------+

Any detected setup failure -> FAULT_HIDDEN, regardless of interaction mode.
```

Keep engineering qualification evidence separate from runtime UI. On October 8 the owner
stated that permission and recipient rehearsal are always handled externally and explicitly
removed both acknowledgments. Present/Start must not ask for them again, including after
hide, display changes, or restart. The app cannot observe the recipient or certify capture
behavior; retain an accurate status, not a misleading green "invisible" badge.

On app-controlled style/handle changes, hide first, invalidate, reapply, then reveal.
On display/session/resume notifications, hide promptly and require technical reinitialization,
without resetting or adding human-acknowledgment gates. Notifications are not instantaneous defenses against all external
changes; already recorded frames cannot be revoked.

## Interaction and shell behavior

The default is now transcript-first Present mode, non-activating, with Notes collapsed.
The same controls can switch to Edit for real keyboard focus. Notes visibility and theme changes
only update the existing visual tree/resources; they preserve data and do not restart capture.
Window/child `Cursor=Arrow` with `ForceCursor`, plus owned-window native cursor messages, avoids
the I-beam without touching global cursor visibility settings. This does not promise remote cursor
inclusion. Generic protection errors are written locally rather than shown in unprotected popups.
Semantic theme mappings live in ThemePalette/ThemeBrushes; canonical provenance and limitations
are documented in CURSOR-AND-THEMES.md.

In Edit mode, preserve keyboard focus, standard text editing, keyboard traversal, and
accessibility. Use a separate drag grip, not the text area, as the move region.
Provide keyboard move/reset alternatives to pointer-only dragging.

Present mode is read-only, optionally topmost, and non-activating by design. Test
`WS_EX_NOACTIVATE` and appropriate native show/position flags rather than applying that
style permanently: it explicitly interferes with activation and accessibility navigation
[S03]. Dragging and scrolling without stealing slide focus are spike/acceptance questions,
not assumed WPF behavior.

Use `SetWindowPos`/equivalent for topmost changes and preserve `SWP_NOACTIVATE` where
appropriate [S10]. A topmost ordinary window is not above the secure desktop and may not
behave as expected over exclusive fullscreen content. Do not fight foreground restrictions
with hooks or repeated focus-stealing calls.

No click-through in v1. `WS_EX_TRANSPARENT` is a painting-order style, not a universal
cross-process click-through contract [S03]. Transparent rendering is not capture protection.

BackgroundOpacity changes only the root Border brush alpha from26to255 (10-100%), default140
(55%). Window/text opacity remains1; the editor/transcript fills are transparent so there is
only one tint layer. No HWND/style change is needed when moving the slider. Header and setup/
confirmation controls retain opaque fills; minimum alpha avoids all-transparent mouse holes.
Local layered-window getter/capture/hit-test evidence is in IMPLEMENTATION-STATUS.md.

No taskbar/Alt-Tab entry does not mean no process, no Task View presence, no tray icon if
one is explicitly enabled, or no capture-picker entry. Measure those separately.

## Recovery and hotkeys

Use `RegisterHotKey`/`UnregisterHotKey` and handle `WM_HOTKEY` [S04].
Hotkeys are configurable; reserve neither F12 nor Windows-key combinations.
Registration can fail because of conflicts: show an inline generic conflict message,
retain an available alternative, and never leave the user with an unreachable window.

A second launch sends only authenticated same-user commands such as Reveal/ResetPosition
to the first process through per-user IPC. No arbitrary file path, executable, script,
or text payload is accepted. Handle stale instance ownership and IPC timeout explicitly.
Reset placement to an attached monitor before showing and recheck capture setup.

Default proposal is no tray icon. A user-opted tray recovery option is a separately visible
shell presence, not stealth. Explorer restart must not strand users relying on that option.
Normal user privileges are sufficient for the design; no service or elevation requirement.

## Secondary surfaces and text escape paths

Inventory WPF popups, tooltips, context menus, combobox dropdowns, system menus, owned dialogs,
IME/composition candidate windows, spelling/suggestion panels, and framework helper HWNDs.
Do not infer ownership or protection from being visually near the main window.

Use inline find/settings/error/unsaved-change panels. Disable app-generated note previews
and external file dialogs in Present mode. In Edit mode, explain that native file dialogs
and system input UI may be captured; do not pass note content to titles or errors.
If a required auxiliary surface cannot be protected, redesign it or mark the affected
interaction unsupported; do not cover it with an unprotected overlay.

Normal accessibility remains available in Edit mode. There is no promise against UI
Automation, clipboard history, process inspection, or spoken-note audio.

## Document and local storage

Implement a standard multiline plain-text editor with selection, copy/cut/paste,
undo/redo, wrapping, find, keyboard navigation, and adjustable font size. Preserve text
exactly across non-edit mode/font/layout changes, including Unicode and newline choices.
Oversized or unsupported-encoding files require an explicit error/conversion choice,
never silent truncation or replacement characters disguised as a successful open.

The preview implements explicit user `.txt` saves with NO automatic recovery. Opt-in application
recovery below remains unimplemented rather than silently saving private text. Proposed recovery
location is `%LOCALAPPDATA%\PhantomFudge`, with generic filenames and current-user access.
No directory under the repo, shared drive, or sync folder is chosen automatically.
Decide plaintext-versus-encrypted recovery explicitly; do not market plaintext autosave as
an encrypted private vault.

Use same-directory temporary write/flush and atomic replace where supported, retain the
last good file on error, serialize writes, and detect externally changed source files.
Surface access denied, disk full, file locks, interrupted writes, invalid settings,
corrupt recovery, and migration failures. Preserve dirty state until persistence actually
succeeds. Never overwrite an unparseable recovery file with a fresh empty document.

Specify recovery retention/clear behavior and backup limits before shipping. Ordinary deletion
does not prove secure erasure on SSDs, snapshots, backup systems, or crash dumps.

## DPI, placement, and accessibility

Use a per-monitor-aware configuration and verify current WPF/framework behavior on actual
mixed-DPI monitors [S05]. Keep logical units separate from native physical pixels.
Clamp saved placement against current monitor work areas, including negative coordinates,
rotated monitors, changed primary display, and disconnected projectors.

Keep a usable drag grip/control region visible. Test 200% text/display scale, high contrast,
light/dark readability, keyboard-only navigation, Narrator in Edit mode, and input composition.
Present-mode nonactivation limitations must be explained rather than hidden.

## Test and release boundaries

Unit tests prove state, persistence, parsing, and placement rules. Interactive desktop tests
prove window creation/styles/focus/failure handling. External recipient recordings prove only
the observed sharing configuration. Headless hosted CI cannot certify capture behavior.

Package the exact candidate first, then run final qualification against its hash. Maintain
redacted, bounded diagnostics, reproducible dependency locks, versioned data migrations,
runtime patching ownership, and no automatic updates during presentations.

If the supported WPF path fails, compare a minimal native Win32 control-window spike
before changing frameworks. A framework switch cannot cure an unsupported recorder path.
