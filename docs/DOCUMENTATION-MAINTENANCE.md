# Living documentation contract

The owner requested book-level documentation, maintained images and a development journal on
2026-10-08. Documentation is now part of feature delivery, not a one-off writing exercise.
The contributor making a behavior change owns the corresponding documentation update.

The root README is a visual front page: use current synthetic UI images, concise status and clear
links. Keep setup detail, explanations, architecture and caveats in `OVERVIEW.md` and the dedicated
guides, linked through `docs/README.md`. Retain a short capture-limit and local-only Ask notice on
the front page; do not turn it back into a long technical document.

## Completion requirements

1. Read the affected handbook chapters and primary contract before changing the implementation.
2. Update behavior, defaults, commands, examples, failure paths, source line ranges and status labels
   in the same change. Do not describe a proposal as implemented.
3. Update diagrams when ownership, data flow, state transitions or trust boundaries change.
4. For UI/rendering/theme/source-selector changes, rerun the synthetic documentation fixture and
   review every exported image. Regenerate no real user content. New UI surfaces need matching fixtures.
5. Append a dated milestone to root `JOURNAL.md` and the development ledger for significant decisions,
   implementations, regressions or new evidence. Routine wording fixes need not fabricate a milestone.
6. Update the relevant TODO evidence and index. Keep blocked qualification distinct from completed code.
7. Rebuild the illustrated editions and run their freshness check. Review a representative rendered
   page for each changed chapter and every changed image, not merely the exit code.
8. Maintain the [source atlas](source-atlas/README.md): read changed first-party files, update their
   authored block explanations/ranges and reviewed source hashes, and cover newly added files.
   Blocks partition every physical line; do not auto-rewrite hashes to excuse stale explanations.
   Authored website HTML/CSS/JavaScript and media configuration are included. Named generated
   tutorial/caption exports, evidence/locks and byte-identical PowerShell mirrors remain separately classified.

The public journal must be understandable from public repository sources alone. Private continuity
stores are not a substitute and must not become book inputs.

## Change-to-document matrix

| Change | Minimum affected surfaces |
| --- | --- |
| Window/cursor/capture | Handbook04/05/11/17, capture contract/matrix, screenshot fixture where visible |
| Editor/save/export | Handbook06/11/19, user guide, data-loss/error examples |
| Input selection/PCM | Handbook07/08/19, DUAL-SOURCE-AUDIO, synthetic source fixtures |
| Protocol or recognizer | Handbook08/09/11, service README and frozen protocol version |
| Native startup/packaging | Handbook10/16/19, preview guide, packaged guide, allowlisted contents |
| Engine/performance result | Handbook12/21, benchmark receipt and limitations |
| Azure Ask | Handbook13/14/18/21, four AI-ANSWER contracts and workstreams27-37 |
| New evidence or qualification | Handbook17/21, IMPLEMENTATION-STATUS, exact-version qualification row |
| Book pipeline | Tool README, relevant parser tests, this contract and generated manifest |

## Reproducible local commands

Run from the repository root using PowerShell7, Node and an installed Edge/Chromium:

```powershell
# Only when documentation dependencies are missing or their manifest changes:
npm ci --prefix .\tools\Docs --no-audit --no-fund --ignore-scripts

# After relevant UI changes; separate synthetic process, no recording:
.\scripts\Export-HandbookScreenshots.ps1

node --test .\tools\Docs\test-docs.mjs
node .\tools\Docs\source-atlas.mjs --validate
node .\tools\Docs\source-atlas.mjs
.\scripts\Build-Handbook.ps1 -AcceptReviewedSource
.\scripts\Build-Handbook.ps1 -Check
```

`-AcceptReviewedSource` is an explicit reviewer assertion, not an automatic fix for stale prose.
Review the changed source and its claims first. Omit it for an initial build or a documentation-only
change that does not alter previously recorded code/tool inputs.

The build blocks on missing chapters, invalid line ranges, missing links/images, stale UI image
hashes, Mermaid failures, missing internal anchors or horizontal print overflow.
It now also requires a complete current source atlas. The atlas detects unannotated first-party
files, gaps/overlaps, stale source hashes, oversized explanation/excerpt ranges and script-mirror drift.
Its HTML includes all source; its PDF includes all authored explanations and selected excerpts. The handbook records hashes
for cited source files, chapters, tooling and outputs. This detects drift in **recorded inputs**;
it cannot discover an uncited new feature or prove that a sentence correctly interprets source.
Human source review remains required. Re-run the script even if source changes are outside the hash
set when the semantics affect a chapter.

`-NoPdf` supports an intermediate HTML-only edit pass. It is not sufficient for a finished book
milestone: perform a full build so the manifest contains the PDF and its hash/page count.
The final freshness check explicitly rejects an HTML-only manifest.

## Illustration policy

- Keep Mermaid fenced sources in Markdown, with accessible titles/descriptions and explicit status.
  The build renders locally; do not paste private architecture into hosted diagram services.
- Keep fewer than roughly15 nodes per diagram; split larger concepts. Sequence diagrams use
  `autonumber`. Use the established dark palette and concise labels.
- UI pictures must be actual synthetic WPF renders, labelled as such. Never imply that successfully
  rendering the app internally proves Windows or a remote sharing recipient cannot capture it.
- Preserve provenance: source hashes, image hashes, dimensions and the no-recording declaration.
  If appearance changed, rerender; do not merely rewrite an old hash.
- README derivatives live in `docs/assets/readme`. `Export-ReadmeImages.ps1` composites only the
  approved synthetic source images over their palette backgrounds and records both hashes.
  The screenshot exporter invokes it automatically; run it alone when reusing unchanged captures.
- Do not use live microphone/playback tests, desktop capture, chat scraping or cloud uploads to
  generate documentation. Screenshots of a future answer pane must use fake streams.

## Journal and review style

Explain the problem, decision, change, evidence, limit and next unresolved boundary. Preserve an
incorrect earlier approach as a **superseded decision**, not a quietly edited success story.
Label owner observations, synthetic results, hardware observations and external research separately.
Use exact artifact identities where relevant, but do not publish tokens, private note contents,
personal endpoint names or real meeting transcripts.

Keep `.ps1` and `.txt` script twins byte-identical. Generated artifacts are local deliverables;
their existence is not permission to push, publish, deploy or send them anywhere.

The full procedure is [workflow06](../TODO/WORKFLOWS/06_LIVING_DOCUMENTATION.todo).
Workstream38 establishes the initial edition; the ongoing obligation applies to every subsequent
feature even when38 is DONE.
