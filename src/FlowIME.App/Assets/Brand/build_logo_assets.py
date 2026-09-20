"""Generate FlowIME raster assets without changing the approved mark geometry.

The authoritative 44 px PNG is recolored pixel-for-pixel. No curve is redrawn,
traced, inferred, or generated.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "Source" / "FlowIME.Mark.Original.44.png"
OUTPUT = ROOT / "Generated"
OUTPUT.mkdir(parents=True, exist_ok=True)

BACKGROUND = np.array((238, 226, 206), dtype=np.float32)  # #EEE2CE light beige
INK = np.array((111, 84, 67), dtype=np.float32)  # #6F5443 dark brown
SIZES = (44, 64, 128, 256, 512, 1024)


def recolor_exact(source: Image.Image) -> Image.Image:
    rgba = np.asarray(source.convert("RGBA"), dtype=np.float32)
    rgb = rgba[..., :3]
    alpha = rgba[..., 3:4]

    # The approved source consists of a brown tile and one light continuous
    # waveform. Use source luminance only to replace those two colors while
    # retaining every source pixel, edge, antialias value, and alpha value.
    luminance = rgb.min(axis=2, keepdims=True)
    waveform = np.clip((luminance - 105.0) / 115.0, 0.0, 1.0)
    recolored = BACKGROUND * (1.0 - waveform) + INK * waveform
    result = np.concatenate((recolored, alpha), axis=2).round().astype(np.uint8)
    return Image.fromarray(result, "RGBA")


def main() -> None:
    approved = Image.open(SOURCE)
    if approved.size != (44, 44):
        raise ValueError(f"Expected the approved 44x44 mark, got {approved.size}")

    exact = recolor_exact(approved)
    exact.save(OUTPUT / "FlowIME.Mark.44.png", optimize=True)

    for size in SIZES[1:]:
        exact.resize((size, size), Image.Resampling.LANCZOS).save(
            OUTPUT / f"FlowIME.Mark.{size}.png",
            optimize=True,
        )

    icon_master = exact.resize((256, 256), Image.Resampling.LANCZOS)
    icon_master.save(
        OUTPUT / "FlowIME.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    main()
