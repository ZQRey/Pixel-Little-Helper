"""Generate face badge overlays (15x11 RGBA) for robot screen."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

SCREEN = "#193e5c"
GREEN_GLOW = "#10b981"
GREEN_CORE = "#34d399"
RED_GLOW = "#ef4444"
RED_CORE = "#f87171"

# 15x11 pixel matrix for the robot's face screen
WIDTH, HEIGHT = 15, 11


def make_badge():
    img = Image.new("RGBA", (WIDTH, HEIGHT), SCREEN)
    d = ImageDraw.Draw(img)
    return img, d


def generate_connection_badges():
    # 1. Connected: Green exclamation mark '!'
    img, d = make_badge()
    # Exclamation bar
    d.rectangle((7, 2, 8, 6), fill=GREEN_CORE)
    d.point((6, 2), fill=GREEN_GLOW)
    d.point((9, 2), fill=GREEN_GLOW)
    d.point((6, 6), fill=GREEN_GLOW)
    d.point((9, 6), fill=GREEN_GLOW)
    # Exclamation dot
    d.rectangle((7, 8, 8, 9), fill=GREEN_CORE)
    img.save(OUT / "face_connected.png", optimize=True)
    print("Generated face_connected.png")

    # 2. Disconnected: Red cross mark 'X'
    img, d = make_badge()
    points = [
        (4, 2), (5, 2), (10, 2), (11, 2),
        (5, 3), (6, 3), (9, 3), (10, 3),
        (6, 4), (7, 4), (8, 4), (9, 4),
        (7, 5), (8, 5),
        (6, 6), (7, 6), (8, 6), (9, 6),
        (5, 7), (6, 7), (9, 7), (10, 7),
        (4, 8), (5, 8), (10, 8), (11, 8),
        (4, 9), (5, 9), (10, 9), (11, 9)
    ]
    for pt in points:
        d.point(pt, fill=RED_CORE)
    img.save(OUT / "face_disconnected.png", optimize=True)
    print("Generated face_disconnected.png")


# 5x7 pixel font digits
DIGITS = {
    '1': [
        "..#..",
        ".##..",
        "..#..",
        "..#..",
        "..#..",
        "..#..",
        ".###."
    ],
    '2': [
        ".###.",
        "#...#",
        "....#",
        "..##.",
        ".#...",
        "#....",
        "#####"
    ],
    '3': [
        "#####",
        "....#",
        "...#.",
        "..##.",
        "....#",
        "#...#",
        ".###."
    ],
    '4': [
        "...#.",
        "..##.",
        ".#.#.",
        "#..#.",
        "#####",
        "...#.",
        "...#."
    ],
    '5': [
        "#####",
        "#....",
        "####.",
        "....#",
        "....#",
        "#...#",
        ".###."
    ],
    '6': [
        "..##.",
        ".#...",
        "#....",
        "####.",
        "#...#",
        "#...#",
        ".###."
    ],
    '7': [
        "#####",
        "....#",
        "...#.",
        "..#..",
        ".#...",
        ".#...",
        ".#..."
    ],
    '8': [
        ".###.",
        "#...#",
        "#...#",
        ".###.",
        "#...#",
        "#...#",
        ".###."
    ],
    '9': [
        ".###.",
        "#...#",
        "#...#",
        ".####",
        "....#",
        "...#.",
        ".##.."
    ]
}


def generate_unread_badges():
    # 1..9
    for num_str, rows in DIGITS.items():
        img, d = make_badge()
        start_x = (WIDTH - 5) // 2  # centered (5)
        start_y = 2
        for r, row in enumerate(rows):
            for c, char in enumerate(row):
                if char == '#':
                    d.point((start_x + c, start_y + r), fill=RED_CORE)
        img.save(OUT / f"face_unread_{num_str}.png", optimize=True)
        print(f"Generated face_unread_{num_str}.png")

    # 9+
    img, d = make_badge()
    # 4x7 compact '9'
    compact_9 = [
        "###",
        "#.#",
        "###",
        "..#",
        "..#",
        ".#.",
        "##."
    ]
    # start_x = 2
    for r, row in enumerate(compact_9):
        for c, char in enumerate(row):
            if char == '#':
                d.point((2 + c, 2 + r), fill=RED_CORE)
    # '+' sign at x=7..11, y=4..6
    d.point((9, 4), fill=RED_CORE)
    d.rectangle((7, 5, 11, 5), fill=RED_CORE)
    d.point((9, 6), fill=RED_CORE)
    img.save(OUT / "face_unread_plus.png", optimize=True)
    print("Generated face_unread_plus.png")


if __name__ == "__main__":
    generate_connection_badges()
    generate_unread_badges()
