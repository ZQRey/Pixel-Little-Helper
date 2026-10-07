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
STATES = [("idle", 4), ("sleep", 2), ("drag", 2), ("action", 2)] + [(s, 2) for s in ("greeting", "success", "error", "notice", "yawn", "wake", "busy", "dizzy", "lookleft", "lookright", "lookup", "lookdown", "joy", "sad", "surprise", "laugh", "think", "celebrate")]


def sprite(state: str, frame: int) -> Image.Image:
    image = Image.new("RGBA", (48, 48))
    d = ImageDraw.Draw(image)
    bob = (0, 1, 0, -1)[frame] if state == "idle" else -frame * 2 if state in ("joy", "laugh", "celebrate") else 0

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
    if state not in ("idle", "sleep", "drag", "action"):
        box(16, 12, 30, 22, SCREEN)
        dx = -2 if state == "lookleft" else 2 if state == "lookright" else 0
        dy = -2 if state in ("lookup", "notice") else 2 if state == "lookdown" else 0
        for x in (18, 27):
            box(x + dx, 14 + dy, x + dx + 1, 17 + dy, CYAN)
        line([(21, 20), (22, 21), (24, 21), (25, 20)], CYAN)
        if state in ("greeting", "wake", "success"):
            box(34, 27, 39, 35, (0, 0, 0, 0))
            line([(35, 29), (39, 25), (41, 18 + frame * 3)], INK, 4)
            line([(35, 29), (39, 25), (41, 18 + frame * 3)], BLUE, 2)
            box(39, 12 + frame * 3, 44, 19 + frame * 3, INK)
            box(40, 13 + frame * 3, 43, 18 + frame * 3, WHITE)
        if state == "success":
            box(16, 12, 24, 22, SCREEN)
            line([(17, 17), (20, 20), (24, 13)], CYAN, 2)
            box(40, 10, 41, 14, WHITE)
        elif state == "error":
            box(16, 12, 30, 22, SCREEN)
            line([(20, 14), (21, 13), (25, 13), (26, 14), (26, 16), (23, 18)], CYAN)
            box(23, 20, 23, 20, CYAN)
            line([(17, 22), (18, 21), (20, 21)], CYAN)
            line([(26, 21), (28, 21), (29, 22)], CYAN)
        elif state == "notice":
            line([(24, 5), (24, 2)], EDGE, 2)
            box(23, 0, 25, 1, CYAN if frame else WHITE)
        elif state == "yawn":
            box(16, 12, 30, 22, SCREEN)
            line([(17, 15), (20, 15)], CYAN)
            line([(26, 15), (29, 15)], CYAN)
            d.ellipse((21, 17, 25, 22), outline=CYAN)
        elif state == "wake":
            line([(12, 29), (7, 24), (5, 15)], INK, 4)
            line([(12, 29), (7, 24), (5, 15)], WHITE, 2)
            if frame == 0:
                box(16, 12, 30, 22, SCREEN)
                line([(17, 17), (20, 17)], CYAN)
                line([(26, 17), (29, 17)], CYAN)
        elif state == "busy":
            box(16, 12, 30, 22, SCREEN)
            for n in range(frame + 2):
                box(18 + n * 5, 17, 19 + n * 5, 18, CYAN)
        elif state == "dizzy":
            box(16, 12, 30, 22, SCREEN)
            for x in (17, 26):
                line([(x, 14), (x + 3, 14), (x + 3, 18), (x, 18), (x, 16), (x + 1, 16)], CYAN)
            for x, y in ((4 + frame * 3, 4), (40 - frame * 3, 2)):
                line([(x - 2, y), (x + 2, y)], WHITE)
                line([(x, y - 2), (x, y + 2)], CYAN)
        elif state in ("joy", "laugh", "celebrate"):
            box(16, 12, 30, 22, SCREEN)
            for x in (17, 26):
                line([(x, 16), (x + 1, 14), (x + 2, 14), (x + 3, 16)], CYAN)
            if state == "laugh":
                box(21, 18, 25, 22, CYAN)
                box(22, 21, 24, 22, "#ff9cb4")
                box(15, 18, 16, 19, WHITE)
                box(30, 18, 31, 19, WHITE)
            else:
                line([(20, 19), (21, 21), (25, 21), (27, 19)], CYAN)
            if state == "celebrate":
                for x, y, color in ((3, 7, "#ffb85c"), (43, 8, "#ff9cb4"), (7, 19, CYAN), (41, 25, WHITE)):
                    line([(x - 2, y + frame), (x + 2, y + frame)], color)
                    line([(x, y - 2 + frame), (x, y + 2 + frame)], color)
        elif state == "sad":
            box(16, 12, 30, 22, SCREEN)
            for x in (17, 26):
                line([(x, 15 + frame), (x + 3, 17 + frame)], CYAN)
            line([(21, 22), (22, 20), (24, 20), (25, 22)], CYAN)
            box(29, 19 + frame, 29, 20 + frame, WHITE)
        elif state == "surprise":
            box(16, 12, 30, 22, SCREEN)
            for x in (17, 26):
                box(x, 13, x + 2, 17, CYAN)
            d.rectangle((22, 19 + bob, 24, 22 + bob), outline=CYAN)
            line([(24, 5), (24, 2)], EDGE, 2)
            box(23, 0, 25, 1, WHITE if frame else CYAN)
        elif state == "think":
            box(16, 12, 30, 22, SCREEN)
            line([(17, 15), (20, 14)], CYAN)
            box(27, 14, 28, 17, CYAN)
            line([(21, 21), (25, 21)], CYAN)
            line([(36, 29), (32, 26), (31, 23)], INK, 4)
            line([(36, 29), (32, 26), (31, 23)], WHITE, 2)
            line([(40, 5), (41, 3), (44, 3), (45, 5), (43, 7)], CYAN)
            box(43, 9 + frame, 43, 9 + frame, WHITE)
    return image


def main():
    OUT.mkdir(exist_ok=True)
    frames = []
    for state, count in STATES:
        for frame in range(count):
            image = sprite(state, frame)
            image.save(OUT / f"{state}_{frame + 1}.png", optimize=True)
            frames.append((state, frame + 1, image))
    preview = Image.new("RGB", (960, 110 * len(STATES)), "#eaf1f6")
    d = ImageDraw.Draw(preview)
    index = 0
    for row, (state, count) in enumerate(STATES):
        d.text((16, row * 110 + 10), state.upper(), fill=INK)
        for column in range(count):
            _, _, image = frames[index]
            preview.paste(image.resize((96, 96), Image.Resampling.NEAREST), (120 + column * 190, row * 110 + 6), image.resize((96, 96), Image.Resampling.NEAREST))
            index += 1
    preview.save(OUT / "preview.png")
    print(f"Generated {len(frames)} transparent sprites in {OUT}")


if __name__ == "__main__":
    main()
