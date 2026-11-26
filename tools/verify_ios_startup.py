"""Reject blank/Home Screen captures without compiling a Swift image-analysis app."""

import sys
from pathlib import Path

from PIL import Image


def verify(path: Path) -> None:
    with Image.open(path) as source:
        screenshot = source.convert("RGB")
    pixels = screenshot.load()
    visible = dark = total = 0
    for y in range(screenshot.height * 12 // 100, screenshot.height * 95 // 100, 8):
        for x in range(0, screenshot.width, 8):
            brightness = max(pixels[x, y]) / 255
            total += 1
            visible += brightness > 0.18
            dark += brightness < 0.18
    if visible <= 40 or dark <= total * 6 // 10:
        raise ValueError("The screenshot is blank or does not show the dark SDA interface.")
    print(f"Verified visible iOS content ({visible} samples; {dark / total:.0%} dark).")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Supply one iOS startup screenshot.")
        verify(Path(sys.argv[1]))
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
