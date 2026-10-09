"""Generate face heart overlay badges (15x11) and facepalm robot sprites (48x48)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

# Face Screen Colors
SCREEN = "#193e5c"
RED_CORE = "#f43f5e"
RED_GLOW = "#ef4444"
PINK_LIGHT = "#fda4af"
WHITE = "#ffffff"

# Robot Colors
INK = "#173451"
EDGE = "#397baa"
BLUE = "#66bce7"
ROBOT_WHITE = "#e4f7ff"
ROBOT_LIGHT = "#ffffff"
CYAN = "#7cf4ed"


def generate_face_heart_badges():
    # 15x11 pixel matrix for the robot's face screen

    # Frame 1: Small pulsing heart (5x5)
    img1 = Image.new("RGBA", (15, 11), SCREEN)
    d1 = ImageDraw.Draw(img1)
    # Heart 1
    # .#.#.
    # #####
    # #####
    # .###.
    # ..#..
    h1 = [
        ".#.#.",
        "#####",
        "#####",
        ".###.",
        "..#.."
    ]
    for r, row in enumerate(h1):
        for c, ch in enumerate(row):
            if ch == '#':
                d1.point((5 + c, 3 + r), fill=RED_CORE)
    # Highlights
    d1.point((6, 4), fill=PINK_LIGHT)
    img1.save(OUT / "face_heart_1.png", optimize=True)
    print("Generated face_heart_1.png")

    # Frame 2: Medium pulsing heart (7x6)
    img2 = Image.new("RGBA", (15, 11), SCREEN)
    d2 = ImageDraw.Draw(img2)
    h2 = [
        ".##.##.",
        "#######",
        "#######",
        ".#####.",
        "..###..",
        "...#..."
    ]
    for r, row in enumerate(h2):
        for c, ch in enumerate(row):
            if ch == '#':
                d2.point((4 + c, 2 + r), fill=RED_GLOW)
    # Inner brighter core
    d2.point((5, 3), fill=PINK_LIGHT)
    d2.point((6, 3), fill=WHITE)
    d2.point((8, 3), fill=PINK_LIGHT)
    d2.point((6, 4), fill=PINK_LIGHT)
    d2.point((7, 4), fill=PINK_LIGHT)
    img2.save(OUT / "face_heart_2.png", optimize=True)
    print("Generated face_heart_2.png")

    # Frame 3: Large blooming heart (9x7)
    img3 = Image.new("RGBA", (15, 11), SCREEN)
    d3 = ImageDraw.Draw(img3)
    h3 = [
        ".###.###.",
        "#########",
        "#########",
        "#########",
        ".#######.",
        "..#####..",
        "...###...",
        "....#...."
    ]
    for r, row in enumerate(h3):
        for c, ch in enumerate(row):
            if ch == '#':
                d3.point((3 + c, 1 + r), fill=RED_GLOW)
    # Highlights & sparkles
    d3.point((4, 2), fill=WHITE)
    d3.point((5, 2), fill=PINK_LIGHT)
    d3.point((4, 3), fill=PINK_LIGHT)
    d3.point((8, 2), fill=PINK_LIGHT)
    d3.point((9, 2), fill=WHITE)
    # Tiny sparkle corners
    d3.point((1, 1), fill=PINK_LIGHT)
    d3.point((13, 1), fill=PINK_LIGHT)
    img3.save(OUT / "face_heart_3.png", optimize=True)
    print("Generated face_heart_3.png")

    # Frame 4: Radiant full heart (soft aura)
    img4 = Image.new("RGBA", (15, 11), SCREEN)
    d4 = ImageDraw.Draw(img4)
    # Outer glow
    for r, row in enumerate(h3):
        for c, ch in enumerate(row):
            if ch == '#':
                d4.point((3 + c, 1 + r), fill=RED_CORE)
    # Glow halo pixels
    d4.point((2, 2), fill="#be123c")
    d4.point((12, 2), fill="#be123c")
    d4.point((7, 9), fill="#be123c")
    # Bright center
    d4.point((5, 3), fill=WHITE)
    d4.point((6, 3), fill=WHITE)
    d4.point((8, 3), fill=WHITE)
    d4.point((9, 3), fill=WHITE)
    d4.point((6, 4), fill=PINK_LIGHT)
    d4.point((7, 4), fill=PINK_LIGHT)
    d4.point((8, 4), fill=PINK_LIGHT)
    img4.save(OUT / "face_heart_4.png", optimize=True)
    print("Generated face_heart_4.png")


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


def generate_facepalm_frames():
    # 2 frames (48x48 RGBA) of facepalm animation
    for frame in (1, 2):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)

        bob = 1 if frame == 2 else 0
        draw_base_robot(d, bob=bob)

        # Left arm (hanging down or on hip)
        d.rectangle((8, 27 + bob, 12, 34 + bob), fill=INK)
        d.rectangle((9, 28 + bob, 11, 33 + bob), fill=ROBOT_WHITE)

        # Face expression:
        # Lowered eyes, closed/strained look, slight frown
        # Frame 1: closed squinting eyes (- -)
        # Frame 2: closed squinting eyes with sweatdrop
        d.line([(18, 16 + bob), (21, 16 + bob)], fill=CYAN)
        d.point((17, 17 + bob), fill=CYAN)
        # Right eye is partially covered by hand
        d.line([(25, 16 + bob), (27, 16 + bob)], fill=CYAN)
        # Frown mouth
        d.line([(21, 20 + bob), (24, 20 + bob)], fill=CYAN)

        if frame == 2:
            # Sweat drop on forehead/temple
            d.rectangle((14, 13 + bob, 15, 15 + bob), fill=CYAN)
            d.point((14, 12 + bob), fill=WHITE)

        # Right arm: raised up to the forehead / facepalm gesture!
        # Arm reaching up across face
        # Upper arm
        d.rectangle((31, 24 + bob, 36, 31 + bob), fill=INK)
        d.rectangle((32, 25 + bob, 35, 30 + bob), fill=BLUE)
        # Forearm angled up to forehead
        d.polygon([
            (26, 16 + bob), (34, 25 + bob), (35, 27 + bob), (28, 20 + bob)
        ], fill=INK)
        d.polygon([
            (27, 17 + bob), (33, 24 + bob), (34, 25 + bob), (28, 19 + bob)
        ], fill=ROBOT_WHITE)

        # Palm covering forehead / upper right side of screen (x=24..29, y=13..18)
        d.rectangle((24, 14 + bob, 29, 18 + bob), fill=INK)
        d.rectangle((25, 15 + bob, 28, 17 + bob), fill=ROBOT_LIGHT)
        # Fingers
        d.point((24, 13 + bob), fill=INK)
        d.point((26, 13 + bob), fill=INK)
        d.point((28, 13 + bob), fill=INK)
        d.point((25, 13 + bob), fill=ROBOT_WHITE)
        d.point((27, 13 + bob), fill=ROBOT_WHITE)

        img.save(OUT / f"facepalm_{frame}.png", optimize=True)
        print(f"Generated facepalm_{frame}.png")


if __name__ == "__main__":
    generate_face_heart_badges()
    generate_facepalm_frames()
