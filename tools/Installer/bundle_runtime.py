"""Build a public-input, CPU-only Windows speech bundle; never start the service."""

import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
import csv
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import runpy
import shutil
import stat
import struct
import subprocess
import sys
import time
from urllib.error import URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen
import uuid
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SERVICE = ROOT / "services" / "transcription"
LOCK = Path(__file__).with_name("runtime-lock.json")
SERVICE_FILES = (
    "manage.py", "server.py", "recognizer.py", "accurate.py", "configuration.py",
    "diagnostics.py", "provision_whisper.py", "requirements.txt", "model.json",
    "whisper-models.json", "technical-vocabulary.json",
)
ALLOWED_HOSTS = {
    "www.python.org", "pypi.org", "files.pythonhosted.org", "codeload.github.com",
    "raw.githubusercontent.com", "huggingface.co", "alphacephei.com",
    "pypi.tuna.tsinghua.edu.cn",
    "visualstudio.microsoft.com",
}
FW_VERSION = "1.2.1+phantomfudge1"
CT2_VERSION = "4.6.0+phantomfudge2"
CPU_RECIPE_VERSION = 4
AUDIT = runpy.run_path(str(Path(__file__).with_name("runtime_audit.py")))


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def plain_file(path):
    return (path.is_file() and not path.is_symlink()
            and not any(p.is_symlink() or p.is_junction() for p in (path, *path.parents)))


def verified(path, expected, size=None):
    return (plain_file(path) and (size is None or path.stat().st_size == size)
            and digest(path) == expected)


def public_response(url, timeout):
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.hostname not in ALLOWED_HOSTS or parsed.username:
        raise ValueError("Only allowlisted public HTTPS inputs may be downloaded.")
    request = Request(url, headers={"User-Agent": "PhantomFudge-public-runtime-build/1"})
    for attempt in range(4):
        try:
            return urlopen(request, timeout=timeout)
        except (URLError, TimeoutError):
            if attempt == 3:
                raise
            time.sleep(2 ** attempt)


def download(url, destination, expected, *, offline=False, size=None):
    if destination.exists():
        if verified(destination, expected, size):
            return destination
        raise ValueError(f"Cached public input failed verification: {destination.name}")
    if offline:
        raise ValueError(f"Offline build is missing {destination.name}")
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.hostname not in ALLOWED_HOSTS or parsed.username:
        raise ValueError("Only allowlisted public HTTPS inputs may be downloaded.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    part = destination.with_name(destination.name + "." + uuid.uuid4().hex + ".partial")
    try:
        with public_response(url, timeout=180) as source, part.open("xb") as target:
            remaining = (size if size is not None else 200_000_000) + 1
            while remaining:
                block = source.read(min(1024 * 1024, remaining))
                if not block:
                    break
                target.write(block)
                remaining -= len(block)
            if remaining == 0:
                raise ValueError("Public input exceeded its download bound.")
        if not verified(part, expected, size):
            raise ValueError(f"Downloaded public input failed verification: {destination.name}")
        part.replace(destination)
        return destination
    finally:
        part.unlink(missing_ok=True)


def pypi_input(name, version, filename, expected, cache, offline):
    destination = cache / filename
    if destination.exists() or offline:
        return download("https://pypi.org/", destination, expected, offline=offline)
    with public_response(f"https://pypi.org/pypi/{name}/{version}/json", timeout=60) as response:
        data = json.loads(response.read(4_000_000))
    matches = [item for item in data["urls"] if item["filename"] == filename]
    if len(matches) != 1 or matches[0]["digests"]["sha256"] != expected:
        raise ValueError(f"PyPI does not match the reviewed lock for {name}.")
    item = matches[0]
    try:
        return download(item["url"], destination, expected, offline=offline, size=item["size"])
    except URLError:
        if urlparse(item["url"]).hostname != "files.pythonhosted.org":
            raise
        mirror = item["url"].replace("https://files.pythonhosted.org/",
                                     "https://pypi.tuna.tsinghua.edu.cn/", 1)
        print(f"Using the public PyPI mirror for the same hash-locked {filename}.", flush=True)
        return download(mirror, destination, expected, offline=offline, size=item["size"])


def archive_path(name):
    path = PurePosixPath(name)
    if (not name or "\\" in name or ":" in name or path.is_absolute()
            or any(part in ("", ".", "..") or part.endswith((" ", "."))
                   or re.fullmatch(r"(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?", part)
                   for part in name.split("/"))):
        raise ValueError("Unsafe archive path.")
    return path


def extract_zip(archive, target, *, strip_root=False, wheel=False):
    if any(path.is_symlink() or path.is_junction() for path in (target, *target.parents)):
        raise ValueError("Archive target cannot contain filesystem links.")
    with zipfile.ZipFile(archive) as source:
        roots = {archive_path(item.filename.rstrip("/")).parts[0] for item in source.infolist()}
        if strip_root and len(roots) != 1:
            raise ValueError("Source archive must have exactly one root directory.")
        for item in source.infolist():
            path = archive_path(item.filename.rstrip("/"))
            if stat.S_ISLNK(item.external_attr >> 16):
                raise ValueError("Archive links are not accepted.")
            parts = path.parts[1:] if strip_root else path.parts
            if not parts:
                continue
            if wheel and parts[0].endswith(".data"):
                if len(parts) < 3:
                    continue
                if parts[1] not in ("purelib", "platlib"):
                    continue
                parts = parts[2:]
            destination = target.joinpath(*parts)
            if any(path.is_symlink() or path.is_junction() for path in destination.parents
                   if path.is_relative_to(target)):
                raise ValueError("Archive target cannot contain filesystem links.")
            if item.is_dir():
                destination.mkdir(parents=True, exist_ok=True)
                continue
            destination.parent.mkdir(parents=True, exist_ok=True)
            if destination.exists():
                raise ValueError(f"Archive would overwrite {destination.name}")
            with source.open(item) as stream, destination.open("xb") as output:
                shutil.copyfileobj(stream, output)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def file_manifest(root):
    return {path.relative_to(root).as_posix(): {
        "bytes": path.stat().st_size, "sha256": digest(path),
    } for path in sorted(root.rglob("*")) if path.is_file()}


def build_environment(work):
    work.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    for key in tuple(environment):
        if (key.startswith(("PHANTOM_", "PYTHON", "HF_", "TRANSFORMERS", "PIP_", "CT2_"))
                or key in ("GITHUB_TOKEN", "GH_TOKEN", "OPENAI_API_KEY", "ANTHROPIC_API_KEY")):
            environment.pop(key)
    environment.update(TEMP=str(work), TMP=str(work), PYTHONDONTWRITEBYTECODE="1",
                       PIP_DISABLE_PIP_VERSION_CHECK="1", PIP_NO_INPUT="1")
    return environment


def run(arguments, *, cwd, environment, log):
    with log.open("ab") as stream:
        stream.write((subprocess.list2cmdline([str(arg) for arg in arguments]) + "\n").encode("utf-8"))
        stream.flush()
        result = subprocess.run([str(arg) for arg in arguments], cwd=cwd, env=environment,
                                stdin=subprocess.DEVNULL, stdout=stream, stderr=stream,
                                check=False, timeout=1800)
    if result.returncode:
        raise RuntimeError(f"Build command failed ({result.returncode}); inspect {log}.")


def visual_studio(path=None):
    if path:
        root = Path(path).resolve()
    else:
        tool = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / (
            r"Microsoft Visual Studio\Installer\vswhere.exe")
        result = subprocess.run([str(tool), "-latest", "-products", "*", "-requires",
                                 "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                                 "-property", "installationPath"], capture_output=True,
                                text=True, check=True, timeout=30)
        root = Path(result.stdout.strip())
    cmake = root / r"Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    if not cmake.is_file():
        raise ValueError("Build host needs Visual Studio 2022 C++ tools and CMake (not target machines).")
    return root, cmake


def build_cpu_library(lock, cache, vs, offline):
    source = cache / "cpu-source"
    build = cache / "cpu-build"
    prefix = cache / "cpu-prefix"
    receipt = cache / "cpu-build-receipt.json"
    inputs = {"sources": lock["cpuSources"], "recipeVersion": CPU_RECIPE_VERSION}
    if receipt.exists():
        data = json.loads(receipt.read_text(encoding="utf-8"))
        dll = prefix / "bin" / "ctranslate2.dll"
        if data["inputs"] == inputs and verified(dll, data["dllSha256"]):
            return source, dll, data
    if source.exists():
        shutil.rmtree(source)
    for item in lock["cpuSources"]:
        url = f"https://codeload.github.com/{item['repository']}/zip/{item['revision']}"
        archive = download(url, cache / item["filename"], item["sha256"], offline=offline)
        extract_zip(archive, source / item["destination"], strip_root=True)
    if build.exists():
        shutil.rmtree(build)
    vsroot, cmake = visual_studio(vs)
    environment = build_environment(cache / "work")
    log = cache / "cpu-build.log"
    dnnl_build = cache / "onednn-build"
    if dnnl_build.exists():
        shutil.rmtree(dnnl_build)
    dnnl_options = [
        "-G", "Visual Studio 17 2022", "-A", "x64",
        f"-DCMAKE_GENERATOR_INSTANCE={vsroot}", f"-DCMAKE_INSTALL_PREFIX={prefix}",
        "-DCMAKE_BUILD_TYPE=Release", "-DDNNL_LIBRARY_TYPE=STATIC",
        "-DDNNL_BUILD_EXAMPLES=OFF", "-DDNNL_BUILD_TESTS=OFF",
        "-DDNNL_ENABLE_WORKLOAD=INFERENCE", "-DDNNL_ENABLE_PRIMITIVE=CONVOLUTION;REORDER",
        "-DONEDNN_BUILD_GRAPH=OFF", "-DDNNL_CPU_RUNTIME=OMP", "-DDNNL_GPU_RUNTIME=NONE",
        "-DDNNL_WERROR=OFF", "-DCMAKE_C_FLAGS=/utf-8", "-DCMAKE_CXX_FLAGS=/utf-8 /EHsc",
        "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
    ]
    run([cmake, "-S", source / "third_party" / "oneDNN", "-B", dnnl_build, *dnnl_options],
        cwd=cache, environment=environment, log=log)
    run([cmake, "--build", dnnl_build, "--config", "Release", "--target", "install", "--parallel", "4"],
        cwd=cache, environment=environment, log=log)
    options = [
        "-G", "Visual Studio 17 2022", "-A", "x64",
        f"-DCMAKE_GENERATOR_INSTANCE={vsroot}", f"-DCMAKE_INSTALL_PREFIX={prefix}",
        "-DCMAKE_BUILD_TYPE=Release", "-DBUILD_CLI=OFF", "-DBUILD_TESTS=OFF",
        "-DWITH_CUDA=OFF", "-DWITH_CUDNN=OFF", "-DWITH_MKL=OFF", "-DWITH_DNNL=ON",
        "-DWITH_OPENBLAS=OFF", "-DWITH_RUY=OFF", "-DOPENMP_RUNTIME=COMP",
        f"-DDNNL_INCLUDE_DIR={prefix / 'include'}", f"-DDNNL_LIBRARY={prefix / 'lib' / 'dnnl.lib'}",
        "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
        "-DCMAKE_C_FLAGS=/utf-8", "-DCMAKE_CXX_FLAGS=/utf-8 /EHsc",
    ]
    run([cmake, "-S", source, "-B", build, *options], cwd=cache, environment=environment, log=log)
    run([cmake, "--build", build, "--config", "Release", "--target", "install", "--parallel", "4"],
        cwd=cache, environment=environment, log=log)
    dll = prefix / "bin" / "ctranslate2.dll"
    data = {"inputs": inputs, "cmakeOptions": options, "dllSha256": digest(dll),
            "oneDnnOptions": dnnl_options, "oneDnnStaticLibrarySha256": digest(prefix / "lib" / "dnnl.lib"),
            "cpuBackend": "oneDNN 3.1.1", "openMpRuntime": "Microsoft compiler OpenMP",
            "cuda": False, "cudnn": False, "intelOpenMpRuntime": False}
    write_json(receipt, data)
    return source, dll, data


def build_srt(lock, cache, offline):
    item = lock["sourceDistribution"]
    source = pypi_input(item["name"], item["version"], item["filename"], item["sha256"],
                       cache, offline)
    output = cache / "srt-wheel"
    output.mkdir(exist_ok=True)
    receipt = output / "receipt.json"
    if receipt.exists():
        data = json.loads(receipt.read_text(encoding="utf-8"))
        wheel = output / data["filename"]
        if data["sourceSha256"] == item["sha256"] and verified(wheel, data["sha256"]):
            return wheel, data
    environment = build_environment(cache / "work")
    run([sys.executable, "-I", "-B", "-m", "pip", "--isolated", "wheel", "--no-deps",
         "--no-build-isolation", "--no-cache-dir", "--no-index", "--wheel-dir", output, source],
        cwd=cache, environment=environment, log=cache / "srt-build.log")
    wheels = list(output.glob("srt-3.5.3-*.whl"))
    if len(wheels) != 1:
        raise ValueError("Expected one srt wheel built from the pinned public source.")
    data = {"filename": wheels[0].name, "sha256": digest(wheels[0]),
            "sourceSha256": item["sha256"], "builderPython": sys.version.split()[0]}
    write_json(receipt, data)
    return wheels[0], data


def replace_once(text, original, replacement):
    if text.count(original) != 1:
        raise ValueError("Reviewed third-party patch no longer matches its pinned input.")
    return text.replace(original, replacement, 1)


def patch_package(site, package, old_version, new_version, metadata_transform):
    original = site / f"{package}-{old_version}.dist-info"
    target = original.with_name(f"{package}-{new_version}.dist-info")
    original.rename(target)
    metadata = target / "METADATA"
    text = metadata.read_text(encoding="utf-8")
    text = replace_once(text, f"Version: {old_version}\n", f"Version: {new_version}\n")
    metadata.write_text(metadata_transform(text), encoding="utf-8", newline="\n")
    version = site / package / "version.py"
    version.write_text(replace_once(version.read_text(encoding="utf-8"),
                                   f'"{old_version}"', f'"{new_version}"'),
                       encoding="utf-8", newline="\n")
    (target / "RECORD").unlink()
    return target


def patch_whisper(site, specification):
    audio = site / "faster_whisper" / "audio.py"
    before = digest(audio)
    if before != specification["audioSha256"]:
        raise ValueError("PCM-only patch input does not match the reviewed upstream source hash.")
    for relative, expected in specification["unchangedFiles"].items():
        if not verified(site.joinpath(*archive_path(relative).parts), expected):
            raise ValueError("The upstream ndarray transcription path does not match its reviewed hash.")
    text = replace_once(audio.read_text(encoding="utf-8"), "import av\n", "")
    if text.count("    resampler = av.audio.resampler.AudioResampler(") != 1:
        raise ValueError("Reviewed third-party audio decoder no longer matches its pinned input.")
    helper = text[text.index("def pad_or_trim("):]
    if hashlib.sha256(helper.encode("utf-8")).hexdigest() != specification["padOrTrimSha256"]:
        raise ValueError("The reviewed ndarray padding helper changed.")
    text = (
        '"""PhantomFudge PCM-only modification of faster-whisper 1.2.1.\n\n'
        "Encoded-file decoding and PyAV/FFmpeg are intentionally not bundled.\n"
        "The upstream ndarray padding helper below is preserved verbatim.\n"
        '"""\n\nimport numpy as np\n\n\n'
        "def decode_audio(*args, **kwargs):\n"
        '    """File/media decoding is intentionally absent from this CPU PCM bundle."""\n'
        '    raise RuntimeError("This installed runtime accepts in-memory PCM arrays only; file decoding is not bundled.")\n\n\n'
    ) + helper
    audio.write_text(text, encoding="utf-8", newline="\n")
    info = patch_package(site, "faster_whisper", "1.2.1", FW_VERSION,
                         lambda value: replace_once(value, "Requires-Dist: av>=11\n", ""))
    return {"package": "faster-whisper", "upstreamVersion": "1.2.1", "version": FW_VERSION,
            "change": "Remove unused encoded-file decoding and av dependency; ndarray transcription unchanged.",
            "audioBeforeSha256": before, "audioAfterSha256": digest(audio),
            "unchangedSourceHashes": specification["unchangedFiles"],
            "padOrTrimSha256": specification["padOrTrimSha256"],
            "licenseRationale": specification["reason"],
            "distInfo": info.name}


def patch_ctranslate(site, library):
    directory = site / "ctranslate2"
    replaced = {file.name: digest(file) for file in directory.glob("*.dll")}
    for file in directory.glob("*.dll"):
        file.unlink()
    shutil.copyfile(library, directory / "ctranslate2.dll")
    info = patch_package(site, "ctranslate2", "4.6.0", CT2_VERSION, lambda text: text)
    return {"package": "ctranslate2", "upstreamVersion": "4.6.0", "version": CT2_VERSION,
            "change": "Same-version public Python binding; CPU-only oneDNN 3.1.1 with Microsoft compiler OpenMP. No CUDA/cuDNN/Intel OpenMP binaries.",
            "removedDlls": replaced, "cpuDllSha256": digest(library), "distInfo": info.name}


def regenerate_record(site, name):
    info = next(site.glob(name + "-*.dist-info"))
    roots = [site / name, info]
    rows = []
    for root in roots:
        for file in sorted(root.rglob("*")):
            if file.is_file() and file.name != "RECORD":
                encoded = base64.urlsafe_b64encode(bytes.fromhex(digest(file))).decode().rstrip("=")
                rows.append((file.relative_to(site).as_posix(), "sha256=" + encoded, file.stat().st_size))
    rows.append(((info / "RECORD").relative_to(site).as_posix(), "", ""))
    with (info / "RECORD").open("w", newline="", encoding="utf-8") as stream:
        csv.writer(stream).writerows(rows)


def copy_models(destination, source):
    vosk = json.loads((SERVICE / "model.json").read_text(encoding="utf-8"))
    whisper = json.loads((SERVICE / "whisper-models.json").read_text(encoding="utf-8"))["models"]["small.en"]
    specifications = [(vosk["name"], {name: {"sha256": value} for name, value in vosk["files"].items()}),
                      (whisper["directory"], whisper["files"])]
    for name, files in specifications:
        for relative, spec in files.items():
            parts = archive_path(relative).parts
            original = source.joinpath(name, *parts)
            if not verified(original, spec["sha256"], spec.get("bytes")):
                raise ValueError(f"Public model file absent or not verified: {name}/{relative}")
            target = destination.joinpath(name, *parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
    return {"vosk": {"name": vosk["name"], "license": vosk["license"], "source": vosk["url"]},
            "whisper": {"name": "small.en", "license": "MIT", "repository": whisper["repository"],
                        "revision": whisper["revision"]}}


def copy_crt(python, vs, specification):
    vsroot, _ = visual_studio(vs)
    root = vsroot / "VC" / "Redist" / "MSVC" / specification["directoryVersion"] / "x64"
    files = []
    inputs = []
    for name, expected in specification["files"].items():
        if not re.fullmatch(r"[a-z0-9_]+\.dll", name):
            raise ValueError("Invalid locked VC runtime filename.")
        folder = "Microsoft.VC143.OpenMP" if name == "vcomp140.dll" else "Microsoft.VC143.CRT"
        file = root / folder / name
        if not verified(file, expected):
            raise ValueError(f"Official Visual Studio redistributable missing or differs from the lock: {name}")
        inputs.append(file)
    if not AUDIT["REQUIRED_CRT"].issubset({file.name for file in inputs}):
        raise ValueError("VC runtime payload is incomplete.")
    names = ",".join("'" + str(file).replace("'", "''") + "'" for file in inputs)
    script = (
        "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); "
        f"@({names}) | ForEach-Object {{ "
        "$s=Get-AuthenticodeSignature -LiteralPath $_; "
        "if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation,') "
        "{ throw 'Invalid Microsoft redistributable signature' }; "
        "[pscustomobject]@{name=[IO.Path]::GetFileName($_); status=$s.Status.ToString(); "
        "signer=$s.SignerCertificate.Subject; thumbprint=$s.SignerCertificate.Thumbprint} "
        "} | ConvertTo-Json -Compress"
    )
    shell = Path(os.environ["SystemRoot"]) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    environment = os.environ.copy()
    environment["PSModulePath"] = str(shell.parent / "Modules")
    result = subprocess.run(
        [str(shell), "-NoProfile", "-NonInteractive", "-EncodedCommand",
         base64.b64encode(script.encode("utf-16le")).decode("ascii")],
        capture_output=True, text=True, encoding="utf-8", env=environment, timeout=120)
    if result.returncode:
        raise RuntimeError("Microsoft CRT signature verification failed: " + result.stderr[-2500:])
    signatures = {item["name"]: item for item in json.loads(result.stdout)}
    if signatures.keys() != specification["files"].keys():
        raise ValueError("Incomplete VC runtime signature verification.")
    for file in inputs:
        shutil.copyfile(file, python / file.name)
        files.append({"name": file.name, "sha256": digest(file), "signature": signatures[file.name],
                      "source": file.relative_to(vsroot).as_posix()})
    return {"version": specification["directoryVersion"], "fileVersion": specification["fileVersion"],
            "architecture": "x64", "files": files,
            "source": "Unmodified, hash-locked files from official Visual Studio 2022 VC/Redist",
            "deployment": "Application-local beside embedded python.exe; no global installer or elevation",
            "license": "Microsoft Visual Studio 2022 Distributable Code terms",
            "licenseUrl": "https://visualstudio.microsoft.com/license-terms/vs2022-ga-proenterprise/",
            "redistListUrl": "https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution",
            "redistributorRequirement": "Redistribution requires a valid Visual Studio license and compliance with its terms.",
            "servicing": "App-local CRT updates must be shipped in PhantomFudge updates; no automatic global VC servicing."}


def collect_notices(lock, cache, output, source, site, offline):
    output.mkdir(parents=True, exist_ok=True)
    write_json(output / "PACKAGE-PATCHES.json", lock["packagePatches"])
    header = (source / "include" / "half_float" / "half.hpp").read_text(encoding="utf-8")
    half_notice, separator, _ = header.partition("\n// Version ")
    if not separator or "Permission is hereby granted" not in half_notice or len(half_notice) > 10000:
        raise ValueError("Pinned half-float header license could not be extracted.")
    (output / "half-float-LICENSE.txt").write_text(half_notice + "\n", encoding="utf-8")
    for item in lock["notices"]:
        public = download(item["url"], cache / item["filename"], item["sha256"], offline=offline)
        shutil.copyfile(public, output / item["filename"])
        if item["filename"] == "Microsoft-VS2022-Enterprise-Professional-License-EN.docx":
            with zipfile.ZipFile(public) as document:
                xml = ET.fromstring(document.read("word/document.xml"))
            namespace = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
            paragraphs = [
                "".join(node.text or "" for node in paragraph.findall(".//w:t", namespace))
                for paragraph in xml.findall(".//w:p", namespace)
            ]
            text = "\n\n".join(paragraphs)
            if "Distributable Code" not in text or "Distribution Requirements" not in text:
                raise ValueError("Pinned Visual Studio license document has unexpected content.")
            (output / "Microsoft-VS2022-LICENSE.txt").write_text(
                "Plain-text extraction for convenience; the adjacent official DOCX is authoritative.\n"
                f"Source: {item['url']}\nSHA256: {item['sha256']}\n\n" + text + "\n", encoding="utf-8")
    for path in source.rglob("*"):
        if path.is_file() and re.match(r"(?i)^(licen[cs]e|copying|copyright|notice)([._-]|$)", path.name):
            target = output / "cpu-source" / path.relative_to(source)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
    for info in sorted(site.glob("*.dist-info")):
        for path in info.rglob("*"):
            if path.is_file() and (
                    "licenses" in path.parts
                    or re.match(r"(?i)^(licen[cs]e|copying|notice)([._-]|$)", path.name)):
                target = output / "python-packages" / info.name / path.relative_to(info)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
    for name in ("LICENSE", "ThirdPartyNotices.txt"):
        shutil.copyfile(site / "onnxruntime" / name, output / ("onnxruntime-" + name))
    (output / "SPEECH-RUNTIME-NOTICES.txt").write_text(
        "PhantomFudge CPU-only speech runtime\n\n"
        "Public model/license origins and exact artifact hashes are in speech-runtime-manifest.json.\n"
        "Python package license texts are retained in site-packages and collected here.\n"
        "Whisper and Silero models: MIT; Vosk English model/API: Apache-2.0.\n"
        "The faster-whisper PCM-only patch is disclosed in the manifest; no PyAV or FFmpeg is shipped.\n"
        "PACKAGE-PATCHES.json records its upstream source hashes, unchanged ndarray path, and license rationale.\n"
        "Installed distribution metadata is intentionally modified; metadata closure is not evidence of\n"
        "an intact upstream graph or of inference correctness. Synthetic PCM execution is checked separately.\n"
        "CTranslate2 4.6.0 is rebuilt with oneDNN 3.1.1/cpu_features (Apache-2.0),\n"
        "Xbyak (BSD-3-Clause), spdlog/fmt (MIT), and Microsoft compiler OpenMP.\n"
        "Its binding/header notices also include pybind11 (BSD-3-Clause), nlohmann/json and half (MIT).\n"
        "No CUDA, cuDNN or proprietary Intel OpenMP runtime is shipped.\n"
        "Vosk includes MinGW GCC/libstdc++ runtime libraries with the GCC Runtime Library Exception.\n"
        "Its upstream Windows build also uses Kaldi/OpenFst (Apache-2.0), OpenBLAS/CLAPACK\n"
        "(BSD-style), libf2c (permissive), and MinGW winpthreads (MIT/BSD-style); notices are included.\n"
        "Vosk's wheel is hash-pinned, but its upstream build used moving Kaldi/OpenFst references;\n"
        "the supplemental license revisions are not a claim of reproducible native Vosk compilation.\n"
        "Upstream build recipe: https://github.com/alphacep/vosk-api/blob/v0.3.45/travis/Dockerfile.win\n"
        "GCC source: https://gcc.gnu.org/git/?p=gcc.git;a=shortlog;h=refs/heads/releases/gcc-10\n"
        "Debian MinGW build sources: https://sources.debian.org/src/gcc-mingw-w64/\n"
        "MinGW source: https://github.com/mingw-w64/mingw-w64/tree/v8.0.0\n"
        "NumPy's bundled numerical/runtime notices and ONNX Runtime's ThirdPartyNotices are retained.\n"
        "Microsoft VC runtime DLLs are Distributable Code under the Visual Studio 2022 license terms;\n"
        "they are not relicensed by PhantomFudge. See the manifest's CRT license/redistribution links.\n"
        "The exact unmodified official VC/Redist files are hash-pinned and Microsoft signatures are checked.\n"
        "Redistribution requires a valid Visual Studio license and compliance with those terms.\n"
        "These DLLs are app-local; no admin/global VC installer runs. App updates must service them.\n"
        "Windows 11 provides the Universal CRT and reviewed inbox DLLs; VC140 is not assumed inbox.\n"
        "This installer does not redistribute Agency, Copilot CLI, provider credentials, or user profiles.\n",
        encoding="utf-8")


def validate_imports(output, cache):
    python = output / "runtime" / "python" / "python.exe"
    workspace = cache / "probe-workspace"
    workspace.mkdir(exist_ok=True)
    environment = build_environment(cache / "work")
    environment.update({
        "PATH": str(python.parent) + os.pathsep + str(Path(os.environ["SystemRoot"]) / "System32"),
        "PHANTOM_TRANSCRIPTION_WORKSPACE": str(workspace),
        "PHANTOM_TRANSCRIPTION_MODELS_ROOT": str(output / "models"),
        "HF_HUB_OFFLINE": "1", "HF_HUB_DISABLE_IMPLICIT_TOKEN": "1",
        "HF_HUB_DISABLE_TELEMETRY": "1", "TRANSFORMERS_OFFLINE": "1",
        "OPENBLAS_NUM_THREADS": "1", "OMP_NUM_THREADS": "1",
    })
    program = AUDIT["DLL_PROBE"] + """
import importlib.metadata as metadata, importlib.util, json, pathlib, sys
import aiohttp, vosk, numpy, ctranslate2, onnxruntime, tokenizers, faster_whisper
from packaging.requirements import Requirement
import configuration, manage, provision_whisper
assert sys.flags.isolated and sys.dont_write_bytecode
assert importlib.util.find_spec('av') is None
assert 'int8' in ctranslate2.get_supported_compute_types('cpu')
assert pathlib.Path(configuration.ROOT) == manage.ROOT == provision_whisper.ROOT
assert len(configuration.paths(configuration.DEFAULT)) == 2
configuration.verify_config(configuration.load_config())
problems = []
for dist in metadata.distributions():
    for text in dist.requires or ():
        dependency = Requirement(text)
        if dependency.marker and not dependency.marker.evaluate({'extra': ''}):
            continue
        try:
            version = metadata.version(dependency.name)
            if not dependency.specifier.contains(version):
                problems.append(str(dependency))
        except metadata.PackageNotFoundError:
            problems.append(str(dependency))
assert not problems, problems
try:
    faster_whisper.decode_audio('not-read.wav')
except RuntimeError:
    pass
else:
    raise AssertionError('Encoded file decoding should not be bundled')
print(json.dumps({'imports': True, 'modelHashes': True, 'dependencyClosure': True,
                  'dependencyClosureScope': 'installed modified distribution metadata only',
                  'upstreamDependencyGraphIntact': False,
                  'serviceStarted': False, 'inference': False,
                  'dllLoadProbe': _dll_evidence(),
                  'python': sys.version.split()[0], 'cpuTypes': sorted(ctranslate2.get_supported_compute_types('cpu'))}))
"""
    result = subprocess.run([str(python), "-I", "-B", "-X", "utf8", "-c", program],
                            cwd=workspace, env=environment, stdin=subprocess.DEVNULL,
                            capture_output=True, text=True, timeout=180)
    if result.returncode:
        (cache / "import-probe.log").write_text(result.stderr, encoding="utf-8")
        raise RuntimeError(f"Embedded import/hash probe failed; inspect {cache / 'import-probe.log'}.")
    return json.loads(result.stdout.strip().splitlines()[-1])


def validate_synthetic_pcm(output, cache):
    test = SERVICE / "tests" / "test_packaged_pcm_runtime.py"
    environment = build_environment(cache / "work")
    environment["PHANTOM_PACKAGED_RUNTIME"] = str(output)
    result = subprocess.run([sys.executable, "-I", "-B", str(test)], cwd=ROOT, env=environment,
                            stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=180)
    if result.returncode:
        log = cache / "synthetic-pcm-probe.log"
        log.write_text(result.stdout + result.stderr, encoding="utf-8")
        raise RuntimeError(f"Real synthetic PCM/clean-exit validation failed; inspect {log}.")
    evidence = json.loads(result.stdout.strip().splitlines()[-1])
    evidence["testSourceSha256"] = digest(test)
    return evidence


def bundle(args):
    if os.name != "nt" or sys.version_info[:2] != (3, 13) or struct.calcsize("P") != 8:
        raise ValueError("The build host must use CPython 3.13 x64 on Windows.")
    output = Path(args.output).resolve()
    cache = Path(args.cache).resolve()
    if not output.is_relative_to(ROOT) or not cache.is_relative_to(ROOT):
        raise ValueError("Build output and cache must remain inside this repository.")
    if output == ROOT or output.is_relative_to(cache) or cache.is_relative_to(output):
        raise ValueError("Output and cache must be separate project subdirectories.")
    if output.exists() and any(output.iterdir()):
        raise ValueError("Choose an empty output directory; no existing payload is overwritten.")
    output.mkdir(parents=True, exist_ok=True)
    cache.mkdir(parents=True, exist_ok=True)
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    locked = {re.sub(r"[-_.]+", "-", row[0]).lower(): row[1] for row in lock["wheels"]}
    locked["srt"] = lock["sourceDistribution"]["version"]
    locked["av"] = "19.0.0"
    requirements = {}
    for line in (SERVICE / "requirements.txt").read_text(encoding="utf-8").splitlines():
        if line.strip():
            name, version = line.split(";")[0].strip().split("==")
            requirements[re.sub(r"[-_.]+", "-", name).lower()] = version
    if requirements != locked:
        raise ValueError("Service requirements changed; review and update the installer hash lock.")
    print("Building the public-source CPU backend (no models/service are started).", flush=True)
    source, dll, cpu = build_cpu_library(lock, cache, args.visual_studio, args.offline)
    runtime = output / "runtime" / "python"
    spec = lock["python"]
    archive = download(spec["url"], cache / spec["filename"], spec["sha256"],
                       offline=args.offline, size=spec["bytes"])
    extract_zip(archive, runtime)
    site = runtime / "Lib" / "site-packages"
    site.mkdir(parents=True)
    print("Obtaining hash-locked public Python packages.", flush=True)
    def fetch(row):
        return pypi_input(*row, cache, args.offline)
    with ThreadPoolExecutor(max_workers=3) as pool:
        wheels = list(pool.map(fetch, lock["wheels"]))
    srt, srt_receipt = build_srt(lock, cache, args.offline)
    for wheel in [*wheels, srt]:
        extract_zip(wheel, site, wheel=True)
    patches = [patch_whisper(site, lock["packagePatches"]["fasterWhisper"]), patch_ctranslate(site, dll)]
    regenerate_record(site, "faster_whisper")
    regenerate_record(site, "ctranslate2")
    (runtime / "python313._pth").write_text(
        "python313.zip\n.\nLib\\site-packages\n..\\..\\services\\transcription\nimport site\n",
        encoding="ascii")
    service = output / "services" / "transcription"
    service.mkdir(parents=True)
    for name in SERVICE_FILES:
        shutil.copyfile(SERVICE / name, service / name)
    text = (service / "requirements.txt").read_text(encoding="utf-8")
    text = replace_once(text, "av==19.0.0\n", "")
    text = replace_once(text, "faster-whisper==1.2.1\n", f"faster-whisper=={FW_VERSION}\n")
    text = replace_once(text, "ctranslate2==4.6.0\n", f"ctranslate2=={CT2_VERSION}\n")
    (service / "requirements.txt").write_text(text, encoding="utf-8", newline="\n")
    models = copy_models(output / "models", Path(args.models).resolve())
    crt = copy_crt(runtime, args.visual_studio, lock["vcRuntime"])
    notices = output / "licenses" / "speech-runtime"
    collect_notices(lock, cache, notices, source, site, args.offline)
    shutil.copyfile(runtime / "LICENSE.txt", notices / "CPython-LICENSE.txt")
    shutil.copyfile(LOCK, notices / LOCK.name)
    forbidden = ("cudnn", "cublas", "libiomp", "libx264", "libx265", "avcodec")
    if any(any(word in path.name.lower() for word in forbidden) for path in runtime.rglob("*.dll")):
        raise ValueError("A prohibited GPU/video-codec runtime entered the payload.")
    print("Auditing all PE imports/delay imports/forwarders without trusting the host VC installation.", flush=True)
    pe_audit = AUDIT["audit_pe_closure"](runtime)
    print("Checking isolated DLL loads, embedded imports, dependency closure and model hashes.", flush=True)
    validation = validate_imports(output, cache)
    validation["nativeDependencyClosure"] = pe_audit
    if args.verify_synthetic_pcm:
        print("Checking real bundled-model inference and clean exit with generated PCM only.", flush=True)
        validation["syntheticPcm"] = validate_synthetic_pcm(output, cache)
        validation["inference"] = True
        validation["inferenceInput"] = "generated in-memory PCM only; no capture or real service"
    manifest = {"schemaVersion": 1, "target": "Windows 11 x64", "python": lock["python"],
                "lockSha256": digest(LOCK), "builderSha256": digest(Path(__file__)),
                "nativeAuditorSha256": digest(Path(__file__).with_name("runtime_audit.py")),
                "packageDownloadPolicy": "Exact PyPI artifact hashes; Tsinghua public PyPI mirror fallback on official file-host network failure.",
                "upstreamWheels": lock["wheels"], "srtBuild": srt_receipt,
                "cpuBuild": cpu, "packageChanges": patches, "models": models, "vcRuntime": crt,
                "validation": validation, "files": file_manifest(output)}
    write_json(output / "speech-runtime-manifest.json", manifest)
    print(json.dumps({"output": str(output), "files": len(manifest["files"]),
                      "bytes": sum(file["bytes"] for file in manifest["files"].values()),
                      "validation": validation}), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="New/empty payload root inside this repository")
    parser.add_argument("--cache", default=str(ROOT / ".local" / "installer-runtime-cache"))
    parser.add_argument("--models", default=str(ROOT / ".local" / "models"),
                        help="Only the exact pinned public model files are read; never copied recursively.")
    parser.add_argument("--visual-studio", help="Build-host Visual Studio 2022 C++ installation")
    parser.add_argument("--offline", action="store_true", help="Require all public inputs already cached")
    parser.add_argument("--verify-synthetic-pcm", action="store_true",
                        help="Run real model inference on generated in-memory PCM; no service or audio device.")
    args = parser.parse_args()
    try:
        bundle(args)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
