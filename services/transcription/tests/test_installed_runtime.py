"""Installed paths and lifecycle recovery without a real service or inference."""

from contextlib import redirect_stdout
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import MagicMock, patch
import uuid

SERVICE = Path(__file__).resolve().parents[1]
ROOT = SERVICE.parents[1]
sys.path.insert(0, str(SERVICE))
import configuration
import manage
import provision_whisper
import recognizer


class InstalledPathTests(unittest.TestCase):
    def setUp(self):
        self.folder = ROOT / ".local" / "installed-runtime-tests" / uuid.uuid4().hex
        self.folder.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.folder)

    def inspect_paths(self, extra):
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith("PHANTOM_TRANSCRIPTION_")}
        environment.update(extra)
        code = (
            "import sys,json; sys.path.insert(0,sys.argv[1]); "
            "import configuration as c,manage as m,provision_whisper as p,server as s; "
            "print(json.dumps({'roots':[str(x) for x in (c.ROOT,m.ROOT,p.ROOT,s.ROOT)],"
            "'models':[str(x) for x in c.paths(c.DEFAULT)],"
            "'config':str(c.CONFIG),'local':str(m.LOCAL),'record':str(m.RECORD)}))"
        )
        result = subprocess.run([sys.executable, "-I", "-B", "-c", code, str(SERVICE)],
                                cwd=self.folder, env=environment, capture_output=True,
                                text=True, check=True, timeout=30)
        return json.loads(result.stdout)

    def test_all_components_share_explicit_writable_workspace_and_immutable_models(self):
        workspace = self.folder / "User name \u00e9" / "InstalledRuntime"
        models = self.folder / "Installed application" / "models"
        data = self.inspect_paths({"PHANTOM_TRANSCRIPTION_WORKSPACE": str(workspace),
                                   "PHANTOM_TRANSCRIPTION_MODELS_ROOT": str(models)})
        self.assertEqual(data["roots"], [str(workspace)] * 4)
        self.assertEqual(data["models"], [str(models / "vosk-model-small-en-us-0.15"),
                                          str(models / "faster-whisper-small.en")])
        self.assertEqual(data["config"], str(workspace / ".local" / "transcription-config.json"))
        self.assertEqual(data["record"], str(workspace / ".local" / "transcription-runtime.json"))
        self.assertFalse(workspace.exists(), "Imports must not initialize private user data.")

    def test_developer_paths_remain_repo_relative_not_working_directory_relative(self):
        data = self.inspect_paths({})
        self.assertEqual(data["roots"], [str(ROOT)] * 4)
        self.assertEqual(data["local"], str(ROOT / ".local"))
        self.assertEqual(Path(data["models"][0]).parent, ROOT / ".local" / "models")

    def test_workspace_without_model_override_defaults_to_its_own_local_models(self):
        data = self.inspect_paths({"PHANTOM_TRANSCRIPTION_WORKSPACE": str(self.folder)})
        self.assertEqual(Path(data["models"][1]).parent, self.folder / ".local" / "models")

    def test_empty_installed_configuration_uses_defaults_without_creating_file(self):
        missing = self.folder / "transcription-config.json"
        self.assertEqual(configuration.load_config(missing), configuration.DEFAULT)
        self.assertFalse(missing.exists())

    def test_existing_user_model_directory_takes_precedence_over_installed_model(self):
        model = self.folder / ".local" / "models" / "faster-whisper-small.en"
        model.mkdir(parents=True)
        data = self.inspect_paths({"PHANTOM_TRANSCRIPTION_WORKSPACE": str(self.folder),
                                   "PHANTOM_TRANSCRIPTION_MODELS_ROOT": str(self.folder / "bundled")})
        self.assertEqual(data["models"][1], str(model))

    def test_provisioner_writes_only_per_user_models_not_installed_bundles(self):
        model = self.folder / "workspace" / ".local" / "models" / "synthetic-model"
        model.mkdir(parents=True)
        with patch.object(provision_whisper, "MODELS_ROOT", self.folder / "bundled"), \
                patch.object(provision_whisper, "ROOT", self.folder / "workspace"), \
                patch.object(provision_whisper, "model_spec", return_value={
                    "directory": model.name, "files": {}, "revision": "synthetic"}), \
                redirect_stdout(io.StringIO()) as output:
            provision_whisper.provision("small.en")
        self.assertTrue(json.loads(output.getvalue())["verified"])
        self.assertFalse((self.folder / "bundled").exists())

    def test_lifecycle_lock_creates_nested_private_directory(self):
        local = self.folder / "nested" / "InstalledRuntime" / ".local"
        with patch.object(manage, "LOCAL", local), manage.lifecycle_lock():
            self.assertTrue(local.is_dir())

    @unittest.skipUnless(os.name == "nt", "Windows native model-path compatibility")
    def test_vosk_unicode_model_path_uses_wide_cwd_and_restores_it(self):
        model = self.folder / "models \u00e9"
        model.mkdir()
        original = Path.cwd()
        native = MagicMock()

        def load(**options):
            self.assertEqual(options["model_path"], ".")
            self.assertEqual(Path.cwd(), model)
            return native

        module = MagicMock(Model=load)
        with patch.object(recognizer, "verify_model"), patch.dict(sys.modules, {"vosk": module}):
            factory = recognizer.VoskFactory(model)
        self.assertIs(factory._model, native)
        self.assertEqual(Path.cwd(), original)

    @unittest.skipUnless(os.name == "nt", "Windows native model-path compatibility")
    def test_vosk_unicode_load_error_restores_working_directory(self):
        model = self.folder / "models \u00e9"
        model.mkdir()
        original = Path.cwd()
        module = MagicMock()
        module.Model.side_effect = RuntimeError("synthetic model failure")
        with patch.object(recognizer, "verify_model"), patch.dict(sys.modules, {"vosk": module}):
            with self.assertRaisesRegex(RuntimeError, "synthetic model failure"):
                recognizer.VoskFactory(model)
        self.assertEqual(Path.cwd(), original)

    def test_spawn_records_identity_immediately_and_disables_bytecode(self):
        local = self.folder / ".local"
        local.mkdir()
        child = MagicMock(pid=1234)
        child.poll.return_value = None
        info = {"pid": 1234, "ready": True}
        def inspect_identity(pid):
            saved = json.loads((local / "record.json").read_text(encoding="utf-8"))
            self.assertEqual(saved["pid"], pid, "Persist PID before process inspection can fail.")
            return "1234567"

        with patch.object(manage, "ROOT", self.folder), patch.object(manage, "LOCAL", local), \
                patch.object(manage, "RECORD", local / "record.json"), \
                patch.object(manage, "token", return_value="synthetic_" + "x" * 40), \
                patch.object(manage, "load_config", return_value=configuration.DEFAULT), \
                patch.object(manage, "read_record", return_value=None), \
                patch.object(manage, "port_in_use", side_effect=[False, True]), \
                patch.object(manage, "validate_start"), \
                patch.object(manage, "paths", return_value=(self.folder / "models", None)), \
                patch.object(manage, "process_identity", side_effect=inspect_identity), \
                patch.object(manage, "owned_info", return_value=info), \
                patch.object(manage, "supports_current_audio", return_value=True), \
                patch.object(manage, "supports_current_engine", return_value=True), \
                patch.object(manage.subprocess, "Popen", return_value=child) as spawn, \
                redirect_stdout(io.StringIO()):
            manage.start()
        record = json.loads((local / "record.json").read_text(encoding="utf-8"))
        self.assertEqual((record["pid"], record["processCreatedAt"]), (1234, "1234567"))
        self.assertEqual(spawn.call_args.args[0][1:4], ["-B", "-X", "utf8"])
        self.assertEqual(spawn.call_args.kwargs["cwd"], self.folder)

    def test_failed_creation_time_probe_keeps_pid_for_later_proven_dead_recovery(self):
        local = self.folder / ".local"
        local.mkdir()
        child = MagicMock(pid=1234)
        with patch.object(manage, "ROOT", self.folder), patch.object(manage, "LOCAL", local), \
                patch.object(manage, "RECORD", local / "record.json"), \
                patch.object(manage, "token", return_value="synthetic_" + "x" * 40), \
                patch.object(manage, "load_config", return_value=configuration.DEFAULT), \
                patch.object(manage, "read_record", return_value=None), \
                patch.object(manage, "port_in_use", return_value=False), \
                patch.object(manage, "validate_start"), \
                patch.object(manage, "paths", return_value=(self.folder / "models", None)), \
                patch.object(manage, "process_identity", side_effect=manage.RuntimeFailure("denied")), \
                patch.object(manage.subprocess, "Popen", return_value=child):
            with self.assertRaises(manage.RuntimeFailure):
                manage.start()
        record = json.loads((local / "record.json").read_text(encoding="utf-8"))
        self.assertEqual(record["pid"], 1234)
        self.assertNotIn("processCreatedAt", record)
        with patch.object(manage, "RECORD", local / "record.json"), \
                patch.object(manage, "process_identity", return_value=None):
            manage.clear_dead_record(record)
        self.assertFalse((local / "record.json").exists())


class DeadReceiptTests(unittest.TestCase):
    def test_start_after_reboot_or_pid_reuse_advances_to_normal_validation(self):
        for identity in (None, "222"):
            with self.subTest(identity=identity), patch.object(manage, "RECORD") as file, \
                    patch.object(manage, "token", return_value="synthetic_" + "x" * 40), \
                    patch.object(manage, "lifecycle_lock"), \
                    patch.object(manage, "load_config", return_value=configuration.DEFAULT), \
                    patch.object(manage, "read_record", return_value={
                        "pid": 123, "processCreatedAt": "111"}), \
                    patch.object(manage, "port_in_use", return_value=False), \
                    patch.object(manage, "process_identity", return_value=identity), \
                    patch.object(manage, "validate_start",
                                 side_effect=manage.RuntimeFailure("synthetic-validation-stop")) as verify, \
                    patch.object(manage.subprocess, "Popen") as spawn:
                with self.assertRaisesRegex(manage.RuntimeFailure, "synthetic-validation-stop"):
                    manage.start()
                file.unlink.assert_called_once()
                verify.assert_called_once()
                spawn.assert_not_called()

    def test_dead_process_receipt_removed_without_signalling_any_process(self):
        for identity in (None, "222"):
            with self.subTest(identity=identity), patch.object(manage, "RECORD") as record, \
                    patch.object(manage, "process_identity", return_value=identity) as probe:
                manage.clear_dead_record({"pid": 123, "processCreatedAt": "111"})
                probe.assert_called_once_with(123)
                record.unlink.assert_called_once()

    def test_live_matching_process_not_overwritten(self):
        with patch.object(manage, "RECORD") as record, \
                patch.object(manage, "process_identity", return_value="111"):
            with self.assertRaises(manage.RuntimeFailure):
                manage.clear_dead_record({"pid": 123, "processCreatedAt": "111"})
            record.unlink.assert_not_called()

    def test_legacy_receipt_only_cleared_when_pid_is_confirmed_absent(self):
        with patch.object(manage, "RECORD") as record, \
                patch.object(manage, "process_identity", return_value=None):
            manage.clear_dead_record({"pid": 123})
            record.unlink.assert_called_once()
        with patch.object(manage, "RECORD") as record, \
                patch.object(manage, "process_identity", return_value="111"):
            with self.assertRaises(manage.RuntimeFailure):
                manage.clear_dead_record({"pid": 123})
            record.unlink.assert_not_called()

    def test_invalid_or_incomplete_identity_is_retained(self):
        for data in ({}, {"pid": None}, {"pid": True}, {"pid": -1}, {"pid": 2**40},
                     {"pid": 123, "processCreatedAt": "not-a-time"}):
            with self.subTest(data=data), patch.object(manage, "RECORD") as record, \
                    patch.object(manage, "process_identity") as probe:
                with self.assertRaises(manage.RuntimeFailure):
                    manage.clear_dead_record(data)
                probe.assert_not_called()
                record.unlink.assert_not_called()

    def test_access_denied_cannot_remove_receipt(self):
        with patch.object(manage, "RECORD") as record, \
                patch.object(manage, "process_identity", side_effect=manage.RuntimeFailure("denied")):
            with self.assertRaises(manage.RuntimeFailure):
                manage.clear_dead_record({"pid": 123, "processCreatedAt": "111"})
            record.unlink.assert_not_called()

    @unittest.skipUnless(os.name == "nt", "Windows process identity API")
    def test_current_test_process_creation_identity_is_stable(self):
        first = manage.process_identity(os.getpid())
        self.assertRegex(first, r"^[1-9][0-9]+$")
        self.assertEqual(first, manage.process_identity(os.getpid()))
