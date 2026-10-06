"""Deterministic hand-drawn 48x48 RGBA pixel robot. Requires Pillow only."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
INK = "#173451"
EDGE = "#397baa"
BLUE = "#66bce7"
WHITE = "#e4f7ff"
LIGHT = "#ffffff"
SCREEN = "#193e5c"
CYAN = "#7cf4ed"


def sprite(state: str, frame: int) -> Image.Image:
    image = Image.new("RGBA", (48, 48))
    d = ImageDraw.Draw(image)
    bob = (0, 1, 0, -1)[frame] if state == "idle" else 0

    def box(x0, y0, x1, y1, fill):
        d.rectangle((x0, y0 + bob, x1, y1 + bob), fill=fill)

    def line(points, fill=INK, width=1):
        d.line([(x, y + bob) for x, y in points], fill=fill, width=width)

    # One wheel, segmented arms, and white/blue torso.
    box(21, 38, 27, 44 if state != "drag" else 42, INK)
    box(23, 39, 25, 43 if state != "drag" else 41, EDGE)
    box(13, 27, 35, 37, INK)
    box(15, 28, 33, 35, BLUE)
    box(17, 28, 31, 31, WHITE)
    box(18, 33, 30, 35, EDGE)
    box(22, 29, 26, 30, LIGHT)
    box(9, 27, 14, 34, INK)
    box(10, 28, 12, 32, WHITE)
    box(11, 33, 15, 36, EDGE)
    box(34, 27, 39, 34, INK)
    box(35, 28, 37, 32, WHITE)
    if state == "action":
        box(36, 22 - frame * 2, 40, 29, INK)
        box(37, 23 - frame * 2, 39, 27, BLUE)
        box(39, 18 - frame * 2, 44, 24, INK)
        box(40, 19 - frame * 2, 43, 23, WHITE)
        line([(43, 11), (45, 9)], CYAN)
        line([(45, 15), (47, 15)], CYAN)

    # Stepped, deliberately non-antialiased outline.
    polygon = [(14, 5), (32, 5), (32, 6), (36, 6), (36, 9), (38, 9),
               (38, 23), (36, 23), (36, 27), (32, 27), (32, 29),
               (14, 29), (14, 27), (10, 27), (10, 23), (8, 23),
               (8, 10), (10, 10), (10, 7), (14, 7)]
    d.polygon([(x, y + bob) for x, y in polygon], fill=INK)
    box(12, 8, 34, 25, EDGE)
    box(14, 7, 31, 9, WHITE)
    box(11, 10, 13, 21, WHITE)
    box(14, 10, 33, 24, BLUE)
    box(34, 11, 35, 22, BLUE)
    box(14, 25, 31, 26, WHITE)
    box(15, 11, 31, 23, INK)
    box(14, 13, 32, 21, INK)
    box(16, 12, 30, 22, SCREEN)
    box(12, 8, 14, 9, LIGHT)
    box(15, 8, 19, 8, LIGHT)
    box(10, 23, 12, 25, BLUE)
    if state == "sleep" or (state == "idle" and frame == 2):
        line([(17, 17), (20, 17)], CYAN)
        line([(26, 17), (29, 17)], CYAN)
    elif state == "drag":
        box(17, 14, 20, 18 + frame, CYAN)
        box(26, 14, 29, 18 + frame, CYAN)
        box(22, 21, 24, 23, CYAN)
    else:
        line([(17, 17), (17, 15), (18, 14), (19, 14), (20, 15), (20, 17)], CYAN)
        if state == "action" and frame == 1:
            line([(26, 16), (28, 17), (30, 16)], CYAN)
        else:
            line([(26, 17), (26, 15), (27, 14), (28, 14), (29, 15), (29, 17)], CYAN)
        line([(21, 20), (22, 21), (24, 21), (25, 20)], CYAN)
    if state == "sleep":
        # Tiny pixel Z and z, outside the head; alpha remains zero between glyphs.
        for x, y, size in [(36, 2 + frame, 4), (42, 0, 3)]:
            d.line([(x, y), (x + size, y), (x, y + size), (x + size, y + size)], fill=EDGE)
    return image


def main():
    OUT.mkdir(exist_ok=True)
    frames = []
    for state, count in [("idle", 4), ("sleep", 2), ("drag", 2), ("action", 2)]:
        for frame in range(count):
            image = sprite(state, frame)
            image.save(OUT / f"{state}_{frame + 1}.png", optimize=True)
            frames.append((state, frame + 1, image))
    preview = Image.new("RGB", (960, 440), "#eaf1f6")
    d = ImageDraw.Draw(preview)
    index = 0
    for row, (state, count) in enumerate([("idle", 4), ("sleep", 2), ("drag", 2), ("action", 2)]):
        d.text((16, row * 110 + 10), state.upper(), fill=INK)
        for column in range(count):
            _, _, image = frames[index]
            preview.paste(image.resize((96, 96), Image.Resampling.NEAREST), (120 + column * 190, row * 110 + 6), image.resize((96, 96), Image.Resampling.NEAREST))
            index += 1
    preview.save(OUT / "preview.png")
    print(f"Generated {len(frames)} transparent sprites in {OUT}")


if __name__ == "__main__":
    main()
