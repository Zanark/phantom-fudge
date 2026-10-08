# Phantom Fudge Engineering Handbook

An illustrated, source-grounded engineering book about building a local-first Windows conversation
companion. Read the [HTML edition](Phantom-Fudge-Engineering-Handbook.html), the
[PDF edition](Phantom-Fudge-Engineering-Handbook.pdf), or the editable chapters below.

The HTML edition works offline with the adjacent `assets` directory. The PDF is self-contained.
Neither is hosted or uploaded by the build. [manifest.json](manifest.json) records the generated
edition's counts, source hashes, citations and artifact hashes; it is the authority for edition size.

## Reading order

| Part | Chapters |
| --- | --- |
| Orientation | [00 Reading map](00-reading-map.md), [01 Principal engineer](01-principal-engineer-guide.md), [02 Zero to hero](02-zero-to-hero.md), [03 Product decisions](03-product-boundaries-and-decisions.md) |
| Windows and experience | [04 Capture and cursors](04-windows-capture-and-cursors.md), [05 UI and themes](05-desktop-ui-and-themes.md), [06 Notes and storage](06-notes-transcripts-and-storage.md) |
| Streaming speech | [07 Dual capture](07-dual-input-capture.md), [08 Protocol](08-streaming-protocol.md), [09 Python recognition](09-python-recognition.md), [10 Native service](10-native-service-lifecycle.md), [11 State machines](11-state-machines-and-failure-containment.md) |
| Performance and planned AI | [12 Benchmarks](12-performance-and-benchmarks.md), [13 Azure answer design](13-proposed-azure-answer-flow.md), [14 Latency, identity, privacy](14-answer-latency-identity-and-privacy.md) |
| Engineering practice | [15 Development](15-development-and-testing.md), [16 Distribution](16-distribution-and-troubleshooting.md), [17 Assurance](17-privacy-and-assurance.md), [18 Extensions](18-extending-the-system.md) |
| Reference and journey | [19 API/configuration](19-api-configuration-reference.md), [20 Glossary/map](20-glossary-and-repository-map.md), [21 Evidence/roadmap](21-evidence-and-roadmap.md), [Development journal](../../JOURNAL.md) |
| Installed answer providers | [Agency, offline local models, setup and retention](../ANSWER-PROVIDERS.md) |

Each chapter contains diagrams and exact public source references. The onboarding path starts from
first principles; the principal engineer path starts from invariants and failure boundaries.
The future Azure answer chapters are deliberately marked **planned**, not disguised as working code.

## Illustrations and provenance

`assets/diagrams` contains generated SVGs, their editable Mermaid sources and rendering receipts.
`assets/screenshots` contains actual WPF visual-tree images rendered with synthetic notes,
transcripts and device labels. Its [manifest](assets/screenshots/manifest.json) records image and UI
source hashes. These are not desktop captures or evidence that a meeting recorder excludes the app.
The cover is original schematic artwork.

Screenshots are never captured from the owner's live notes, chats, microphone, meeting or desktop.
The documentation fixture runs separately and does not start audio capture.

## Keep this book alive

Follow [the maintenance contract](../DOCUMENTATION-MAINTENANCE.md) for every relevant change.
Canonical order lives in [book.json](book.json); [catalogue.json](catalogue.json) is the structured
research/writing map. Markdown is the editable source; never patch generated HTML/PDF/SVG by hand.
See [tools/Docs](../../tools/Docs/README.md) for rebuilding and freshness checks.
