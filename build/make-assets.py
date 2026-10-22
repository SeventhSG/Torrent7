"""Draws every raster Torrent7 ships: the window and tray icon, and the Linux desktop icon.

The mark is the same geometry as MarkSeven and MarkTray in Themes/Tokens.axaml: a 7 whose
stroke lands on a tray, on a 20 by 20 box. Run from the repository root:

    python build/make-assets.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Torrent7" / "Assets"

SURFACE = (25, 23, 20, 255)      # SurfaceColor
LINE = (60, 53, 46, 255)         # LineStrongColor
ACCENT = (232, 163, 61, 255)     # AccentColor
DIM = (154, 145, 136, 255)       # TextDimColor

SS = 4  # supersampling, for clean edges at every size


def draw(size: int) -> Image.Image:
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Small sizes drop the tile border and thicken the strokes, so the 7 still reads at 16 px.
    radius = big * 0.22
    d.rounded_rectangle((0, 0, big - 1, big - 1), radius=radius, fill=SURFACE,
                        outline=None if size <= 24 else LINE, width=max(1, big // 96))

    def p(x: float, y: float) -> tuple[float, float]:
        # The mark's 20 box, inset so it sits in the tile with air around it.
        inset = big * 0.12
        scale = (big - 2 * inset) / 20
        return inset + x * scale, inset + y * scale

    stroke = big * (0.13 if size <= 24 else 0.10)
    seven = [p(5, 4.5), p(15, 4.5), p(9.2, 14.2)]
    d.line(seven, fill=ACCENT, width=round(stroke), joint="curve")
    for pt in (seven[0], seven[-1]):
        r = stroke / 2
        d.ellipse((pt[0] - r, pt[1] - r, pt[0] + r, pt[1] + r), fill=ACCENT)

    tray = [p(4.5, 17), p(15.5, 17)]
    d.line(tray, fill=DIM, width=round(stroke))
    for pt in tray:
        r = stroke / 2
        d.ellipse((pt[0] - r, pt[1] - r, pt[0] + r, pt[1] + r), fill=DIM)

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
