"""Boot a fresh CI simulator with bounded recovery before testing the app."""

import argparse
import json
import subprocess
import sys
import time
import uuid
from pathlib import Path


def boot_simulator(diagnostics: Path, github_env: Path, boot_timeout=240, budget=480, runner=subprocess.run):
    diagnostics.mkdir(parents=True, exist_ok=True)
    deadline = time.monotonic() + budget

    def simctl(*arguments, timeout=20):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("Simulator boot budget exhausted.")
        return runner(["xcrun", "simctl", *arguments], check=True, text=True,
                      stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                      timeout=min(timeout, remaining)).stdout

    runtimes = json.loads(simctl("list", "runtimes", "--json"))["runtimes"]
    available = [item for item in runtimes if item.get("isAvailable") and ".iOS-" in item["identifier"]]
    if not available:
        raise RuntimeError("No available iOS simulator runtime on this runner.")
    runtime = max(available, key=lambda item: tuple(map(int, item["version"].split("."))))["identifier"]
    device_type = "com.apple.CoreSimulator.SimDeviceType.iPhone-14-Pro"
    types = json.loads(simctl("list", "devicetypes", "--json"))["devicetypes"]
    if not any(item["identifier"] == device_type for item in types):
        raise RuntimeError("The iPhone 14 Pro simulator device type is missing.")

    for attempt in range(1, 3):
        device = None
        try:
            device = str(uuid.UUID(simctl("create", f"SDA Startup Check {attempt}", device_type, runtime).strip()))
            with github_env.open("a", encoding="utf-8") as output:
                output.write(f"STARTUP_SIMULATOR={device}\n")
            print(f"Simulator boot attempt {attempt}/2: iPhone 14 Pro ({device}).", flush=True)
            simctl("boot", device)
            status = simctl("bootstatus", device, "-b", timeout=boot_timeout)
            (diagnostics / f"simulator-boot-{attempt}.log").write_text(status, encoding="utf-8")
            print("Simulator boot completed; app verification can begin.", flush=True)
            return device
        except (subprocess.TimeoutExpired, subprocess.CalledProcessError, TimeoutError) as error:
            output = getattr(error, "stdout", None) or ""
            if isinstance(output, bytes):
                output = output.decode("utf-8", errors="replace")
            (diagnostics / f"simulator-boot-{attempt}.log").write_text(
                f"{type(error).__name__}: simulator boot did not complete.\n{output}", encoding="utf-8")
            print(f"Simulator boot attempt {attempt} failed ({type(error).__name__}).", flush=True)
            if device:
                # Only shut down the simulator this script created, including when
                # the total boot budget has expired. Never touch unrelated devices.
                try:
                    runner(["xcrun", "simctl", "shutdown", device], check=True, text=True,
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)
                except (subprocess.SubprocessError, OSError):
                    pass
            if time.monotonic() >= deadline:
                break
    raise RuntimeError("Simulator boot failed before SDA was installed or launched. See simulator-boot logs.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--diagnostics-dir", type=Path, required=True)
    parser.add_argument("--github-env", type=Path, required=True)
    args = parser.parse_args()
    try:
        boot_simulator(args.diagnostics_dir, args.github_env)
    except (OSError, ValueError, subprocess.SubprocessError, RuntimeError, TimeoutError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
