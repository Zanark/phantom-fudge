# Local engineering-book pipeline

This directory builds the Markdown sources listed in `docs\handbook\book.json` into an illustrated
offline HTML reader and printable A4 PDF. It uses pinned Mermaid, markdown-it and puppeteer-core
dependencies, plus an **already-installed** Edge/Chromium. It does not download a browser, host a
website, publish artifacts, invoke Azure or inspect live notes.

## Prerequisites and commands

Use PowerShell7 and Node24 (the initial edition used Node24.13.0). From the repository root:

```powershell
# Install only when missing or after dependency changes:
npm ci --prefix .\tools\Docs --no-audit --no-fund --ignore-scripts
node --test .\tools\Docs\test-docs.mjs
.\scripts\Build-Handbook.ps1
.\scripts\Build-Handbook.ps1 -Check
```

The first build expects all Markdown chapters, the public root `JOURNAL.md`, original cover and
synthetic screenshot manifest. To regenerate screenshots after relevant UI changes:

```powershell
.\scripts\Export-HandbookScreenshots.ps1
```

That script builds an isolated .NET output and runs the app's `--documentation-screenshots` fixture.
It captures the WPF visual tree with synthetic content/device labels, never the desktop. It neither
opens capture endpoints nor starts speech. Do not run simultaneous .NET builds sharing `obj`.
No existing owner app is closed; a timeout terminates only the fixture's own PID.

The screenshot exporter also runs `Export-ReadmeImages.ps1`. It composites five verified synthetic
renders onto their palette backgrounds so GitHub's light/dark page backgrounds do not wash out
transparent UI text. Reuse current captures without opening the app with:

```powershell
.\scripts\Export-ReadmeImages.ps1
```

That Windows-only step uses System.Drawing, performs no recording, and records source/output hashes
in `docs\assets\readme\manifest.json`. The full book freshness check also covers those derivatives.

If Edge is installed outside the standard locations, set an absolute browser path in the same shell:

```powershell
$env:PHANTOM_DOCS_BROWSER = 'C:\Path\To\chrome.exe'
.\scripts\Build-Handbook.ps1
```

The renderer launches its own headless browser and closes it in `finally`; it never attaches to a
personal browser profile. Page requests outside `file`, `data`, `about` and `blob` are blocked.
External citation links are retained for readers but not fetched during generation. Package setup
is a separate dependency download, not an offline operation.

## Build modes

| Option | Meaning |
| --- | --- |
| `-Check` | Compare all recorded inputs and output hashes without launching a browser |
| `-AcceptReviewedSource` | Acknowledge that changed source/tool inputs have been reviewed against the prose/journal |
| `-NoPdf` | Intermediate HTML/diagram build; not a complete final book edition |
| `node .\tools\Docs\build.mjs --force-diagrams` | Ignore diagram render caches |

The wrapper passes failures through as errors. A bad diagram or stale screenshot never becomes an
empty placeholder. Fix the source and rebuild; do not edit generated files to suppress a failure.
An HTML-only manifest reports `intermediate-html-only`; `-Check` rejects it until a full PDF build.

## Inputs, outputs and provenance

Each chapter requires quoted `title`/`description` frontmatter, at least five distinct public source
citations and two diagrams. Citation paths and ranges are checked; prose interpretation still needs
human review. The catalogue has an uncollapsed onboarding section, unique nodes, depth/child limits
and source-linked writing prompts.

The pipeline validates local Markdown links/images, generates SVG/Mermaid/cache files into
`docs\handbook\assets\diagrams`, renders HTML and checks its images/anchors/print width before
printing a tagged PDF with page numbers. It also checks reader widths of1440,768 and390 pixels.
`manifest.json` captures SHA256 input/output hashes,
chapter/word/diagram counts and PDF page count. Word counts exclude fenced code/diagram blocks;
they are a reproducible size measure, not a literary-quality score.

`assets\screenshots\manifest.json` separately records the actual UI assembly hash and source/image
hashes. A generated UI image proves appearance under its synthetic fixture, **not capture exclusion**.
Keep the HTML and `assets` together; the PDF contains the printed illustrations.

References outside the repository and private-store/token/dependency paths are rejected, including
real paths reached through symlinks. Only public HTTP(S) external links are accepted. This is a
bounded local documentation tool, not a general untrusted-document sanitizer.

## Troubleshooting

| Failure | Correct response |
| --- | --- |
| Missing dependency | Run the pinned `npm ci` command above; do not add a global renderer |
| Browser not found | Set `PHANTOM_DOCS_BROWSER` to an installed executable |
| Invalid citation | Read current source; correct the range and the associated claim |
| Stale UI image | Regenerate the synthetic fixture; review the actual resulting image |
| Mermaid parse error | Repair the fenced source locally; keep a smaller focused diagram |
| Missing internal anchor | Correct the Markdown target to the chapter's actual heading |
| Print overflow | Fix the relevant table/code/diagram layout, then rebuild the PDF |
| Changed recorded source | Review docs, screenshots and journal; rebuild with `-AcceptReviewedSource` |

Follow [the maintenance contract](../../docs/DOCUMENTATION-MAINTENANCE.md). No build step grants
permission to capture a meeting, publish a book, change application behavior or deploy Azure resources.
