"""
Generate new sprites for PixelHelper:
- flower_1.png, flower_2.png (PetState.Flower: robot sniffing flower and being happy)
- cry_1.png, cry_2.png (PetState.Cry: robot crying when hurt/insulted)
- hat_birthday.png (Birthday party cone hat overlay for birthday mode)
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

# Face Screen Colors
SCREEN = "#193e5c"
WHITE = "#ffffff"
CYAN = "#7cf4ed"
TEAR_BLUE = "#38bdf8"
TEAR_LIGHT = "#bae6fd"

# Robot Body Colors
INK = "#173451"
EDGE = "#397baa"
BLUE = "#66bce7"
ROBOT_WHITE = "#e4f7ff"
ROBOT_LIGHT = "#ffffff"

# Flower Colors
STEM_GREEN = "#16a34a"
STEM_LIGHT = "#4ade80"
PETAL_PINK = "#f43f5e"
PETAL_LIGHT = "#fda4af"
FLOWER_CENTER = "#facc15"
POT_BROWN = "#92400e"

# Party Hat Colors
HAT_RED = "#ef4444"
HAT_YELLOW = "#facc15"
HAT_CYAN = "#06b6d4"
HAT_WHITE = "#f8fafc"


def draw_base_robot(d: ImageDraw.ImageDraw, bob: int = 0):
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
    box(16, 12, 30, 22, SCREEN)
    box(12, 8, 14, 9, ROBOT_LIGHT)
    box(15, 8, 19, 8, ROBOT_LIGHT)
    box(10, 23, 12, 25, BLUE)


def generate_flower_frames():
    for frame in (1, 2):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        bob = 1 if frame == 1 else 0
        draw_base_robot(d, bob=bob)

        # Flower Pot on right side (x: 35-43, y: 35-43)
        d.rectangle((36, 37, 43, 44), fill=INK)
        d.rectangle((37, 38, 42, 43), fill=POT_BROWN)
        d.rectangle((35, 36, 44, 38), fill=INK)
        d.rectangle((36, 37, 43, 37), fill="#b45309")

        # Stem curving towards robot
        stem_points = [(40, 36), (39, 33), (38, 30), (37, 27), (36, 25)]
        for pt in stem_points:
            d.rectangle((pt[0]-1, pt[1], pt[0], pt[1]+1), fill=STEM_GREEN)
        # Leaf
        d.point((41, 32), fill=STEM_LIGHT)
        d.point((42, 31), fill=STEM_GREEN)

        # Flower Blossom (around 35, 23)
        fx, fy = (34, 23) if frame == 1 else (35, 22)
        # Petals
        for dx, dy in [(-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, -1), (-1, 1), (1, 1)]:
            d.rectangle((fx+dx, fy+dy, fx+dx+1, fy+dy+1), fill=PETAL_PINK)
        # Center
        d.rectangle((fx, fy, fx+1, fy+1), fill=FLOWER_CENTER)
        d.point((fx, fy), fill="#fef08a")

        # Scent sparkles floating
        if frame == 2:
            d.point((30, 18), fill=PETAL_LIGHT)
            d.point((28, 16), fill=FLOWER_CENTER)
            d.point((32, 14), fill=PETAL_LIGHT)

        # Robot Face: Happy curved eyes (sniffing and smiling)
        # Eyes at y=15,16,17 on screen (x=16..30)
        # Happy eyes "^ ^"
        d.point((19, 15 + bob), fill=CYAN)
        d.point((20, 14 + bob), fill=CYAN)
        d.point((21, 15 + bob), fill=CYAN)

        d.point((25, 15 + bob), fill=CYAN)
        d.point((26, 14 + bob), fill=CYAN)
        d.point((27, 15 + bob), fill=CYAN)

        # Cute pink blush on cheeks
        d.point((18, 18 + bob), fill="#fda4af")
        d.point((28, 18 + bob), fill="#fda4af")

        # Smiling mouth
        d.rectangle((22, 19 + bob, 24, 19 + bob), fill=CYAN)
        d.point((21, 18 + bob), fill=CYAN)
        d.point((25, 18 + bob), fill=CYAN)

        # Arms: reaching gently towards flower
        d.rectangle((31, 28 + bob, 35, 30 + bob), fill=INK)
        d.rectangle((32, 29 + bob, 34, 29 + bob), fill=ROBOT_WHITE)

        # Left arm relaxed
        d.rectangle((8, 28 + bob, 12, 33 + bob), fill=INK)
        d.rectangle((9, 29 + bob, 11, 32 + bob), fill=ROBOT_WHITE)

        img.save(OUT / f"flower_{frame}.png", optimize=True)
        print(f"Generated flower_{frame}.png")


def generate_cry_frames():
    for frame in (1, 2):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        bob = 1 if frame == 2 else 0
        draw_base_robot(d, bob=bob)

        # Face screen eyes: sad trembling eyes (> <)
        # Left eye ">"
        d.point((18, 14 + bob), fill=TEAR_BLUE)
        d.point((19, 15 + bob), fill=TEAR_BLUE)
        d.point((20, 16 + bob), fill=WHITE)
        d.point((19, 17 + bob), fill=TEAR_BLUE)
        d.point((18, 18 + bob), fill=TEAR_BLUE)

        # Right eye "<"
        d.point((28, 14 + bob), fill=TEAR_BLUE)
        d.point((27, 15 + bob), fill=TEAR_BLUE)
        d.point((26, 16 + bob), fill=WHITE)
        d.point((27, 17 + bob), fill=TEAR_BLUE)
        d.point((28, 18 + bob), fill=TEAR_BLUE)

        # Trembling wavy mouth
        d.point((21, 20 + bob), fill=TEAR_BLUE)
        d.point((22, 19 + bob), fill=TEAR_BLUE)
        d.point((23, 20 + bob), fill=TEAR_BLUE)
        d.point((24, 19 + bob), fill=TEAR_BLUE)
        d.point((25, 20 + bob), fill=TEAR_BLUE)

        # Tear streams dripping down screen and body
        if frame == 1:
            # Tear stream 1
            d.rectangle((18, 19 + bob, 19, 23 + bob), fill=TEAR_BLUE)
            d.point((18, 22 + bob), fill=TEAR_LIGHT)
            # Tear stream 2
            d.rectangle((27, 19 + bob, 28, 23 + bob), fill=TEAR_BLUE)
            d.point((28, 22 + bob), fill=TEAR_LIGHT)
            # Droplet falling
            d.point((18, 26 + bob), fill=TEAR_BLUE)
            d.point((28, 28 + bob), fill=TEAR_BLUE)
        else:
            # Larger tear streams
            d.rectangle((17, 18 + bob, 19, 24 + bob), fill=TEAR_BLUE)
            d.rectangle((27, 18 + bob, 29, 24 + bob), fill=TEAR_BLUE)
            d.point((18, 20 + bob), fill=WHITE)
            d.point((28, 20 + bob), fill=WHITE)
            # Droplets dripping on torso
            d.rectangle((17, 26 + bob, 18, 28 + bob), fill=TEAR_BLUE)
            d.rectangle((28, 27 + bob, 29, 30 + bob), fill=TEAR_BLUE)
            d.point((17, 32 + bob), fill=TEAR_LIGHT)

        # Arms: Raised hands wiping tears / covering face
        # Left arm up
        d.rectangle((10, 20 + bob, 15, 27 + bob), fill=INK)
        d.rectangle((11, 21 + bob, 14, 26 + bob), fill=ROBOT_WHITE)
        # Right arm up
        d.rectangle((31, 20 + bob, 36, 27 + bob), fill=INK)
        d.rectangle((32, 21 + bob, 35, 26 + bob), fill=ROBOT_WHITE)

        img.save(OUT / f"cry_{frame}.png", optimize=True)
        print(f"Generated cry_{frame}.png")


def generate_birthday_hat():
    # 24x20 RGBA party hat overlay to be placed on head (around top x: 14-32, y: 0-10)
    img = Image.new("RGBA", (24, 20), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Top Pom-pom at (11, 1), (12, 1), (11, 2), (12, 2)
    d.rectangle((10, 0, 13, 3), fill=INK)
    d.rectangle((11, 1, 12, 2), fill=HAT_YELLOW)
    d.point((11, 1), fill="#ffffff")

    # Cone Hat: triangle from (11, 3) down to (4, 18) - (19, 18)
    # Striped pattern: Red, Yellow, Cyan, White
    stripes = [
        # y: 3..4 (width 3)
        (3, 10, 13, HAT_RED),
        (4, 10, 13, HAT_RED),
        # y: 5..6 (width 5)
        (5, 9, 14, HAT_YELLOW),
        (6, 9, 14, HAT_YELLOW),
        # y: 7..8 (width 7)
        (7, 8, 15, HAT_CYAN),
        (8, 8, 15, HAT_CYAN),
        # y: 9..10 (width 9)
        (9, 7, 16, HAT_WHITE),
        (10, 7, 16, HAT_WHITE),
        # y: 11..12 (width 11)
        (11, 6, 17, HAT_RED),
        (12, 6, 17, HAT_RED),
        # y: 13..14 (width 13)
        (13, 5, 18, HAT_YELLOW),
        (14, 5, 18, HAT_YELLOW),
        # y: 15..16 (width 15)
        (15, 4, 19, HAT_CYAN),
        (16, 4, 19, HAT_CYAN),
        # y: 17..18 (width 17) rim
        (17, 3, 20, HAT_WHITE),
        (18, 3, 20, HAT_WHITE),
    ]

    for y, x0, x1, col in stripes:
        # border outline
        d.line([(x0 - 1, y), (x1 + 1, y)], fill=INK)
        d.line([(x0, y), (x1, y)], fill=col)

    # Base fringe pom-poms
    for bx in [4, 7, 10, 13, 16, 19]:
        d.point((bx, 19), fill=HAT_YELLOW)

    img.save(OUT / "hat_birthday.png", optimize=True)
    print("Generated hat_birthday.png")


if __name__ == "__main__":
    generate_flower_frames()
    generate_cry_frames()
    generate_birthday_hat()
