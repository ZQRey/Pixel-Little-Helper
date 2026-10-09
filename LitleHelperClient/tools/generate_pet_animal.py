"""
Generate pixel art sprites for PixelHelper:
- petcat_1.png, petcat_2.png (PetState.PetCat: robot stroking a cute kitten)
- petdog_1.png, petdog_2.png (PetState.PetDog: robot patting a happy puppy)
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

# Colors
SCREEN = "#193e5c"
CYAN = "#7cf4ed"
HEART_RED = "#f43f5e"
HEART_PINK = "#fda4af"

INK = "#173451"
EDGE = "#397baa"
BLUE = "#66bce7"
ROBOT_WHITE = "#e4f7ff"
ROBOT_LIGHT = "#ffffff"

# Cat Colors (Ginger & Cream)
CAT_DARK = "#9a3412"
CAT_ORANGE = "#ea580c"
CAT_LIGHT = "#fed7aa"
CAT_WHITE = "#fffbeb"
CAT_PINK = "#fb7185"

# Dog Colors (Golden Caramel & Cream)
DOG_DARK = "#78350f"
DOG_GOLD = "#d97706"
DOG_LIGHT = "#fde68a"
DOG_WHITE = "#fef3c7"
DOG_TONGUE = "#f43f5e"
DOG_NOSE = "#1c1917"


def draw_robot(d: ImageDraw.ImageDraw, bob: int = 0, arm_pose: str = "pet_left"):
    def box(x0, y0, x1, y1, fill):
        d.rectangle((x0, y0 + bob, x1, y1 + bob), fill=fill)

    # Wheel
    box(23, 38, 29, 44, INK)
    box(25, 39, 27, 43, EDGE)

    # Torso
    box(16, 27, 36, 37, INK)
    box(18, 28, 34, 35, BLUE)
    box(20, 28, 33, 31, ROBOT_WHITE)
    box(20, 33, 32, 35, EDGE)
    box(24, 29, 28, 30, ROBOT_LIGHT)

    # Right arm (resting on side)
    box(35, 27, 40, 34, INK)
    box(36, 28, 38, 32, ROBOT_WHITE)

    # Left petting arm (extends towards the animal on the left)
    if arm_pose == "reach_down":
        box(10, 28, 17, 33, INK)
        box(11, 29, 15, 31, ROBOT_WHITE)
        box(7, 32, 13, 37, INK)
        box(8, 33, 11, 36, BLUE)
        box(6, 36, 10, 39, EDGE)  # Hand patting head
    else:  # "reach_stroke"
        box(11, 27, 17, 32, INK)
        box(12, 28, 15, 30, ROBOT_WHITE)
        box(8, 31, 14, 36, INK)
        box(9, 32, 12, 35, BLUE)
        box(8, 35, 12, 38, EDGE)  # Hand stroking back

    # Head polygon
    polygon = [
        (16, 5), (34, 5), (34, 6), (38, 6), (38, 9), (40, 9),
        (40, 23), (38, 23), (38, 27), (34, 27), (34, 29),
        (16, 29), (16, 27), (12, 27), (12, 23), (10, 23),
        (10, 10), (12, 10), (12, 7), (16, 7)
    ]
    d.polygon([(x, y + bob) for x, y in polygon], fill=INK)

    box(14, 8, 36, 25, EDGE)
    box(16, 7, 33, 9, ROBOT_WHITE)
    box(13, 10, 15, 21, ROBOT_WHITE)
    box(16, 10, 35, 24, BLUE)
    box(36, 11, 37, 22, BLUE)
    box(16, 25, 33, 26, ROBOT_WHITE)
    box(17, 11, 33, 23, INK)
    box(16, 13, 34, 21, INK)

    # Screen
    box(18, 12, 32, 22, SCREEN)
    box(14, 8, 16, 9, ROBOT_LIGHT)
    box(17, 8, 21, 8, ROBOT_LIGHT)
    box(12, 23, 14, 25, BLUE)

    # Happy Face (^ . ^)
    # Left eye curve
    d.point((21, 15 + bob), fill=CYAN)
    d.point((22, 14 + bob), fill=CYAN)
    d.point((23, 15 + bob), fill=CYAN)

    # Right eye curve
    d.point((27, 15 + bob), fill=CYAN)
    d.point((28, 14 + bob), fill=CYAN)
    d.point((29, 15 + bob), fill=CYAN)

    # Cute smile
    d.point((24, 18 + bob), fill=CYAN)
    d.point((25, 19 + bob), fill=CYAN)
    d.point((26, 18 + bob), fill=CYAN)

    # Rosy cheeks
    box(19, 17, 20, 18, HEART_PINK)
    box(30, 17, 31, 18, HEART_PINK)


def draw_cat(d: ImageDraw.ImageDraw, frame: int):
    # Kitten sitting curled happily at (x: 2..15, y: 32..44)
    def box(x0, y0, x1, y1, fill):
        d.rectangle((x0, y0, x1, y1), fill=fill)

    # Outline
    box(4, 33, 14, 43, CAT_DARK)

    # Cat Body
    box(5, 34, 13, 42, CAT_ORANGE)
    box(7, 36, 12, 41, CAT_LIGHT)
    box(8, 39, 11, 42, CAT_WHITE)  # White chest/paws

    # Cat Ears
    d.point((4, 32), fill=CAT_DARK)
    d.point((5, 32), fill=CAT_DARK)
    d.point((5, 33), fill=CAT_PINK)
    d.point((9, 32), fill=CAT_DARK)
    d.point((10, 32), fill=CAT_DARK)
    d.point((9, 33), fill=CAT_PINK)

    # Cat Face (closed happy purring eyes ^ ^)
    d.point((6, 35), fill=CAT_DARK)
    d.point((8, 35), fill=CAT_DARK)
    d.point((7, 36), fill=CAT_PINK)  # nose

    # Little whiskers
    d.point((4, 36), fill=CAT_WHITE)
    d.point((10, 36), fill=CAT_WHITE)

    # Tail (swishes between frames)
    if frame == 1:
        d.line([(4, 40), (2, 38), (1, 35)], fill=CAT_DARK, width=1)
        d.line([(4, 41), (3, 39), (2, 36)], fill=CAT_ORANGE, width=1)
        # Small purr heart
        d.point((7, 28), fill=HEART_RED)
        d.point((9, 28), fill=HEART_RED)
        box(7, 29, 9, 29, HEART_RED)
        d.point((8, 30), fill=HEART_RED)
    else:
        d.line([(4, 41), (2, 40), (1, 38)], fill=CAT_DARK, width=1)
        d.line([(4, 42), (3, 41), (2, 39)], fill=CAT_ORANGE, width=1)
        # Musical purr note
        d.point((8, 27), fill=CYAN)
        d.point((8, 28), fill=CYAN)
        d.point((9, 27), fill=CYAN)
        d.point((7, 29), fill=CYAN)


def draw_dog(d: ImageDraw.ImageDraw, frame: int):
    # Puppy sitting happily at (x: 2..15, y: 31..44)
    def box(x0, y0, x1, y1, fill):
        d.rectangle((x0, y0, x1, y1), fill=fill)

    # Outline
    box(3, 32, 14, 43, DOG_DARK)

    # Dog Body
    box(4, 33, 13, 42, DOG_GOLD)
    box(6, 35, 12, 41, DOG_LIGHT)
    box(7, 38, 11, 42, DOG_WHITE)  # White chest

    # Floppy ears
    box(3, 32, 4, 37, DOG_DARK)
    box(10, 32, 11, 37, DOG_DARK)

    # Eyes (Frame 1: happy dots, Frame 2: happy arcs ^ ^)
    if frame == 1:
        d.point((6, 34), fill=DOG_NOSE)
        d.point((8, 34), fill=DOG_NOSE)
    else:
        d.point((5, 34), fill=DOG_DARK)
        d.point((6, 33), fill=DOG_DARK)
        d.point((8, 33), fill=DOG_DARK)
        d.point((9, 34), fill=DOG_DARK)

    # Nose & open happy mouth with tongue!
    d.point((7, 35), fill=DOG_NOSE)
    d.point((7, 36), fill=DOG_TONGUE)
    d.point((7, 37), fill=DOG_TONGUE)

    # Wagging tail
    if frame == 1:
        d.line([(3, 39), (1, 37), (1, 34)], fill=DOG_DARK, width=2)
        d.line([(3, 39), (2, 37), (2, 35)], fill=DOG_GOLD, width=1)
        # Wag sparkle
        d.point((1, 32), fill=DOG_LIGHT)
    else:
        d.line([(3, 40), (1, 40), (0, 38)], fill=DOG_DARK, width=2)
        d.line([(3, 41), (2, 40), (1, 39)], fill=DOG_GOLD, width=1)
        # Wag sparkle
        d.point((0, 36), fill=DOG_LIGHT)
        d.point((6, 28), fill=CYAN)
        d.point((7, 27), fill=CYAN)
        d.point((8, 28), fill=CYAN)
        d.point((7, 29), fill=CYAN)


def generate():
    for f in (1, 2):
        # Pet Cat
        img_cat = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d_cat = ImageDraw.Draw(img_cat)
        arm = "reach_down" if f == 1 else "reach_stroke"
        draw_robot(d_cat, bob=0 if f == 1 else 1, arm_pose=arm)
        draw_cat(d_cat, f)
        cat_path = OUT / f"petcat_{f}.png"
        img_cat.save(cat_path)
        print(f"Generated {cat_path}")

        # Pet Dog
        img_dog = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d_dog = ImageDraw.Draw(img_dog)
        arm = "reach_down" if f == 1 else "reach_stroke"
        draw_robot(d_dog, bob=0 if f == 1 else 1, arm_pose=arm)
        draw_dog(d_dog, f)
        dog_path = OUT / f"petdog_{f}.png"
        img_dog.save(dog_path)
        print(f"Generated {dog_path}")


if __name__ == "__main__":
    generate()
