import sys
import tempfile
import threading
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from wait_for_ios_startup import wait_for_marker


class StartupReadinessTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "ready.txt"

    def test_already_ready(self):
        self.path.write_text("passcode-presented:123")
        wait_for_marker(self.path, "passcode-presented:123", 0)

    def test_delayed_file_creation(self):
        timer = threading.Timer(0.03, lambda: self.path.write_text("passcode-presented:123"))
        timer.start()
        self.addCleanup(timer.join)
        wait_for_marker(self.path, "passcode-presented:123", 1, interval=0.005)

    def test_does_not_accept_previous_launch_pid(self):
        self.path.write_text("passcode-presented:1234")
        updated = threading.Event()

        def replace():
            updated.set()
            self.path.write_text("passcode-presented:123")

        timer = threading.Timer(0.03, replace)
        timer.start()
        self.addCleanup(timer.join)
        wait_for_marker(self.path, "passcode-presented:123", 1, interval=0.005, exact=True)
        self.assertTrue(updated.is_set())

    def test_missing_file_times_out(self):
        with self.assertRaises(TimeoutError):
            wait_for_marker(self.path, "passcode-presented:123", 0.01, interval=0.005)

    def test_native_crash_fails_without_waiting_for_timeout(self):
        self.path.write_text("Native Crash Reporting\nGot a SIGSEGV while executing native code.")
        with self.assertRaisesRegex(RuntimeError, "crashed during native startup"):
            wait_for_marker(self.path, "passcode-presented:123", 0)

    def test_crash_after_readiness_does_not_pass(self):
        self.path.write_text("SDA startup: passcode presented.\nNative Crash Reporting")
        with self.assertRaises(RuntimeError):
            wait_for_marker(self.path, "SDA startup: passcode presented.", 0)

    def test_managed_startup_failure_does_not_pass(self):
        self.path.write_text("SDA startup: FAILED")
        with self.assertRaises(RuntimeError):
            wait_for_marker(self.path, "passcode-presented:123", 0)

    def test_host_marker_does_not_imply_passcode_readiness(self):
        self.path.write_text("SDA startup: native host presented.")
        with self.assertRaises(TimeoutError):
            wait_for_marker(self.path, "SDA startup: passcode presented.", 0.01, interval=0.005)


if __name__ == "__main__":
    unittest.main()
