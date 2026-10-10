# Phantom Fudge

**Local transcription. Speaker notes. Questions under your control.**

[Windows download](https://github.com/Zanark/phantom-fudge/releases/tag/v0.2.0-preview.1) · [Website & video tutorial](docs/WEBSITE.md) · [Install / update / remove](docs/INSTALLATION.md) · [Workflow](docs/CURRENT-WORKFLOW.md) · [Docs](docs/README.md) · [Roadmap](TODO/00_INDEX.todo)

> **v0.2.0-preview.1 is published: unsigned Windows 11 x64 preview.** [Evidence and limits](docs/IMPLEMENTATION-STATUS.md). Capture exclusion is not universal. Answers are snapshot-only until an external provider is configured.

**New in current source, not that release:** opt-in [previous-answer context](docs/LOCAL-QUESTIONS.md#follow-up-with-the-previous-answer).

## See it in action

**Select transcript lines. Explicitly Ask. Read the answer.**

[![Zoomed sample-data excerpts: select useful lines, later Ask, then read the sample answer](site/assets/demo-selection.gif)](site/assets/demo-selection.gif)

**Follow up with context** · **Cover and reveal**

<p align="center">
  <a href="site/assets/demo-context.gif"><img src="site/assets/demo-context.gif" width="48%" alt="Sample: Previous answer changes from OFF to ON without sending; current source only"></a>
  <a href="site/assets/demo-cover.gif"><img src="site/assets/demo-cover.gif" width="48%" alt="Sample: click the book icon to cover the workspace, then later reveal it"></a>
</p>

**Choose audio inputs** · **Start and stop transcription**

<p align="center">
  <a href="site/assets/demo-inputs.gif"><img src="site/assets/demo-inputs.gif" width="48%" alt="Sample: select Microphone, then Both, without starting capture"></a>
  <a href="site/assets/demo-transcription.gif"><img src="site/assets/demo-transcription.gif" width="48%" alt="Sample: Start transcription, then Stop at a later moment"></a>
</p>

**Adjust answer size** · **Select with the keyboard**

<p align="center">
  <a href="site/assets/demo-answer-size.gif"><img src="site/assets/demo-answer-size.gif" width="48%" alt="Sample: increase the answer font from 10 to 17 with the size slider"></a>
  <a href="site/assets/demo-keyboard.gif"><img src="site/assets/demo-keyboard.gif" width="48%" alt="Sample: V enters Select mode; later Space selects a transcript row"></a>
</p>

*Edited, original-speed sample-data GIFs, not live meetings or latency measurements. Click to enlarge. [Clip provenance](site/assets/asset-provenance.json).*

**[Complete 38-minute tutorial and DeepSeaFoam website](docs/WEBSITE.md)** — 15 chapters, searchable transcript, speed and captions. Self-contained Pages build; no separate video release or host required. Publication remains pending.

## From meeting audio to an answer

**1. Choose device cards** · **2. Start transcription**

<p align="center">
  <a href="docs/assets/readme/audio-devices.png"><img src="docs/assets/readme/audio-devices.png" width="47%" alt="Selectable Computer and Microphone device cards, without scrolling lists"></a>
  <a href="docs/assets/readme/recording.png"><img src="docs/assets/readme/recording.png" width="47%" alt="Synthetic active-transcription view showing the red recording dot"></a>
</p>

[Computer / Mic / Both](docs/DUAL-SOURCE-AUDIO.md) · [Setup and controls](docs/PREVIEW-GUIDE.md)

**3. Select several transcript lines** · **4. Clarify first, if needed**

<p align="center">
  <a href="docs/assets/readme/selection.png"><img src="docs/assets/readme/selection.png" width="47%" alt="Selected Mic and Computer transcript rows combined into one editable question"></a>
  <a href="docs/assets/readme/clarification.png"><img src="docs/assets/readme/clarification.png" width="47%" alt="Optional Clarify first interpretation with explicit correction and Yes, answer controls"></a>
</p>

[Compose your question](docs/LOCAL-QUESTIONS.md) · [Confirm or correct its meaning](docs/QUESTION-CLARIFICATION.md)

**5. Ask, then optionally follow up** · **6. Cover the workspace**

<p align="center">
  <a href="docs/assets/readme/answer-context.png"><img src="docs/assets/readme/answer-context.png" width="47%" alt="Opt-in Previous answer toggle immediately left of Ask, with red-bordered Clear on the right"></a>
  <a href="docs/assets/readme/cover.png"><img src="docs/assets/readme/cover.png" width="47%" alt="NOTEPAD IS READY with the icon-only mask toggle at the top left"></a>
</p>

**Cover keeps transcription and answers running.** It does not make the app universally invisible.

[Provider setup](docs/ANSWER-PROVIDERS.md) · [Cover and privacy limits](docs/COVER-AND-PRIVACY.md)

[Keyboard controls and answer-size slider](docs/KEYBOARD-CONTROLS.md)

*Use the versioned Setup installer to install, repair or update; remove it through Windows Settings. [Source workflow](docs/PREVIEW-GUIDE.md).*

## Three looks. One workspace.

<p align="center">
  <a href="site/assets/demo-themes.gif"><img src="site/assets/demo-themes.gif" width="80%" alt="Sample: switch through Harbor Daylight, Plain B/W and DeepSeaFoam"></a>
</p>

**DeepSeaFoam** · **Harbor Daylight** · **Plain B/W**

[Themes and translucency](docs/CURSOR-AND-THEMES.md)

## Under the hood

[![One native window, separate Computer and Mic tracks, local recognition, and a planned Azure answer boundary](docs/handbook/assets/cover.svg)](docs/OVERVIEW.md)

| Use it | Understand it | Build it |
| --- | --- | --- |
| [Installation and recovery](docs/INSTALLATION.md) | [Product overview](docs/OVERVIEW.md) | [Architecture](docs/ARCHITECTURE.md) |
| [Local questions](docs/LOCAL-QUESTIONS.md) | [Capture boundaries](docs/handbook/17-privacy-and-assurance.md) | [Implementation status](docs/IMPLEMENTATION-STATUS.md) |
| [Speech service](services/transcription/README.md) | [HTML handbook](docs/handbook/Phantom-Fudge-Engineering-Handbook.html) · [PDF](docs/handbook/Phantom-Fudge-Engineering-Handbook.pdf) | [Development JOURNAL](JOURNAL.md) |

[Explore the annotated source](docs/source-atlas/index.html) · [Source-atlas PDF](docs/source-atlas/Phantom-Fudge-Source-Atlas.pdf)

*Separate synthetic WPF examples, composited for readability—not meeting captures. Click to enlarge. [Image provenance](docs/assets/readme/manifest.json).*
