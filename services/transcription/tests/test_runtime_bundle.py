"""Public-input packaging tests; fixtures stay inside the ignored repository data."""

import csv
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import stat
import struct
import unittest
from unittest.mock import patch
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("bundle_runtime", ROOT / "tools" / "Installer" / "bundle_runtime.py")
bundle = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bundle)


class BundleTests(unittest.TestCase):
    def setUp(self):
        self.folder = ROOT / ".local" / "runtime-bundle-tests" / uuid.uuid4().hex
        self.folder.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.folder)

    def test_archive_paths_reject_traversal_windows_streams_and_aliases(self):
        for name in ("", ".", "..", "../token", "/absolute", "C:/absolute", r"..\token",
                     "file:stream", "root/../token", "root//file", "./file",
                     "root/name.", "root/name ", "root/CON", "root/nul.txt"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                bundle.archive_path(name)
        self.assertEqual(bundle.archive_path("nested/public-model.bin").parts,
                         ("nested", "public-model.bin"))

    def archive(self, entries):
        archive = self.folder / "public.zip"
        with zipfile.ZipFile(archive, "w") as stream:
            for name, content in entries:
                stream.writestr(name, content)
        return archive

    def test_extract_rejects_symlinks(self):
        entry = zipfile.ZipInfo("link")
        entry.create_system = 3
        entry.external_attr = (stat.S_IFLNK | 0o777) << 16
        archive = self.archive([(entry, "outside")])
        with self.assertRaises(ValueError):
            bundle.extract_zip(archive, self.folder / "target")

    def test_source_archive_requires_one_root(self):
        archive = self.archive([("one/file", "a"), ("two/file", "b")])
        with self.assertRaises(ValueError):
            bundle.extract_zip(archive, self.folder / "target", strip_root=True)
        self.assertFalse((self.folder / "target").exists())

    def test_archive_never_overwrites_existing_files(self):
        target = self.folder / "target"
        target.mkdir()
        existing = target / "file"
        existing.write_bytes(b"retain")
        archive = self.archive([("file", "replacement")])
        with self.assertRaises(ValueError):
            bundle.extract_zip(archive, target)
        self.assertEqual(existing.read_bytes(), b"retain")

    def test_wheel_skips_console_scripts_and_maps_only_library_schemes(self):
        archive = self.archive([("pkg.data/scripts/cli.exe", "not-shipped"),
                                ("pkg.data/purelib/pkg.py", "public"),
                                ("pkg.dist-info/METADATA", "public metadata")])
        target = self.folder / "target"
        bundle.extract_zip(archive, target, wheel=True)
        self.assertEqual((target / "pkg.py").read_text(), "public")
        self.assertFalse((target / "pkg.data").exists())

    def test_verified_cache_reused_without_network(self):
        path = self.folder / "input"
        path.write_bytes(b"public")
        expected = hashlib.sha256(b"public").hexdigest()
        with patch.object(bundle, "urlopen") as request:
            self.assertEqual(bundle.download("https://pypi.org/", path, expected), path)
            request.assert_not_called()

    def test_tampered_cache_or_missing_offline_input_is_refused(self):
        path = self.folder / "input"
        with patch.object(bundle, "urlopen") as request:
            with self.assertRaisesRegex(ValueError, "Offline"):
                bundle.download("https://pypi.org/", path, "0" * 64, offline=True)
            path.write_text("changed")
            with self.assertRaisesRegex(ValueError, "failed verification"):
                bundle.download("https://pypi.org/", path, "0" * 64)
            request.assert_not_called()

    def test_nonpublic_download_urls_rejected_before_network(self):
        for url in ("file:///C:/private", "http://pypi.org/file",
                    "https://internal.example/file", "https://secret@pypi.org/file"):
            with self.subTest(url=url), patch.object(bundle, "urlopen") as request:
                with self.assertRaises(ValueError):
                    bundle.download(url, self.folder / "input", "0" * 64)
                request.assert_not_called()

    def test_model_copy_uses_only_hashed_allowlist_not_extraneous_neighbor_files(self):
        service, source, target = (self.folder / name for name in ("service", "source", "target"))
        service.mkdir()
        expected = hashlib.sha256(b"public").hexdigest()
        for name in ("vosk-public", "whisper-public"):
            model = source / name
            model.mkdir(parents=True)
            (model / "model.bin").write_bytes(b"public")
            (model / "personal.txt").write_text("synthetic-not-for-payload")
        (source / "token.txt").write_text("synthetic-not-for-payload")
        (service / "model.json").write_text(json.dumps({
            "name": "vosk-public", "files": {"model.bin": expected},
            "license": "Apache-2.0", "url": "https://public.example"}))
        (service / "whisper-models.json").write_text(json.dumps({"models": {"small.en": {
            "directory": "whisper-public", "files": {"model.bin": {"sha256": expected, "bytes": 6}},
            "repository": "public/model", "revision": "synthetic"}}}))
        with patch.object(bundle, "SERVICE", service):
            bundle.copy_models(target, source)
        self.assertEqual(sorted(path.relative_to(target).as_posix() for path in target.rglob("*")
                                if path.is_file()), ["vosk-public/model.bin", "whisper-public/model.bin"])

    def test_pcm_only_patch_discloses_version_dependency_and_record(self):
        site = self.folder / "site"
        package = site / "faster_whisper"
        info = site / "faster_whisper-1.2.1.dist-info"
        package.mkdir(parents=True)
        info.mkdir()
        (package / "version.py").write_text('__version__ = "1.2.1"\n')
        (package / "audio.py").write_text(
            "import av\n\ndef decode_audio(path):\n"
            "    resampler = av.audio.resampler.AudioResampler()\n"
            "    return resampler\n\n"
            "def pad_or_trim(array):\n    return array\n")
        (info / "METADATA").write_text("Name: faster-whisper\nVersion: 1.2.1\nRequires-Dist: av>=11\n")
        (info / "RECORD").write_text("")
        helper = "def pad_or_trim(array):\n    return array\n"
        specification = {"audioSha256": bundle.digest(package / "audio.py"),
                         "padOrTrimSha256": hashlib.sha256(helper.encode()).hexdigest(),
                         "unchangedFiles": {}, "reason": "synthetic fixture"}
        disclosure = bundle.patch_whisper(site, specification)
        bundle.regenerate_record(site, "faster_whisper")
        patched = site / disclosure["distInfo"]
        metadata = (patched / "METADATA").read_text()
        self.assertIn(bundle.FW_VERSION, metadata)
        self.assertNotIn("Requires-Dist: av", metadata)
        scope = {}
        exec((package / "audio.py").read_text(), scope)
        array = [1, 2, 3]
        self.assertIs(scope["pad_or_trim"](array), array)
        with self.assertRaisesRegex(RuntimeError, "PCM"):
            scope["decode_audio"]("not-read")
        rows = list(csv.reader(io.StringIO((patched / "RECORD").read_text())))
        self.assertTrue(all((site / row[0]).is_file() for row in rows))
        self.assertNotEqual(disclosure["audioBeforeSha256"], disclosure["audioAfterSha256"])

    def test_exact_locked_upstream_wheel_patch_preserves_numpy_transcription_sources(self):
        lock = json.loads(bundle.LOCK.read_text(encoding="utf-8"))
        row = next(item for item in lock["wheels"] if item[0] == "faster-whisper")
        wheel = ROOT / ".local" / "installer-runtime-cache" / row[2]
        if not wheel.exists():
            self.skipTest("Run the explicit installer bundle build to populate its public wheel cache.")
        self.assertTrue(bundle.verified(wheel, row[3]))
        site = self.folder / "site"
        bundle.extract_zip(wheel, site, wheel=True)
        original = (site / "faster_whisper" / "audio.py").read_text(encoding="utf-8")
        disclosure = bundle.patch_whisper(site, lock["packagePatches"]["fasterWhisper"])
        patched = (site / "faster_whisper" / "audio.py").read_text(encoding="utf-8")
        self.assertEqual(original[original.index("def pad_or_trim("):],
                         patched[patched.index("def pad_or_trim("):])
        for relative, expected in disclosure["unchangedSourceHashes"].items():
            self.assertEqual(bundle.digest(site / relative), expected)
        self.assertEqual(disclosure["audioBeforeSha256"],
                         lock["packagePatches"]["fasterWhisper"]["audioSha256"])
        self.assertNotIn("import av", patched)
        with self.assertRaisesRegex(ValueError, "source hash"):
            bundle.patch_whisper(site, lock["packagePatches"]["fasterWhisper"])

    def test_service_allowlist_cannot_capture_configuration_token_or_developer_environment(self):
        self.assertEqual(len(bundle.SERVICE_FILES), len(set(bundle.SERVICE_FILES)))
        for name in bundle.SERVICE_FILES:
            self.assertEqual(Path(name).name, name)
            self.assertNotIn(name, {"transcription-token.txt", "transcription-config.json", ".venv"})
            self.assertTrue((bundle.SERVICE / name).is_file())

    def pe(self, name, *, dependency="KERNEL32.dll", delay=None, forward=None, machine=0x8664):
        """Small PE fixture with a single raw section; it is never executed."""
        data = bytearray(0x1200)
        data[:2] = b"MZ"
        struct.pack_into("<I", data, 60, 0x80)
        data[0x80:0x84] = b"PE\0\0"
        struct.pack_into("<HH", data, 0x84, machine, 1)
        struct.pack_into("<H", data, 0x94, 240)
        optional = 0x98
        struct.pack_into("<H", data, optional, 0x20B)
        struct.pack_into("<Q", data, optional + 24, 0x180000000)
        struct.pack_into("<I", data, optional + 60, 0x200)
        struct.pack_into("<I", data, optional + 108, 16)
        section = optional + 240
        data[section:section + 8] = b".rdata\0\0"
        struct.pack_into("<IIII", data, section + 8, 0x1000, 0x1000, 0x1000, 0x200)
        strings = 0x1600

        def text(value):
            nonlocal strings
            result = strings
            encoded = value.encode("ascii") + b"\0"
            data[strings - 0xE00:strings - 0xE00 + len(encoded)] = encoded
            strings += len(encoded)
            return result

        struct.pack_into("<II", data, optional + 112 + 8, 0x1000, 40)
        struct.pack_into("<IIIII", data, 0x200, 0, 0, 0, text(dependency), 0)
        if delay:
            struct.pack_into("<II", data, optional + 112 + 13 * 8, 0x1200, 64)
            struct.pack_into("<II", data, 0x400, 1, text(delay))
        if forward:
            struct.pack_into("<II", data, optional + 112, 0x1400, 0x100)
            struct.pack_into("<I", data, 0x600 + 20, 1)
            struct.pack_into("<I", data, 0x600 + 28, 0x1430)
            struct.pack_into("<I", data, 0x630, 0x1450)
            encoded = forward.encode("ascii") + b"\0"
            data[0x650:0x650 + len(encoded)] = encoded
        path = self.folder / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def add_fixture_crt(self):
        for name in bundle.AUDIT["REQUIRED_CRT"]:
            self.pe(name)

    def test_pe_reader_includes_delay_imports_and_forwarders(self):
        path = self.pe("native.dll", dependency="MSVCP140.dll",
                       delay="SHELL32.dll", forward="python313.PyArg_Parse")
        result = bundle.AUDIT["pe_dependencies"](path)
        self.assertEqual(result["imports"], ["msvcp140.dll"])
        self.assertEqual(result["delayImports"], ["shell32.dll"])
        self.assertEqual(result["forwarders"], ["python313.dll"])

    def test_pe_reader_rejects_truncation_and_unterminated_import_tables(self):
        path = self.pe("native.dll")
        data = bytearray(path.read_bytes())
        path.write_bytes(data[:200])
        with self.assertRaisesRegex(ValueError, "Truncated PE"):
            bundle.AUDIT["pe_dependencies"](path)
        struct.pack_into("<I", data, 0x98 + 112 + 8 + 4, 20)
        path.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "Unterminated"):
            bundle.AUDIT["pe_dependencies"](path)

    def test_pe_closure_accepts_only_local_crt_and_reviewed_windows_dependencies(self):
        self.add_fixture_crt()
        self.pe("consumer.pyd", dependency="MSVCP140.dll", delay="SHELL32.dll")
        result = bundle.AUDIT["audit_pe_closure"](self.folder)
        self.assertTrue(result["passed"])
        self.assertFalse(result["system32UsedToResolveNonInboxDependencies"])
        self.assertEqual(result["files"]["consumer.pyd"]["resolved"]["msvcp140.dll"], "msvcp140.dll")

    def test_pe_closure_rejects_missing_crt_even_if_the_host_has_global_vc_runtime(self):
        self.add_fixture_crt()
        self.pe("consumer.pyd", dependency="MSVCP140.dll")
        (self.folder / "msvcp140.dll").unlink()
        with self.assertRaisesRegex(ValueError, "Unbundled.*msvcp140"):
            bundle.AUDIT["audit_pe_closure"](self.folder)

    def test_pe_closure_does_not_accept_unrelated_package_dll_or_unknown_system_dependency(self):
        self.add_fixture_crt()
        self.pe("other/native.dll")
        self.pe("consumer.pyd", dependency="native.dll")
        with self.assertRaisesRegex(ValueError, "Unbundled.*native.dll"):
            bundle.AUDIT["audit_pe_closure"](self.folder)
        self.pe("consumer.pyd", dependency="developer-only.dll")
        with self.assertRaisesRegex(ValueError, "Unbundled.*developer-only"):
            bundle.AUDIT["audit_pe_closure"](self.folder)

    def test_pe_closure_rejects_non_x64_runtime_but_inventories_unused_setuptools_templates(self):
        self.add_fixture_crt()
        path = self.pe("wrong.dll", machine=0xAA64)
        with self.assertRaisesRegex(ValueError, "Non-x64"):
            bundle.AUDIT["audit_pe_closure"](self.folder)
        path.unlink()
        self.pe("Lib/site-packages/setuptools/cli-arm64.exe", machine=0xAA64)
        self.assertTrue(bundle.AUDIT["audit_pe_closure"](self.folder)["passed"])

    def test_crt_copy_rejects_unpinned_input_before_copy_or_signature_process(self):
        specification = json.loads(bundle.LOCK.read_text(encoding="utf-8"))["vcRuntime"]
        output = self.folder / "output"
        output.mkdir()
        with patch.object(bundle, "visual_studio", return_value=(self.folder, None)), \
                patch.object(bundle.subprocess, "run") as process:
            with self.assertRaisesRegex(ValueError, "missing or differs"):
                bundle.copy_crt(output, None, specification)
        self.assertEqual(list(output.iterdir()), [])
        process.assert_not_called()
