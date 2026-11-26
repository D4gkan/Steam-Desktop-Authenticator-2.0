"""Wait for native readiness instead of assuming a cold simulator starts in 20 seconds."""

import argparse
import sys
import time
from pathlib import Path


def wait_for_marker(path: Path, marker: str, timeout: float, interval: float = 0.25, exact: bool = False) -> None:
    deadline = time.monotonic() + timeout
    while True:
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
            if "Native Crash Reporting" in text or "SDA startup: FAILED" in text:
                raise RuntimeError("The app crashed during native startup. See the simulator console log.")
            ready = text.strip() == marker if exact else marker in text
            if ready:
                return
        except FileNotFoundError:
            pass
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("Timed out waiting for the native passcode screen to become ready.")
        time.sleep(min(interval, remaining))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path)
    parser.add_argument("--marker", required=True)
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("--exact", action="store_true")
    args = parser.parse_args()
    try:
        wait_for_marker(args.path, args.marker, args.timeout, exact=args.exact)
    except (OSError, RuntimeError, TimeoutError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
    print("Verified native startup readiness.")
