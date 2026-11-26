import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from boot_ios_simulator import boot_simulator


class SimulatorBootTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.folder = Path(self.directory.name)
        self.environment = self.folder / "env"
        self.created = []
        self.boots = []
        self.shutdowns = []
        self.status_failures = 0
        self.runtime_available = True
        self.timeouts = []

    def run_command(self, command, **options):
        self.assertTrue(options["check"])
        self.assertGreater(options["timeout"], 0)
        self.timeouts.append(options["timeout"])
        arguments = command[2:]
        output = ""
        if arguments[:2] == ["list", "runtimes"]:
            output = json.dumps({"runtimes": [
                {"identifier": "com.apple.CoreSimulator.SimRuntime.iOS-26-5", "version": "26.5", "isAvailable": self.runtime_available},
                {"identifier": "com.apple.CoreSimulator.SimRuntime.tvOS-27-0", "version": "27.0", "isAvailable": True}]})
        elif arguments[:2] == ["list", "devicetypes"]:
            output = json.dumps({"devicetypes": [{"identifier": "com.apple.CoreSimulator.SimDeviceType.iPhone-14-Pro"}]})
        elif arguments[0] == "create":
            self.assertEqual(arguments[-1], "com.apple.CoreSimulator.SimRuntime.iOS-26-5")
            output = f"00000000-0000-0000-0000-{len(self.created) + 1:012d}"
            self.created.append(output)
        elif arguments[0] == "boot":
            self.boots.append(arguments[1])
        elif arguments[0] == "bootstatus":
            if self.status_failures:
                self.status_failures -= 1
                raise subprocess.TimeoutExpired(command, options["timeout"], output=b"Waiting on System App")
            output = "Finished"
        elif arguments[0] == "shutdown":
            self.shutdowns.append(arguments[1])
        else:
            self.fail(f"Unexpected simulator command: {arguments}")
        return Mock(stdout=output)

    def boot(self, **options):
        return boot_simulator(self.folder, self.environment, runner=self.run_command, **options)

    def test_success_records_created_device_for_later_steps(self):
        device = self.boot()
        self.assertEqual(self.boots, [device])
        self.assertEqual(self.environment.read_text().strip(), f"STARTUP_SIMULATOR={device}")
        self.assertEqual(self.shutdowns, [])

    def test_stalled_boot_retries_once_using_another_fresh_device(self):
        self.status_failures = 1
        device = self.boot()
        self.assertEqual(device, self.created[1])
        self.assertEqual(self.shutdowns, [self.created[0]])
        self.assertEqual(len(self.boots), 2)
        self.assertIn("Waiting on System App", (self.folder / "simulator-boot-1.log").read_text())

    def test_two_stalled_boots_fail_before_app_verification(self):
        self.status_failures = 2
        with self.assertRaisesRegex(RuntimeError, "before SDA was installed or launched"):
            self.boot()
        self.assertEqual(self.shutdowns, self.created)
        self.assertEqual(len(self.created), 2)

    def test_missing_ios_runtime_does_not_boot_tvos(self):
        self.runtime_available = False
        with self.assertRaisesRegex(RuntimeError, "No available iOS"):
            self.boot()
        self.assertEqual(self.created, [])

    def test_every_command_is_bounded_by_total_budget(self):
        self.boot(boot_timeout=240, budget=30)
        self.assertTrue(all(timeout <= 30 for timeout in self.timeouts))

    def test_exhausted_total_budget_shuts_down_without_another_attempt(self):
        elapsed = [0]

        def run_with_expired_budget(command, **options):
            if command[2] == "bootstatus":
                elapsed[0] = 30
                raise subprocess.TimeoutExpired(command, options["timeout"])
            return self.run_command(command, **options)

        with patch("boot_ios_simulator.time.monotonic", side_effect=lambda: elapsed[0]):
            with self.assertRaisesRegex(RuntimeError, "before SDA was installed or launched"):
                boot_simulator(self.folder, self.environment, budget=30, runner=run_with_expired_budget)
        self.assertEqual(len(self.created), 1)
        self.assertEqual(self.shutdowns, self.created)


if __name__ == "__main__":
    unittest.main()
