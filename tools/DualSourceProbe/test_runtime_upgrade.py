from contextlib import nullcontext
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "services" / "transcription"))
import manage


class UpgradeTests(unittest.TestCase):
    def test_capabilities_require_exact_integer_two(self):
        self.assertFalse(manage.supports_current_audio({}))
        self.assertFalse(manage.supports_current_audio({"speechProtocolVersions": ["2"]}))
        self.assertFalse(manage.supports_current_audio({"speechProtocolVersions": [True]}))
        self.assertTrue(manage.supports_current_audio({"speechProtocolVersions": [1, 2]}))

    def test_unknown_activity_never_allows_shutdown(self):
        for activity in (True, None, "false"):
            with self.subTest(activity=activity), patch.object(manage, "request") as request:
                with self.assertRaises(manage.RuntimeFailure):
                    manage.stop_owned("synthetic", {"instanceId": "a" * 32}, {"active": activity})
                request.assert_not_called()

    def test_current_service_is_reused_without_restart(self):
        with patch.object(manage, "token", return_value="synthetic"), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=True), \
                patch.object(manage, "owned_info", return_value={
                    "ready": True, "active": True, "speechProtocolVersions": [1, 2]
                }), patch.object(manage, "stop_owned") as stop, \
                patch.object(manage.subprocess, "Popen") as spawn:
            manage.start()
            stop.assert_not_called()
            spawn.assert_not_called()

    def test_active_old_service_is_not_restarted(self):
        with patch.object(manage, "token", return_value="synthetic"), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=True), \
                patch.object(manage, "owned_info", return_value={"ready": True, "active": True}), \
                patch.object(manage, "request") as request, \
                patch.object(manage.subprocess, "Popen") as spawn:
            with self.assertRaises(manage.RuntimeFailure):
                manage.start()
            request.assert_not_called()
            spawn.assert_not_called()
