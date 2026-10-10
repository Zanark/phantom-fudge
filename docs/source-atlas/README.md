# Annotated source atlas

[Open the searchable source reader](index.html) | [Read the printable annotations](Phantom-Fudge-Source-Atlas.pdf) | [Coverage ledger](coverage.json) | [Engineering handbook](../handbook/README.md)

220 first-party files, 33867 physical lines and 1359 authored explanation blocks.

Open any file to read its purpose, callers/readers, annotated ranges and focused code excerpts. Expand the full source for stable line links. Search matches code as well as explanations. The PDF prints explanations and selected snippets, not a second dump of every source file.

## Coverage boundary

Physical-line assignment is mechanically checked; it does not prove that every subtlety is understood. External packages, runtime credentials, model weights, build outputs, private context and live device/provider behavior are outside this source walkthrough. Website HTML/CSS/JavaScript and media configuration are first-party source. Explicit tutorial/caption exports, generated evidence receipts and lockfiles are indexed separately. PowerShell `.txt` mirrors are byte-checked against their annotated `.ps1` source.

## Source groups

- [Native audio, strict transport, synthetic tests and local capture controls](../handbook/25-native-audio-source-walkthrough.md): 16 files.
- [Desktop window, explicit questions, and lifecycle source walkthrough](../handbook/22-desktop-source-walkthrough.md): 34 files.
- [Documentation compilers and root configuration](../handbook/28-documentation-source-walkthrough.md): 16 files.
- [Domain values, bounded state, local storage and diagnostics](../handbook/23-domain-source-walkthrough.md): 26 files.
- [Native installed launcher, ownership and maintenance](../INSTALLATION.md): 14 files.
- [Windows installer and release payload packaging](../INSTALLATION.md): 5 files.
- [Installed speech runtime: public inputs, disclosed transformations and bounded verification](../INSTALLATION.md): 7 files.
- [Operations, synthetic measurement, and installed-CLI probe source](../handbook/27-operations-source-walkthrough.md): 31 files.
- [Answer providers, explicit previous-answer context, bounded clarification and process ownership](../handbook/24-provider-source-walkthrough.md): 33 files.
- [Local speech: framing, bounded recognition, ownership and source-level tests](../handbook/26-speech-source-walkthrough.md): 23 files.
- [Authored website, self-contained tutorial media and pending Pages publication](../WEBSITE.md): 15 files.

## Maintenance

Update the relevant authored JSON in `annotations` after reading changed source, including its source hash and line partitions. Then run `node .\tools\Docs\source-atlas.mjs`, followed by `scripts\Build-Handbook.ps1`. `node .\tools\Docs\source-atlas.mjs --check` rejects stale source, gaps, missing files, mirror drift or an HTML-only edition. Do not update a hash merely to bypass a stale explanation.
