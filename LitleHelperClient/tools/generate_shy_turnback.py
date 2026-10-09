"""
Generate new sprites for PixelHelper:
- turnback_1.png, turnback_2.png (PetState.TurnBack: robot turned with back to user)
- shy_1.png, shy_2.png (PetState.Shy: robot blushing with shy eyes and rosy cheeks)
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

# Face Screen Colors
SCREEN = "#193e5c"
CYAN = "#7cf4ed"
BLUSH_PINK = "#f43f5e"
BLUSH_LIGHT = "#fda4af"

# Robot Body Colors
INK = "#173451"
EDGE = "#397baa"
BLUE = "#66bce7"
ROBOT_WHITE = "#e4f7ff"
ROBOT_LIGHT = "#ffffff"


def draw_base_robot(d: ImageDraw.ImageDraw, bob: int = 0, is_back: bool = False):
    def box(x0, y0, x1, y1, fill):
        d.rectangle((x0, y0 + bob, x1, y1 + bob), fill=fill)

    # Wheel
    box(21, 38, 27, 44, INK)
    box(23, 39, 25, 43, EDGE)

    # Torso
    box(13, 27, 35, 37, INK)
    box(15, 28, 33, 35, BLUE)
    box(17, 28, 31, 31, ROBOT_WHITE)
    box(18, 33, 30, 35, EDGE)
    box(22, 29, 26, 30, ROBOT_LIGHT)

    # Arms
    box(9, 27, 14, 34, INK)
    box(10, 28, 12, 32, ROBOT_WHITE)
    box(11, 33, 15, 36, EDGE)
    box(34, 27, 39, 34, INK)
    box(35, 28, 37, 32, ROBOT_WHITE)

    # Head polygon outline
    polygon = [
        (14, 5), (32, 5), (32, 6), (36, 6), (36, 9), (38, 9),
        (38, 23), (36, 23), (36, 27), (32, 27), (32, 29),
        (14, 29), (14, 27), (10, 27), (10, 23), (8, 23),
        (8, 10), (10, 10), (10, 7), (14, 7)
    ]
    d.polygon([(x, y + bob) for x, y in polygon], fill=INK)

    box(12, 8, 34, 25, EDGE)
    box(14, 7, 31, 9, ROBOT_WHITE)
    box(11, 10, 13, 21, ROBOT_WHITE)
    box(14, 10, 33, 24, BLUE)
    box(34, 11, 35, 22, BLUE)
    box(14, 25, 31, 26, ROBOT_WHITE)
    box(15, 11, 31, 23, INK)
    box(14, 13, 32, 21, INK)

    if not is_back:
        # Screen
        box(16, 12, 30, 22, SCREEN)
    else:
        # Back panel of head (casing armor and seam)
        box(16, 12, 30, 22, BLUE)
        box(18, 13, 28, 21, ROBOT_WHITE)
        box(20, 15, 26, 19, EDGE)
        box(22, 16, 24, 17, CYAN)

    box(12, 8, 14, 9, ROBOT_LIGHT)
    box(15, 8, 19, 8, ROBOT_LIGHT)
    box(10, 23, 12, 25, BLUE)


def generate_turnback_frames():
    for frame in (1, 2):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        bob = 1 if frame == 2 else 0
        draw_base_robot(d, bob=bob, is_back=True)
        path = OUT / f"turnback_{frame}.png"
        img.save(path)
        print(f"Generated {path}")


def generate_shy_frames():
    for frame in (1, 2):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        bob = 1 if frame == 2 else 0
        draw_base_robot(d, bob=bob, is_back=False)

        def line(points, fill=CYAN, width=1):
            d.line([(x, y + bob) for x, y in points], fill=fill, width=width)

        # Cheeks with cute pink blush
        d.rectangle((17, 18 + bob, 19, 19 + bob), fill=BLUSH_PINK)
        d.rectangle((27, 18 + bob, 29, 19 + bob), fill=BLUSH_PINK)
        d.point((18, 18 + bob), fill=BLUSH_LIGHT)
        d.point((28, 18 + bob), fill=BLUSH_LIGHT)

        # Shy eyes: curved or gently closed with eyelashes
        if frame == 1:
            line([(17, 16), (18, 15), (19, 15), (20, 16)], CYAN)
            line([(26, 16), (27, 15), (28, 15), (29, 16)], CYAN)
            line([(22, 20), (23, 21), (24, 20)], CYAN)
        else:
            line([(17, 16), (18, 16), (19, 16), (20, 16)], CYAN)
            line([(26, 16), (27, 16), (28, 16), (29, 16)], CYAN)
            line([(22, 20), (23, 20), (24, 20)], CYAN)

        path = OUT / f"shy_{frame}.png"
        img.save(path)
        print(f"Generated {path}")


if __name__ == "__main__":
    generate_turnback_frames()
    generate_shy_frames()
