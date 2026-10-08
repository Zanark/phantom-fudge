# Phantom Fudge documentation

**New here?** Browse the [visual front page](../README.md), read the [product overview](OVERVIEW.md),
or go straight to [setup](PREVIEW-GUIDE.md). The [Engineering Handbook](handbook/README.md) is the
deep, source-linked reference; the [development journal](../JOURNAL.md) tells the journey.

| Need | Read |
| --- | --- |
| Understand the product, stack and boundaries | [Product overview](OVERVIEW.md), [product contract](PRODUCT.md) |
| Read the illustrated book | [Offline HTML](handbook/Phantom-Fudge-Engineering-Handbook.html) or [printable PDF](handbook/Phantom-Fudge-Engineering-Handbook.pdf) |
| Understand the system quickly | [Principal engineer's guide](handbook/01-principal-engineer-guide.md) |
| Learn the prerequisites | [Zero to hero](handbook/02-zero-to-hero.md) |
| Run the current preview | [Preview guide](PREVIEW-GUIDE.md), [service operations](../services/transcription/README.md) |
| Separate implemented from planned | [Current evidence](IMPLEMENTATION-STATUS.md), [roadmap](handbook/21-evidence-and-roadmap.md) |
| Understand two audio sources | [Frozen v2 contract](DUAL-SOURCE-AUDIO.md), [audio chapter](handbook/07-dual-input-capture.md) |
| Explore appearance and input behavior | [Themes, translucency and cursors](CURSOR-AND-THEMES.md) |
| Compose a local question | [Selection, editing and exact snapshots](LOCAL-QUESTIONS.md); no model request is sent yet |
| Design the future Azure answer feature | [Architecture](AI-ANSWER-ARCHITECTURE.md), [research](AI-ANSWER-RESEARCH.md), [evaluation](AI-ANSWER-EVALUATION.md), [privacy](AI-ANSWER-PRIVACY.md) |
| Choose the next implementation task | [TODO index](../TODO/00_INDEX.todo), [delivery workflow](DEVELOPMENT-WORKFLOW.md) |
| Review feasibility and support claims | [Research](FEASIBILITY.md), [architecture](ARCHITECTURE.md), [qualification matrix](QUALIFICATION.md) |
| Compare speech engines | [Measured benchmark](SPEECH-BENCHMARK.md), [current transcription design](TRANSCRIPTION.md) |
| Follow project conventions | [TODO format](TODO-CONVENTIONS.md), [contributor contract](../AGENTS.md) |
| Change the docs or regenerate images | [Maintenance contract](DOCUMENTATION-MAINTENANCE.md), [book tooling](../tools/Docs/README.md) |

The app is a **local, unqualified Windows preview**, not a universally invisible application.
The current native Python service does not need Docker. Local question drafting is implemented; Azure sending is not.
Handbook UI images are actual WPF visual-tree renders with **synthetic content and device labels**,
not desktop screenshots and not evidence of screen-sharing exclusion.

## Which document is authoritative?

Source code establishes current behavior; dated receipts establish what was actually exercised.
The protocol document establishes the current transport contract. Product/design documents explain
intent and must label unimplemented portions. The TODO index owns work status. The journal records
why decisions changed without rewriting history. The handbook connects those sources for readers;
it does not replace them.
