# Third-party notices

These dependency notices do not choose a license for Phantom Fudge. **The owner has not selected
a project license.** Public repository visibility and the authorized Preview 2 prerelease do not
supply one. Preview 2's exact assembled notice/inventory evidence is pending.

The historical v0.2.0-preview.1 payload includes its exact third-party license/notice files under
`licenses` and the installed runtime/package directories. Preserve those files when redistributing
an authorized artifact. [Release provenance](release-0.2.0-preview.1.json) records the assembled
artifact identities. Publication is not a legal certification or proof of publisher licensing entitlement.

## NAudio 2.2.1 - MIT

Source: https://github.com/naudio/NAudio/tree/v2.2.1

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## .NET runtime and WPF

Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Self-contained runtime packs may contain additional third-party notices. Preserve those
shipped files. A public distribution must audit notices for its exact runtime and dependency set.

## Palette attribution

DeepSeaFoam and Harbor Daylight color values reference the owner's canonical
https://github.com/Zanark/DeepSeaFoam palettes. DeepSeaFoam credits SpriteCanvas and
Ethan Schoonover's Solarized (https://ethanschoonover.com/solarized/).
The WPF role mapping/control templates here are newly authored. No showcase artwork,
audio, product icons or bundled fonts were copied. See CURSOR-AND-THEMES.md for revision/hashes.

## Local speech service and bundled runtime

Vosk 0.3.45 and the explicitly downloaded `vosk-model-small-en-us-0.15` are Apache-2.0.
The model is not committed to the repository and was not included in the older native development
ZIP. The new installer is intended to include the verified model for offline operation. Provenance
and the observed archive hash are in `services\transcription\model.json` and that service's README.

The accurate English model is `Systran/faster-whisper-small.en`, with MIT licensing and the
pinned revision/file identities recorded in `services\transcription\whisper-models.json`. These
observed/pinned checksums are not publisher signatures. The bundled Python distribution and
installed Python wheels carry their own license/notice metadata; the release build must retain
it rather than infer every dependency's terms from Vosk or Whisper.
(services/transcription/model.json:2-10)
(services/transcription/whisper-models.json:1-5,17-26)

### Application-private package changes

The final runtime candidate uses pinned embedded **CPython 3.13.14 x64**, not a copy of the developer's
virtual environment. It retains the CPython license, Python package license metadata, selected
upstream notices and CPU-source notices under `licenses\speech-runtime`.
**This is engineering provenance, not legal certification. Exact installer redistribution review
remains a release gate, and no project license has been selected.**
(tools/Installer/runtime-lock.json:1-11)
(tools/Installer/bundle_runtime.py:465-535,668-673,687-694)

| Installed variant | Disclosed source change |
| --- | --- |
| `faster-whisper 1.2.1+phantomfudge1` | Removes optional encoded-file decoding and the `av` dependency; attempted file decoding fails explicitly. The service uses the retained in-memory PCM array path |
| `CTranslate2 4.6.0+phantomfudge2` | Same upstream Python-binding version with a rebuilt CPU-only oneDNN3.1.1 DLL and Microsoft compiler OpenMP; no CUDA/cuDNN/proprietary Intel OpenMP binaries |

The recipe records package changes and replaced-file hashes and regenerates package `RECORD`
files rather than presenting these as unmodified upstream wheels. It excludes PyAV/FFmpeg and
collects CPU-source/package notices and separately licensed VC runtime terms; none is a Phantom
Fudge project license. The PCM-only transformation is bound to the exact locked wheel and input
`audio.py`; hashes also protect the unchanged transcription, feature extraction, VAD and padding
helper code. Installed metadata closure describes the **modified** dependency graph, explicitly
not an intact upstream graph or proof of execution. The separate generated-PCM gate requires real
Vosk/Whisper execution and clean child exit. The final handoff reports that gate and Unicode
relocation passing; the earlier Ruy stage remains rejected. See [current evidence](IMPLEMENTATION-STATUS.md).
Old development-build performance/accuracy evidence does not qualify the new backend.
(tools/Installer/runtime-lock.json:31-51)
(tools/Installer/bundle_runtime.py:108-126,332-390,538-609)

`licenses\speech-runtime\PACKAGE-PATCHES.json` preserves the omission rationale: PyAV19's bundled
FFmpeg codec classification requires a component/source/license review not established for this
release. Excluding unused encoded-file decoding avoids shipping those unresolved codec components.
This is **not** a claim that PyAV is inherently nonredistributable or that a dependency's top-level
license alone establishes compliance for its entire binary bundle.
(tools/Installer/runtime-lock.json:43-50)
(tools/Installer/bundle_runtime.py:465-467,510-513)

Vosk's pinned package is unmodified. The first-party startup adapter works around its Windows
non-ASCII model-path limitation by verifying the model, taking a load lock, changing the working
directory through Python's wide Windows API, loading `"."`, and restoring the directory in `finally`.
This is a service adapter change, not an undocumented patch to the Vosk wheel.
(services/transcription/recognizer.py:25-63)

App-local x64 Microsoft Visual C++ runtime DLLs, including `vcomp140.dll` for compiler OpenMP,
are included under their own Distributable Code terms; they are not relicensed by Phantom Fudge.
Native notices also include oneDNN, cpu_features, Xbyak, spdlog/fmt, pybind11, nlohmann/json and half.
(tools/Installer/bundle_runtime.py:412-462,465-535)

The recipe copies unmodified, hash-locked files from the official VS2022 `VC/Redist` directories,
not a host System32 installation, and verifies their Microsoft Authenticode signatures. It retains
the pinned official Visual Studio license DOCX and a convenience text extraction; the DOCX remains
authoritative. Verifying origin and signatures does not establish the publisher's licensing
entitlement or legal compliance. Redistribution requires the applicable valid license and terms;
having build tools installed is not that certification.
(tools/Installer/runtime-lock.json:10-26,95)
(tools/Installer/bundle_runtime.py:412-489)

These DLLs are deployed beside bundled Python, without a global VC installer or elevation.
Application updates must service this app-local CRT; system-wide VC servicing is not assumed.
The dependency audit strengthens engineering evidence, not licensing approval or clean-VM
qualification. No Phantom Fudge project license is implied.
(tools/Installer/bundle_runtime.py:454-462,528-534)
(tools/Installer/runtime_audit.py:126-166,222-233)

### Public dependency provenance

The runtime owner reports obtaining hash-locked wheels from the
Tsinghua public PyPI mirror after the official file CDN failed TLS. The recipe permits that
public mirror only for the same pinned artifact hashes and discloses the policy in the speech
runtime manifest. This is a download-source disclosure, not relaxed integrity checking or
permission to disable TLS verification. The final staged payload was rebuilt offline from the
verified cache. See [the dated runtime handoff](IMPLEMENTATION-STATUS.md) for its manifest identity
and qualification limits; earlier scratch payloads are not release inputs.
(tools/Installer/bundle_runtime.py:77-126,690)

Supplemental Vosk native-dependency notices have been added. Its wheel is hash-pinned, but
the upstream build used moving Kaldi/OpenFst references; collected license revisions do not
establish reproducible compilation of every native Vosk dependency. Preserve the exact shipped
notices and source links; exact installer redistribution review is still required.
(tools/Installer/bundle_runtime.py:518-526)

The separate Docker recipe includes Python and pinned Python packages from
`services\transcription\requirements.txt`, including aiohttp and Vosk dependencies. Preserve their
installed package LICENSE/metadata. Final container redistribution requires the exact-image
license inventory. Docker is not installed by the Windows installer. A dependency's license does
not authorize a project license or establish that the exact package has passed release review.
