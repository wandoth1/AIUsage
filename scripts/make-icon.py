# Generates src/AIUsage.Windows/AIUsage.ico: AIUsage's own mark (three rising bars on a dark rounded tile),
# the same motif the tray icon draws at runtime. Original artwork; no third-party logos. Requires Pillow.
# Not part of the build: the .ico is committed. Run only to regenerate it:  python scripts/make-icon.py
import os
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
TILE_TOP, TILE_BOTTOM = (25, 33, 52), (15, 20, 33)
BAR_TOP, BAR_BOTTOM = (132, 178, 255), (84, 136, 245)
SCALE = 8  # supersampling


def gradient(size, top, bottom):
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    for y in range(size):
        t = y / max(1, size - 1)
        draw.line([(0, y), (size, y)], fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    return image


def render(size):
    big = size * SCALE
    tile_mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(tile_mask).rounded_rectangle([0, 0, big - 1, big - 1], radius=round(big * 0.22), fill=255)
    icon = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    icon.paste(gradient(big, TILE_TOP, TILE_BOTTOM), (0, 0), tile_mask)
    # Three bars: same proportions at every size; widths snap to whole pixels of the final size for crisp edges.
    unit = big / 32.0
    bar_mask = Image.new("L", (big, big), 0)
    draw = ImageDraw.Draw(bar_mask)
    for x, top in ((6, 18), (13.5, 12), (21, 6)):
        left, right = round(x * unit), round((x + 5) * unit)
        draw.rounded_rectangle([left, round(top * unit), right, round(26 * unit)], radius=max(1, round(1.2 * unit)), fill=255)
    icon.paste(gradient(big, BAR_TOP, BAR_BOTTOM), (0, 0), bar_mask)
    return icon.resize((size, size), Image.LANCZOS)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    target = os.path.join(root, "src", "AIUsage.Windows", "AIUsage.ico")
    images = [render(s) for s in SIZES]
    images[-1].save(target, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    images[-1].save(os.path.join(root, "artifacts", "icon-preview.png")) if os.path.isdir(os.path.join(root, "artifacts")) else None
    print(target, os.path.getsize(target), "bytes")


if __name__ == "__main__":
    main()
