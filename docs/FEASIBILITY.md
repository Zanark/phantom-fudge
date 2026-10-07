# Feasibility research: private speaker notes

Research baseline: **2026-10-07 UTC**. Sources below were retrieved during this planning
session; some work continued into October 8 in Asia/Kolkata.

**Verdict:** a quiet Windows 11 speaker-notes utility that requests exclusion from supported
capture paths is technically plausible and worth a small proof. A visible surface that can
never be detected or captured by any software is not a supportable promise.

**Evidence level:** primary-document research, framework implementation inspection, and
engineering synthesis. No application, benchmark, capture spike, meeting test, or qualified
release was produced. All product compatibility remains untested.

## 1. Break the requirement into separate capabilities

| Request | Feasibility | Important limit |
| --- | --- | --- |
| Plain text with normal movement/resizing | Standard desktop controls/windowing | A frameless design must preserve native input/resize/accessibility behavior |
| No taskbar or Alt-Tab entry | Documented tool-window behavior [S03] | Not process hiding, picker omission, or an accessibility boundary |
| Notes absent from supported capture | Windows `WDA_EXCLUDEFROMCAPTURE` [S01] | Only applicable capture paths; exact product/backend needs observation |
| Not an actual window/application | Not how this proposed desktop utility works | It has an HWND and a process, even with minimal shell presentation |
| Never discoverable or recordable | Not supportable | Window enumeration [S06], process inspection, photography and other paths remain |

The user explicitly selected Windows 11 first, qualifying Teams, Zoom, Google Meet, and
OBS separately after the limitation was explained. That is a platform decision, not a
claim that any of those products has passed.

## 2. The supported Windows mechanism

`SetWindowDisplayAffinity(HWND, DWORD)` is the relevant documented API [S01].
The HWND must be a **top-level window owned by the calling process** and DWM must be
composing the desktop.

| Setting | Value | Documented behavior |
| --- | --- | --- |
| `WDA_NONE` | `0x00000000` | No display-affinity restrictions |
| `WDA_MONITOR` | `0x00000001` | Window content displayed only on the monitor; elsewhere a contentless window |
| `WDA_EXCLUDEFROMCAPTURE` | `0x00000011` | Window excluded rather than merely blanked on supported capture paths |

The exclusion value was introduced in **Windows 10 version 2004**. Earlier Windows
versions treat it like WDA_MONITOR. That historical API floor is **not** the app's support
policy; the chosen v1 platform is supported Windows 11 configurations.

Microsoft explicitly describes protection through a **specific set of public OS features
and APIs**, not DRM or a security guarantee. A camera aimed at the monitor is an obvious
counterexample. An HDMI recorder of an output containing the notes, privileged inspection,
or an unsupported recorder path is outside a target application's general assurance.

Do not attach to a recorder or inject into a presentation process to achieve the effect.
The intended use is a normal app setting affinity on its own windows.

### Configuration, readback, and composition timing are different

The setter returns success/failure; failures need immediate native-error capture.
Success is not independent evidence of the pixels seen by an audience.

The current `GetWindowDisplayAffinity` documentation says its getter succeeds only for a
layered window with DWM composition [S02]. This is narrower than the setter's top-level-owned
HWND contract. Do not silently infer that every opaque WPF window has readable affinity.
Record getter success/value/error separately. Resolve actual opaque-window behavior in the
spike; **do not enable `AllowsTransparency` merely to satisfy the getter**.

Electron's current Windows wrapper documentation additionally notes that affinity changes
take effect on a subsequent desktop composition rather than synchronously at setter return
[S20]. This is wrapper documentation, not a newly inferred Win32 synchronization guarantee.
It nevertheless makes first-show/first-frame testing essential. Do not use a fixed sleep,
successful getter, or return code as proof of a completed capture transition.

Create/configure a hidden HWND before binding/showing private text, and test actual captured
startup frames. Any proposed composition synchronization must be validated on the native
path; no timing barrier has been demonstrated in this planning work.

## 3. Quiet windowing without pretending to be invisible

`WS_EX_TOOLWINDOW` is documented to omit a window from the taskbar and Alt-Tab dialog.
`WS_EX_APPWINDOW` forces a visible top-level window onto the taskbar, so it must not
contradict the intended tool-window setup [S03]. WPF's `ShowInTaskbar=false` expresses
the shell intent, but the actual HWND/helper-window behavior still needs inspection.

Use a generic title and a small drag grip. A separate caption region preserves text
selection. WPF `WindowChrome` supports invisible caption/resize areas while retaining
native behavior; setting `WindowStyle=None` alone removes those behaviors [S08].

Always-on-top is independent of focus and capture protection. `SetWindowPos` supplies
topmost and no-activation behavior [S10]. `WS_EX_NOACTIVATE` should not be permanent on an
editable notepad: its documented behavior interferes with ordinary activation and
accessibility navigation [S03]. Separate deliberate Edit and non-activating Present modes.

`WS_EX_TRANSPARENT` primarily describes painting order, not universal cross-process
click-through. Transparency, opacity, taskbar omission, and capture exclusion are different
features. Do not conflate them.

The result remains an ordinary app. `EnumWindows` enumerates desktop top-level windows
[S06]; taskbar omission is not a promise against capture-picker listing or programmatic
discovery. Standard text controls intentionally expose accessibility information [S17].

## 4. Recommended stack and alternatives

**Recommendation, not approval: C# WPF on current patched .NET 10 LTS**, with a standard
multiline `TextBox`, one opaque window, and a small typed Win32 adapter.

WPF has supported HWND interop, standard plain-text editing, and built-in accessibility
providers [S07, S17]. `EnsureHandle` explicitly separates native handle creation from
showing the window [S07], fitting a protect-before-reveal experiment.

| Candidate | Strengths for this app | Tradeoff / recommendation |
| --- | --- | --- |
| WPF / C# | Standard editor, UI Automation, HWND lifecycle access, managed code | Preferred Windows-only simplicity; measure resource use, do not assume benchmark numbers |
| Native C++ / Win32 | Direct HWND ownership, standard EDIT control, no .NET/browser dependency | Strong fallback/reference spike; more native lifetime/window-message work and CRT deployment choices [S19] |
| WinUI 3 | Modern XAML controls, HWND access, accessibility | Feasible; adds Windows App SDK deployment/servicing decisions with little obvious benefit to this tiny surface [S18] |
| Electron | Fast for a web-experienced team; content-protection wrapper and native handles | Browser/runtime/process/IPC servicing footprint; drag regions need editor exclusions; macOS protection explicitly weaker [S20] |
| Tauri 2 | Native window layer plus web UI; content-protection API | Platform webview/runtime/capability complexity; not stronger macOS protection; Linux method unsupported in inspected implementation [S21, S22] |

This is a maintainability assessment, not measured MB/startup/CPU superiority. No framework
can repair a capture backend that does not honor the relevant OS policy.

### Transparency is an unproven complication, not a proven universal bug

`AllowsTransparency` enables a distinct transparency path and constrains WPF window style
[S09]. An opaque baseline avoids unnecessary variables and fits the plain-notepad brief.
This research did **not** establish an authoritative current rule that transparent WPF
windows always fail affinity. If transparency becomes required, test it separately rather
than citing community anecdotes as a universal incompatibility.

## 5. Why cross-platform parity is not a v1 promise

### macOS

Electron documents that its macOS `setContentProtection` sets `NSWindow.sharingType` to
`NSWindowSharingNone`, and that newer ScreenCaptureKit applications can still capture the
window [S20].

Apple's current documentation calls `NSWindow.SharingType.none` a legacy constant and warns
against using it to hide/omit content from capture [S23]. ScreenCaptureKit's `SCContentFilter`
is a **recorder-side stream filter**, not a target app's universal veto over other recorders.

The inspected Tauri -> Wry -> Tao macOS implementation uses the same legacy sharing type
[S21]. That establishes mechanism similarity, not Windows-equivalent effectiveness.
The implementation reference was a moving development branch, not a pinned app dependency.

A macOS edition would need a different, honestly described contract such as recorder-side
cooperation or sharing another window/display. It cannot inherit Windows claims.

### Linux

No documented **portable universal application-controlled exclusion contract** was
established in the inspected Linux interfaces [S24]. This is a bounded finding, not proof
that every compositor or extension lacks any content-protection feature.

The ScreenCast portal describes capture sessions, source selection and PipeWire output.
X11 EWMH skip-taskbar/skip-pager states concern shell lists, not pixel exclusion. A useful
counterexample to blanket impossibility is Weston's compositor-specific content-protection
protocol, which discusses screenshot censorship/HDCP-oriented output protection for closed
systems. It is not general GNOME/KDE/X11/Wayland support.

Wayland window placement/always-on-top behavior can also vary; shared web UI does not
automatically provide the same draggable-overlay UX. Keep Linux outside v1.

## 6. Application compatibility: what the sources do and do not establish

OBS documents separate Display Capture and Window Capture sources, with a selectable
capture method and BitBlt-related behavior [S11, S12]. A single "works with OBS" result
would erase backend differences. Record every exposed relevant method; "Automatic" is not
an independently identified backend.

Teams documents screen versus window presentation [S13]. Meet documents entire-screen,
window, and Chrome-tab choices [S14]. A tab/window capture can omit unrelated windows by
selection rather than by affinity. Such a result does not demonstrate whole-screen exclusion.

The Zoom support page retrieved during research exposed its screen/desktop-sharing title
but not substantive mode/backend details [S15]. Exact Zoom options therefore remain a
runtime qualification input, not a verified documentation claim.

**No retrieved product documentation establishes that every Teams, Zoom, Meet, or OBS
path respects the flag.** No exhaustive assertion about all vendor documentation is made.
Use independent recipient views and applicable recordings, not local preview alone.
Capture-picker presence and blank previews are separate observations.

The full test plan, positive controls, first-frame scenarios, and result schema are in
[QUALIFICATION.md](QUALIFICATION.md). All product rows are untested.

## 7. Threat and failure boundaries that affect implementation

| Risk | Design response | Remaining limit |
| --- | --- | --- |
| First unprotected frame | Hidden HWND setup before content; recorded startup/frame review | No asynchronous composition barrier proven yet |
| New HWND loses policy | Generation-based lifecycle and centralized reveal gate | External recorder behavior remains unobservable |
| Tooltip/menu/IME leak | Inline app UI; secondary-surface inventory; Present restrictions | OS/third-party candidate/accessibility surfaces may be outside ownership |
| Fault misreported as protection | Explicit hidden fault state, generic error, no fallback to NONE/MONITOR | Cannot detect arbitrary external bypasses |
| Lost window without taskbar | Hotkeys plus second-launch/reset recovery | Hotkey conflicts and foreground restrictions need real tests [S04] |
| Monitor change/off-screen notes | DPI-aware placement and clamping [S05] | Actual mixed-DPI/projector hardware must be exercised |
| Text escapes via storage/audio | Local opt-in recovery, explicit saves, redacted logs and guidance | Affinity is not encryption, secure deletion, microphone control, or clipboard isolation |
| Upgrade changes capture behavior | Versioned exact-package evidence and requalification | Past results do not certify future builds |

Fail-closed here means **our application withholds/hides notes on detected setup failure**.
It does not mean the OS guarantees protection from all recorders. Do not claim that changing
a flag revokes already recorded frames.

## 8. Supported runtime and packaging direction

Microsoft lists .NET 10 as LTS through **2028-11-14**. .NET 8 support ends **2026-11-10**
[S16]. Choose the current patched .NET 10 SDK/runtime at implementation time; no exact patch
has been pinned or installed by this task.

OS support and .NET support must both remain valid. An API's historical minimum is not
permission to support every old Windows edition. WPF is Windows-only despite .NET's broader
platform availability [S16, S17].

Propose an architecture-specific self-contained x64 folder/ZIP for the pilot. It carries
the runtime, but the publisher must rebuild/redistribute runtime patches. A framework-dependent
alternative requires the **.NET Desktop Runtime**, not merely the base runtime [S25].
ARM64 is a separate package/qualification decision.

Single-file is optional and distinct from self-contained deployment; native-library extraction
or extra files can remain. Current WPF trimming support is disabled; do not base the plan on
aggressive trimming or unverified Native AOT assumptions [S25].

WinUI's current deployment docs support single-file scenarios for unpackaged self-contained
apps [S18]; rejecting it as categorically incapable would be stale. It is simply a less
compelling fit for this small Windows-only requirement.

Signing identifies a publisher but does not guarantee SmartScreen reputation or enterprise
acceptance; EV certificates no longer imply an instant bypass [S26]. Release/signing identity,
account permissions, cost, license, runtime servicing, and distribution destination must be
decided explicitly. Do not instruct users to disable OS protections to make installation work.

## 9. Decision and experiment register

| Item | Current disposition | Owner workstream |
| --- | --- | --- |
| Windows 11 + separate four-product qualification | User-approved scope | 01 |
| Opaque WPF / .NET 10 | Proposed, not approved or proven | 01, 03 |
| Getter behavior on chosen opaque HWND | Unresolved empirical question | 03 |
| Composition/first-frame ordering | Unresolved empirical question; no magic delay accepted | 03, 06 |
| All required modes exclude contents without unacceptable residue | Unknown | 03, 13 |
| No taskbar/Alt-Tab while recoverable and editable | Documented ingredients; integrated behavior untested | 03, 05, 09 |
| Present dragging/scrolling without slide focus theft | Untested interaction | 03, 10 |
| Native popup/IME/file-dialog leakage | Must inventory and constrain | 06, 07, 10, 13 |
| Runtime/support/package/signing choices | Proposed or unresolved | 01, 04, 14 |
| Cross-platform equivalent assurance | Not established; outside v1 | 18 |

The next engineering action is the gated **synthetic capture proof**, not app-wide feature
implementation. If required capture modes fail, narrow support with approval or use a
different presentation setup. Prefer an unshared display/device or sharing only the presentation
window/tab over speculative stealth techniques.

## 10. Source register

Primary references below were fetched directly in this session by the parent or the
framework-research agent. Multiple pages under an ID support one bounded subject.
Moving documentation and development branches must be rechecked when selecting dependencies.

- **[S01] Microsoft, SetWindowDisplayAffinity:** values, own top-level HWND, DWM, historical
  API floor and explicit non-security guarantee.
  <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity>
- **[S02] Microsoft, GetWindowDisplayAffinity:** getter behavior and documented layered-window
  restriction. <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdisplayaffinity>
- **[S03] Microsoft, Extended Window Styles:** TOOLWINDOW, APPWINDOW, NOACTIVATE, TOPMOST,
  TRANSPARENT. <https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles>
- **[S04] Microsoft, RegisterHotKey:** conflicts, WM_HOTKEY, reserved keys and cleanup.
  <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey>
- **[S05] Microsoft, High DPI desktop development:** per-monitor behavior and framework/coordinate
  caveats. Some historical framework discussion must be read in context, not as a claim about
  every modern WPF version.
  <https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows>
- **[S06] Microsoft, EnumWindows:** ordinary top-level desktop-window enumeration.
  <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows>
- **[S07] Microsoft, WindowInteropHelper.EnsureHandle:** create native HWND independently of Show,
  SourceInitialized behavior, and managed/native state caveats.
  <https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.windowinterophelper.ensurehandle?view=windowsdesktop-10.0>
- **[S08] Microsoft, WindowChrome:** custom visuals while retaining caption/resize functionality.
  <https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome?view=windowsdesktop-10.0>
- **[S09] Microsoft, AllowsTransparency:** transparency and WindowStyle constraints, not a
  universal affinity incompatibility claim.
  <https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-10.0>
- **[S10] Microsoft, SetWindowPos:** frame/style application, topmost and activation flags.
  <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos>
- **[S11] OBS, Window Capture sources:** source selection, capture methods and BitBlt.
  <https://obsproject.com/kb/window-capture-sources>
- **[S12] OBS, Display Capture sources:** monitor capture and source-specific behavior.
  <https://obsproject.com/kb/display-capture-sources>
- **[S13] Microsoft Teams, Present content:** screen/window choices and product constraints,
  not an all-backend affinity warranty.
  <https://support.microsoft.com/en-us/teams/meetings/present-content-in-microsoft-teams-meetings>
- **[S14] Google Meet, Present during a meeting:** screen/window/tab choices.
  <https://support.google.com/meet/answer/9308856?hl=en>
- **[S15] Zoom, Sharing your screen or desktop:** title-only retrieval; substantive details
  could not be established from the returned page.
  <https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0060596>
- **[S16] Microsoft, .NET support and Windows support:** LTS dates, Desktop Runtime, OS lifecycle.
  <https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>
  <https://learn.microsoft.com/en-us/dotnet/core/install/windows>
  <https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md>
- **[S17] Microsoft, WPF and standard editing/accessibility:**
  <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/>
  <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/textbox>
  <https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-providersoverview>
- **[S18] Microsoft, HWND interop, WinUI TextBox, deployment and servicing:**
  <https://learn.microsoft.com/en-us/windows/apps/develop/ui/retrieve-hwnd>
  <https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/text-box>
  <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview>
  <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels>
- **[S19] Microsoft, native window/EDIT controls and C++ distribution:**
  <https://learn.microsoft.com/en-us/windows/win32/learnwin32/creating-a-window>
  <https://learn.microsoft.com/en-us/windows/win32/controls/about-edit-controls>
  <https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method?view=msvc-170>
- **[S20] Electron, content protection and runtime/input model:** Windows wrapper, asynchronous
  desktop-composition note, ScreenCaptureKit warning, separate shell APIs and drag-region behavior.
  <https://www.electronjs.org/docs/latest/api/browser-window#winsetcontentprotectionenable-macos-windows>
  <https://www.electronjs.org/docs/latest/api/base-window>
  <https://www.electronjs.org/docs/latest/tutorial/process-model>
  <https://www.electronjs.org/docs/latest/tutorial/electron-timelines>
  <https://www.electronjs.org/docs/latest/tutorial/custom-window-interactions>
- **[S21] Tauri/Tao, content-protection API and inspected implementation:** moving dev branches,
  not a released/pinned dependency guarantee.
  <https://v2.tauri.app/reference/javascript/api/namespacewindow/#setcontentprotected>
  <https://github.com/tauri-apps/tauri/blob/dev/packages/api/src/window.ts>
  <https://raw.githubusercontent.com/tauri-apps/tauri/dev/crates/tauri-runtime-wry/src/lib.rs>
  <https://raw.githubusercontent.com/tauri-apps/tao/dev/src/platform_impl/macos/window.rs>
  <https://raw.githubusercontent.com/tauri-apps/tao/dev/src/window.rs>
- **[S22] Tauri, platform webviews and Windows distribution:**
  <https://v2.tauri.app/reference/webview-versions/>
  <https://v2.tauri.app/distribute/windows-installer/>
  <https://v2.tauri.app/distribute/>
- **[S23] Apple, sharing type and ScreenCaptureKit filter:** readable DocC payloads were fetched
  because the reference website is script-rendered.
  <https://developer.apple.com/documentation/appkit/nswindow/sharingtype-swift.enum/none>
  <https://developer.apple.com/tutorials/data/documentation/appkit/nswindow/sharingtype-swift.enum/none.json>
  <https://developer.apple.com/documentation/screencapturekit/sccontentfilter>
  <https://developer.apple.com/tutorials/data/documentation/screencapturekit/sccontentfilter.json>
- **[S24] Linux primary interfaces:** bounded portal/EWMH evidence plus specialized Weston
  counterexample; not an exhaustive compositor survey.
  <https://github.com/flatpak/xdg-desktop-portal/blob/main/data/org.freedesktop.portal.ScreenCast.xml>
  <https://specifications.freedesktop.org/wm/latest/ar01s05.html>
  <https://gitlab.freedesktop.org/wayland/weston/-/raw/main/protocol/weston-content-protection.xml>
- **[S25] Microsoft, publishing/single-file/trimming:** deployment modes, native dependencies,
  runtime servicing and WPF trimming limitation.
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/>
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview>
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities#wpf>
- **[S26] Microsoft, SmartScreen/distribution:** signing versus file/publisher reputation,
  EV changes and policy limits.
  <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation>
  <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/distribution-feature-status#smartscreen-reputation-ev-certificates-no-longer-grant-instant-bypass>

## Retrieval and inference limits

Public search calls timed out; the research used directly fetched official references and
upstream source instead. The Zoom page was title-only. The Apple forum/Electron issue linked
from documentation was not fully retrieved and is not evidence for additional claims.
No authoritative general WPF-transparency incompatibility report was established.
Framework size/performance, actual account access, precise OS/GPU behavior, and final capture
compatibility remain empirical work in the backlog.
