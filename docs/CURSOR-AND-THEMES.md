# Transcript-first view, cursor behavior, and themes

## Current interaction model

The [illustrated workflow](CURRENT-WORKFLOW.md) shows the current cards, selection, clarification,
answer controls and cover with the maintained images.

The app starts in read-only, non-activating Present mode. The transcript occupies the main
reading area; Q/A is expanded and Notes is collapsed. Click Notes to expand/collapse without changing its text,
or use Edit / Ctrl+Alt+E to open editable notes deliberately. Audio setup can be opened
without activating the window; token typing and file dialogs still require Edit.

The default surface uses `ShowActivated=false`, `WS_EX_NOACTIVATE`, and `MA_NOACTIVATE`.
Buttons/selectors are not keyboard-focusable while Present is passive, but work with the pointer.
Global Edit or independent question editing restores keyboard access. The app never forwards clicks into another app.
Changing themes, opacity or notes visibility does not start/stop audio or recreate the HWND.
Changing Edit/Present mode still stops transcription, as before.
Opening Audio setup uses the reading area for cards rather than a scrolling list. Its page arrows
preserve selection and remain locked during capture lifecycle changes. Cover instead detaches the
workspace without stopping audio/answers, preserves the existing native interaction style, and
leaves one accessible Uncover action. Global inspection/cached data remain separate privacy limits.

**Direct transcript clicks deliberately take focus.** Click several Mic/Computer rows to toggle
whole segments without Ctrl and populate the question in displayed history order. This opens Q/A
and enters the same independent transition as **Type / edit**, not global Notes Edit: transcription
continues and nothing is sent. **Done typing** / Escape restores passive interaction. After an edit,
further direct selection changes retain the draft/highlights with inline status until **Clear
question**. The manual **Choose source → Mic / Computer / All → Use selected** route can remain passive.
This distinction is intentional, not evidence that all clicks preserve foreground focus.
(src/PhantomFudge.App/MainWindow.TranscriptSelection.cs:85-147;
[`SetQuestionEditing`](../src/PhantomFudge.App/MainWindow.Questions.cs).)

The October 9 requested window is 1240 x 1520, clamped to the current monitor work area
(1240 x 1176 on the observed 1920 x 1200 work area). Wide layouts use transcript and Q/A columns.
The main panels have no nested outer scrollbar; each long text area still scrolls. Primary actions
remain visible at the tested minimum 470 x 560. This is local fixture evidence, not owner acceptance.
Capture status is now a quiet requested-state footer with deliberate inline **Details**; actual
failures remain actionable. See [the correction record](QUALITY-UPDATE.md).

All app text controls declare an Arrow cursor, with inherited `ForceCursor=true`; the native
window's cursor messages use the standard Windows arrow. No custom/fake captured cursor,
global cursor replacement, cursor-visibility counter manipulation, recorder injection, or
system-wide mouse setting is used.

## Important unresolved distinction

The owner reported that the shared-view cursor disappears when clicking the old app and
changes to an I-beam over its text. The old default was activating Edit mode. Avoiding
unnecessary activation and fixing the app's cursor shape addresses those local causes/cues,
but the reported recipient-side disappearance has **not** been reproduced in its actual
sharing app/mode. It is not honest to label that remote symptom universally fixed.

Captured pointer behavior belongs partly to the recording application and capture API.
Windows Graphics Capture exposes `IsCursorCaptureEnabled` to the **capturer**, not a
captured app's permission to dictate another program's cursor stream. Display affinity
is expressly not a security or universal capture guarantee. Local native cursor messages,
foreground preservation and GDI window samples do not establish recipient cursor visibility.

Engineering qualification must separately record: sharing product/version, full-screen
versus window/tab source, cursor-capture option/backend, local cursor shape/visibility,
foreground HWND changes, and actual recipient/recording frames during hover/click/drag/type.
No repeated permission or rehearsal prompts were added to the application.

Automatic startup/protection failures no longer open an unprotected branded message box.
They withhold/hide the content, emit a generic error and save a bounded notice at
`%LOCALAPPDATA%\PhantomFudge\last-window-error.txt`. This contains no notes or transcript.
Startup failure exits nonzero. Explicit file dialogs in Edit remain outside the protected HWND.

## Theme controls

The current sizes are15for final transcription,13.5/10.5for normal/compact partials and10/9for
answers/interpretations. Question text stays18. Ask uses the palette accent/action-text colors,
while Clear immediately to its right keeps a two-unit theme-error red border. Device-card selection
uses an accent outline/radio dot; the active recording dot uses the same red error-role color.
These are independent of the background-opacity slider and do not fade reading glyphs.

DeepSeaFoam is the default. The sun/moon button alternates DeepSeaFoam and Harbor Daylight.
Plain B/W restores the neutral presentation; the themed button returns from Plain to
DeepSeaFoam. The current name stays visible. Theme and opacity choices are session-only.

Themes update semantic brushes for text, surfaces, borders, hover/pressed/selected states,
input fields, scrollbars, slider, progress meter and notices. Opaque controls keep their
reading contrast; the root tint and answer-reading tint share the Background slider. Question,
transcript and notes backgrounds do not add opaque content containers. Text stays solid.
At low opacity, contrast depends on whatever is behind the window: no universal accessibility
claim is made for transparent content.

Main transcript selections use a thin accent outline, not an opaque fill over the content.
White/black glyphs and shared background alpha remain unchanged; keyboard focus has its own
outline state. See `TranscriptRowTemplate` / `TranscriptRowStyle` in
[MainWindow.xaml](../src/PhantomFudge.App/MainWindow.xaml) and [the multi-click guide](LOCAL-QUESTIONS.md).

| Role | DeepSeaFoam | Harbor Daylight | Plain B/W |
| --- | --- | --- | --- |
| Background | `#000F13` | `#F3F2E9` | `#FAF9F6` |
| Surface | `#001E26` | `#E3ECE7` | `#F0F0EC` |
| General UI text | `#93A1A1` | `#355451` | `#202B30` |
| Transcript/question/answer/notes reading | `#FFFFFF` | `#000000` | `#000000` |
| Heading | `#EEE8D5` | `#173D3A` | `#202B30` |
| Accent | `#00A591` | `#006F63` | `#202B30` |
| Border | `#586E75` | `#6B857E` | `#879A99` |

Palette provenance: the owner's canonical DeepSeaFoam repository, read-only, revision
`e0c17e626e990ce34372317ef96a3d686c31f947`. DeepSeaFoam 0.7.0 derives from the credited
SpriteCanvas/Solarized lineage; Harbor Daylight is its adopted 2026-09-24 companion.
This is an original WPF mapping, not a claim that prior native app exports already existed.
No artwork, music, logos, or external fonts were copied.
The exact white/black reading role is the owner's October 9 readability override; it is not
misrepresented as an unchanged canonical palette swatch.

- [Dark canonical JSON](https://github.com/Zanark/DeepSeaFoam/blob/e0c17e626e990ce34372317ef96a3d686c31f947/palette/deepseafoam.json):
  SHA256 `EC2508A8ACE73F92CA365D5A221EAFE8B5039E760312B344E325E672B50CB376`.
- [Light canonical JSON](https://github.com/Zanark/DeepSeaFoam/blob/e0c17e626e990ce34372317ef96a3d686c31f947/palette/harbor-daylight.json):
  SHA256 `53D288BE60B3DE2BEA0D90F1BBF627C72368B79AF909E59B6CC1436DD6157763`.
- Harbor hover/selection use the documented opaque composites `#D7E1DC`/`#C7DDD7`;
  dark hover composites the border at20% over panel to `#122E36`. Dark selection uses
  the canonical terminal selection `#003748`, with warm readable selection text.
- [Solarized attribution](https://ethanschoonover.com/solarized/).

## Platform references

- [SetWindowDisplayAffinity limitations](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [SetCursor sets a shared cursor shape, not another recorder's stream](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursor)
- [WPF ForceCursor](https://learn.microsoft.com/en-us/dotnet/api/system.windows.frameworkelement.forcecursor?view=windowsdesktop-10.0)
- [Recorder-owned cursor inclusion](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.iscursorcaptureenabled?view=winrt-26100)
