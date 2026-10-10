# Phantom Fudge documentation

**New here?** Follow the [illustrated current workflow](CURRENT-WORKFLOW.md), browse the
[visual front page](../README.md), or read [installation](INSTALLATION.md). The [Engineering Handbook](handbook/README.md) is the
deep, source-linked reference; the [development journal](../JOURNAL.md) tells the journey.

| Need | Read |
| --- | --- |
| Watch the full tutorial or preview the ghost-themed website | [Website, video controls and Pages deployment](WEBSITE.md) |
| Install, update or remove the Windows11 x64 preview | [Installer guide](INSTALLATION.md) and [published asset identities](release-0.2.0-preview.1.json) |
| See all current features and images in one walkthrough | [Illustrated workflow, controls and state transitions](CURRENT-WORKFLOW.md) |
| Understand the product, stack and boundaries | [Product overview](OVERVIEW.md), [product contract](PRODUCT.md) |
| Read the illustrated book | [Offline HTML](handbook/Phantom-Fudge-Engineering-Handbook.html) or [printable PDF](handbook/Phantom-Fudge-Engineering-Handbook.pdf) |
| Understand important statements in every first-party file | [Searchable source atlas](source-atlas/index.html), [annotation PDF](source-atlas/Phantom-Fudge-Source-Atlas.pdf), [coverage ledger](source-atlas/coverage.json) |
| Understand the system quickly | [Principal engineer's guide](handbook/01-principal-engineer-guide.md) |
| Learn the prerequisites | [Zero to hero](handbook/02-zero-to-hero.md) |
| Run from source or an older development bundle | [Source/legacy preview guide](PREVIEW-GUIDE.md), [service operations](../services/transcription/README.md) |
| Review the October 9 corrections | [Workspace, recognition and answer results](QUALITY-UPDATE.md) |
| Investigate timing or failures locally | [Metadata logs, text opt-in and safe export](DIAGNOSTICS.md) |
| Separate implemented from planned | [Current evidence](IMPLEMENTATION-STATUS.md), [roadmap](handbook/21-evidence-and-roadmap.md) |
| Understand two audio sources | [Frozen v2 contract](DUAL-SOURCE-AUDIO.md), [audio chapter](handbook/07-dual-input-capture.md) |
| Explore appearance and input behavior | [Themes, translucency and cursors](CURSOR-AND-THEMES.md) |
| Work without moving the pointer; choose answer size | [Keyboard modes, commands, device navigation, shortcut JSON and Size slider](KEYBOARD-CONTROLS.md) |
| Cover the workspace without stopping it | [Notepad cover and privacy feasibility](COVER-AND-PRIVACY.md) |
| Compose and answer a question | [Drafts and snapshots](LOCAL-QUESTIONS.md), [Copilot/local model setup](ANSWER-PROVIDERS.md) |
| Follow up using the displayed answer | [Previous answer: explicit, bounded and default-off](LOCAL-QUESTIONS.md#follow-up-with-the-previous-answer); local source only, not v0.2.0-preview.1 |
| Repair the intent of a noisy question | [Optional interpretation and Yes/correction replies](QUESTION-CLARIFICATION.md) |
| Design the future Azure answer feature | [Architecture](AI-ANSWER-ARCHITECTURE.md), [research](AI-ANSWER-RESEARCH.md), [evaluation](AI-ANSWER-EVALUATION.md), [privacy](AI-ANSWER-PRIVACY.md) |
| Choose the next implementation task | [TODO index](../TODO/00_INDEX.todo), [delivery workflow](DEVELOPMENT-WORKFLOW.md) |
| Review feasibility and support claims | [Research](FEASIBILITY.md), [architecture](ARCHITECTURE.md), [qualification matrix](QUALIFICATION.md) |
| Compare speech engines | [Measured benchmark](SPEECH-BENCHMARK.md), [current transcription design](TRANSCRIPTION.md) |
| Follow project conventions | [TODO format](TODO-CONVENTIONS.md), [contributor contract](../AGENTS.md) |
| Change the docs or regenerate images | [Maintenance contract](DOCUMENTATION-MAINTENANCE.md), [book tooling](../tools/Docs/README.md) |

The app is a **local, unqualified Windows preview**, not a universally invisible application.
The repository is now public. This visibility change does not establish a qualified release.
The current native Python service does not need Docker. Providers are explicit: installed Copilot
can use cloud models or a configured local endpoint. Direct Azure remains future work.
Handbook UI images are actual WPF visual-tree renders with **synthetic content and device labels**,
not desktop screenshots and not evidence of screen-sharing exclusion.

## Which document is authoritative?

Source code establishes current behavior; dated receipts establish what was actually exercised.
The protocol document establishes the current transport contract. Product/design documents explain
intent and must label unimplemented portions. The TODO index owns work status. The journal records
why decisions changed without rewriting history. The handbook connects those sources for readers;
it does not replace them.
