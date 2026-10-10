# Phantom Fudge Engineering Handbook

An illustrated, source-grounded engineering book about building a local-first Windows conversation
companion. Read the [HTML edition](Phantom-Fudge-Engineering-Handbook.html), the
[PDF edition](Phantom-Fudge-Engineering-Handbook.pdf), or the editable chapters below.

The HTML edition works offline with the adjacent `assets` directory. The PDF is self-contained.
Neither is hosted or uploaded by the build. [manifest.json](manifest.json) records the generated
edition's counts, source hashes, citations and artifact hashes; it is the authority for edition size.

The prerelease attachments are the immutable tagged-build editions. This maintained repository
edition also records publication follow-up; it does not silently replace those release assets.
The newer Previous answer toggle and `context on/off` commands are **local source only,
not included in v0.2.0-preview.1**. Start with
[follow-up usage](../LOCAL-QUESTIONS.md#follow-up-with-the-previous-answer).

## Reading order

For the Windows 11 x64 prerelease, begin with [Install, update and remove](../INSTALLATION.md).
It is registered near the front of the book. The repository and release downloads are public;
see the versioned release for downloads and [current evidence](../IMPLEMENTATION-STATUS.md)
for qualification boundaries. The maintained source atlas includes the native launcher, installer
recipe, pinned speech-runtime builder and website tooling; its manifest records the current counts.

For the complete current experience with the new images, start with the
[illustrated workflow](../CURRENT-WORKFLOW.md). It appears near the front of both generated editions.
The new [keyboard controls and answer-size guide](../KEYBOARD-CONTROLS.md) is registered immediately
after it. Both generated editions include modal selection/editing, deterministic commands,
device navigation, session answer size and their bounded synthetic evidence.

| Part | Chapters |
| --- | --- |
| Orientation | [00 Reading map](00-reading-map.md), [01 Principal engineer](01-principal-engineer-guide.md), [02 Zero to hero](02-zero-to-hero.md), [03 Product decisions](03-product-boundaries-and-decisions.md) |
| Windows and experience | [04 Capture and cursors](04-windows-capture-and-cursors.md), [05 UI and themes](05-desktop-ui-and-themes.md), [06 Notes and storage](06-notes-transcripts-and-storage.md) |
| Streaming speech | [07 Dual capture](07-dual-input-capture.md), [08 Protocol](08-streaming-protocol.md), [09 Python recognition](09-python-recognition.md), [10 Native service](10-native-service-lifecycle.md), [11 State machines](11-state-machines-and-failure-containment.md) |
| Performance and planned AI | [12 Benchmarks](12-performance-and-benchmarks.md), [13 Azure answer design](13-proposed-azure-answer-flow.md), [14 Latency, identity, privacy](14-answer-latency-identity-and-privacy.md) |
| Engineering practice | [15 Development](15-development-and-testing.md), [16 Distribution](16-distribution-and-troubleshooting.md), [17 Assurance](17-privacy-and-assurance.md), [18 Extensions](18-extending-the-system.md) |
| Windows installation | [Native launcher, update/uninstall guards and retained data](../INSTALLATION.md) |
| Static website and tutorial | [Local preview, media provenance and pending Pages publication](../WEBSITE.md) |
| Reference and journey | [19 API/configuration](19-api-configuration-reference.md), [20 Glossary/map](20-glossary-and-repository-map.md), [21 Evidence/roadmap](21-evidence-and-roadmap.md), [Development journal](../../JOURNAL.md) |
| Installed answer providers | [Agency, offline local models, setup and retention](../ANSWER-PROVIDERS.md) |
| Optional question assistance | [Clarify first, explicit corrections and confirmation](../QUESTION-CLARIFICATION.md) |
| Cover and privacy limits | [Visual cover, supported Windows controls and feasible protection boundaries](../COVER-AND-PRIVACY.md) |
| Source walkthroughs | [22 Desktop](22-desktop-source-walkthrough.md), [23 Domain](23-domain-source-walkthrough.md), [24 Providers](24-provider-source-walkthrough.md), [25 Native audio](25-native-audio-source-walkthrough.md), [26 Speech](26-speech-source-walkthrough.md), [27 Operations](27-operations-source-walkthrough.md), [28 Documentation](28-documentation-source-walkthrough.md) |
| Operational updates | [October 9 quality evidence](../QUALITY-UPDATE.md), [local diagnostics and export](../DIAGNOSTICS.md) |

Each chapter contains diagrams and exact public source references.
The [annotated source atlas](../source-atlas/index.html) adds complete line-numbered first-party
source, authored explanations of contiguous blocks, focused excerpts and a file/line coverage ledger.
Its [separate PDF](../source-atlas/Phantom-Fudge-Source-Atlas.pdf) prints the explanations and selected
snippets rather than duplicating every raw source line. Mechanical coverage is not semantic proof.

The onboarding path starts from first principles; the principal engineer path starts from invariants
and failure boundaries.
The future Azure answer chapters are deliberately marked **planned**, not disguised as working code.

## Illustrations and provenance

`assets/diagrams` contains generated SVGs, their editable Mermaid sources and rendering receipts.
`assets/screenshots` contains actual WPF visual-tree images rendered with synthetic notes,
transcripts and device labels. Its [manifest](assets/screenshots/manifest.json) records image and UI
source hashes. These are not desktop captures or evidence that a meeting recorder excludes the app.
The cover is original schematic artwork.

Screenshots are never captured from the owner's live notes, chats, microphone, meeting or desktop.
The documentation fixture runs separately and does not start audio capture or real provider preparation.

## Keep this book alive

Follow [the maintenance contract](../DOCUMENTATION-MAINTENANCE.md) for every relevant change.
Canonical order lives in [book.json](book.json); [catalogue.json](catalogue.json) is the structured
research/writing map. Markdown is the editable source; never patch generated HTML/PDF/SVG by hand.
See [tools/Docs](../../tools/Docs/README.md) for rebuilding and freshness checks.
