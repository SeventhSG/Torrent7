"""Draws every raster Torrent7 ships: the window and tray icon, the Linux desktop icon and
the README icon.

The mark is a 7 whose stem turns into a download arrow, off-black on an amber tile. The
outline is the same polygon as MarkPath in Themes/Tokens.axaml, on a 100 by 100 grid. Run
from the repository root:

    python build/make-assets.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Torrent7" / "Assets"

AMBER_TOP = (242, 172, 66)
AMBER_BOTTOM = (219, 140, 36)
INK = (17, 18, 21)

# One outline: bar, stem, arrowhead.
MARK = [(25, 22), (75, 22), (75, 37), (53.5, 63), (62.5, 63), (46, 83),
        (29.5, 63), (38.5, 63), (60, 37), (25, 37)]

SS = 4  # supersampling, for clean edges at every size


def draw(size: int) -> Image.Image:
    big = size * SS
    tile = Image.new("RGBA", (big, big))
    g = ImageDraw.Draw(tile)
    for y in range(big):
        t = y / (big - 1)
        g.line([(0, y), (big, y)], fill=tuple(round(AMBER_TOP[i] * (1 - t) + AMBER_BOTTOM[i] * t) for i in range(3)) + (255,))

    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    mask = Image.new("L", (big, big), 0)
    # Tiny icons get squarer corners and a bigger mark, so the 7 stays legible.
    radius = 0.18 if size <= 24 else 0.235
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, big - 1, big - 1), radius=big * radius, fill=255)
    img.paste(tile, (0, 0), mask)

    scale, offset = (1.12, -6) if size <= 24 else (1.0, 0)
    pts = [((x * scale + offset) * big / 100, (y * scale + offset) * big / 100) for x, y in MARK]
    ImageDraw.Draw(img).polygon(pts, fill=INK)
    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [draw(s) for s in sizes]
    frames[-1].save(ASSETS / "Torrent7.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    draw(256).save(ASSETS / "Torrent7-256.png")
    draw(512).save(ROOT / "assets" / "icon-512.png")
    print("wrote", ASSETS / "Torrent7.ico", ASSETS / "Torrent7-256.png", ROOT / "assets" / "icon-512.png")


if __name__ == "__main__":
    main()
