# Phantom Fudge

**Local transcription. Speaker notes. Questions under your control.**

[Windows download](https://github.com/Zanark/phantom-fudge/releases/tag/v0.2.0-preview.1) · [Install / update / remove](docs/INSTALLATION.md) · [Workflow](docs/CURRENT-WORKFLOW.md) · [Docs](docs/README.md) · [Roadmap](TODO/00_INDEX.todo)

> **Unsigned Windows 11 x64 preview.** Repository access required. [Evidence and limits](docs/IMPLEMENTATION-STATUS.md). Capture exclusion is not universal. Answers are snapshot-only until an external provider is configured.

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

**5. Ask and read** · **6. Cover the workspace**

<p align="center">
  <a href="docs/assets/readme/question.png"><img src="docs/assets/readme/question.png" width="47%" alt="Large accent Ask button, red-bordered Clear button and a synthetic streamed answer"></a>
  <a href="docs/assets/readme/cover.png"><img src="docs/assets/readme/cover.png" width="47%" alt="NOTEPAD IS READY with the icon-only mask toggle at the top left"></a>
</p>

**Cover keeps transcription and answers running.** It does not make the app universally invisible.

[Provider setup](docs/ANSWER-PROVIDERS.md) · [Cover and privacy limits](docs/COVER-AND-PRIVACY.md)

[Keyboard controls and answer-size slider](docs/KEYBOARD-CONTROLS.md)

*Use the versioned Setup installer to install, repair or update; remove it through Windows Settings. [Source workflow](docs/PREVIEW-GUIDE.md).*

## Three looks. One workspace.

<p align="center">
  <a href="docs/assets/readme/deepseafoam.png"><img src="docs/assets/readme/deepseafoam.png" width="31%" alt="DeepSeaFoam dark theme"></a>
  <a href="docs/assets/readme/harbor-daylight.png"><img src="docs/assets/readme/harbor-daylight.png" width="31%" alt="Harbor Daylight light theme"></a>
  <a href="docs/assets/readme/plain.png"><img src="docs/assets/readme/plain.png" width="31%" alt="Plain B/W neutral theme"></a>
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
