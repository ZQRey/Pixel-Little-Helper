"""Generate workout and charging sprites for PixelHelper robot (48x48 RGBA)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

INK = "#173451"
EDGE = "#397baa"
BLUE = "#66bce7"
WHITE = "#e4f7ff"
LIGHT = "#ffffff"
SCREEN = "#193e5c"
CYAN = "#7cf4ed"
RED_HEADBAND = "#ef4444"
DUMBBELL_BAR = "#94a3b8"
DUMBBELL_WEIGHT = "#334155"
GREEN_BATTERY = "#22c55e"
YELLOW_LIGHTNING = "#facc15"


def draw_base_robot(d: ImageDraw.ImageDraw, bob: int = 0, leg_squat: int = 0):
    def box(x0, y0, x1, y1, fill):
        d.rectangle((x0, y0 + bob, x1, y1 + bob), fill=fill)

    # Wheel (compacted if squatting)
    wheel_bottom = 44 - leg_squat
    box(21, 38 + leg_squat, 27, wheel_bottom, INK)
    box(23, 39 + leg_squat, 25, wheel_bottom - 1, EDGE)

    # Torso
    box(13, 27, 35, 37, INK)
    box(15, 28, 33, 35, BLUE)
    box(17, 28, 31, 31, WHITE)
    box(18, 33, 30, 35, EDGE)
    box(22, 29, 26, 30, LIGHT)

    # Head polygon outline
    polygon = [
        (14, 5), (32, 5), (32, 6), (36, 6), (36, 9), (38, 9),
        (38, 23), (36, 23), (36, 27), (32, 27), (32, 29),
        (14, 29), (14, 27), (10, 27), (10, 23), (8, 23),
        (8, 10), (10, 10), (10, 7), (14, 7)
    ]
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


def generate_workout_frames():
    # 8 frames: athletic workout with red headband, dumbbells, squats and energetic face
    for frame in range(1, 9):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)

        # bob and squat progression
        # frames 1-4: dumbbell curls / presses standing
        # frames 5-7: squats with dumbbells
        # frame 8: victory lift
        bob = 0
        squat = 0
        arm_mode = 0  # 0: shoulders, 1: overhead press, 2: squat low, 3: triumph

        if frame in (1, 3):
            bob = 0
            arm_mode = 0
        elif frame in (2, 4):
            bob = -1
            arm_mode = 1  # press up
        elif frame == 5:
            bob = 1
            squat = 1
            arm_mode = 2  # squatting
        elif frame == 6:
            bob = 2
            squat = 2
            arm_mode = 2  # deep squat
        elif frame == 7:
            bob = 1
            squat = 1
            arm_mode = 0  # rising
        elif frame == 8:
            bob = -1
            squat = 0
            arm_mode = 3  # victory triumph

        draw_base_robot(d, bob=bob, leg_squat=squat)

        # Red headband across the forehead
        d.rectangle((14, 7 + bob, 32, 9 + bob), fill=RED_HEADBAND)
        # Headband knot on the left side
        d.rectangle((10, 8 + bob, 13, 10 + bob), fill=RED_HEADBAND)

        # Face expressions
        if arm_mode == 1:
            # Resolute focused eyes
            d.line([(18, 16 + bob), (21, 16 + bob)], fill=CYAN)
            d.line([(25, 16 + bob), (28, 16 + bob)], fill=CYAN)
            d.rectangle((22, 19 + bob, 24, 20 + bob), fill=CYAN)
        elif arm_mode == 2:
            # Straining / workout effort eyes (determined)
            d.line([(18, 15 + bob), (21, 17 + bob)], fill=CYAN)
            d.line([(25, 17 + bob), (28, 15 + bob)], fill=CYAN)
            d.line([(21, 20 + bob), (25, 20 + bob)], fill=CYAN)
            # Sweat drop on frame 6
            if frame == 6:
                d.rectangle((32, 13 + bob, 33, 15 + bob), fill=CYAN)
        elif arm_mode == 3:
            # Happy triumphant smile
            d.line([(17, 15 + bob), (19, 13 + bob), (21, 15 + bob)], fill=CYAN)
            d.line([(25, 15 + bob), (27, 13 + bob), (29, 15 + bob)], fill=CYAN)
            d.line([(20, 19 + bob), (23, 21 + bob), (26, 19 + bob)], fill=CYAN)
        else:
            # Cheerful standard workout eyes
            d.line([(18, 16 + bob), (18, 14 + bob), (20, 14 + bob), (20, 16 + bob)], fill=CYAN)
            d.line([(26, 16 + bob), (26, 14 + bob), (28, 14 + bob), (28, 16 + bob)], fill=CYAN)
            d.line([(21, 20 + bob), (25, 20 + bob)], fill=CYAN)

        # Dumbbells and arms
        if arm_mode == 0:
            # Arms bent at shoulders holding dumbbells
            # Left arm
            d.rectangle((8, 26 + bob, 13, 33 + bob), fill=INK)
            d.rectangle((9, 27 + bob, 12, 31 + bob), fill=WHITE)
            # Right arm
            d.rectangle((33, 26 + bob, 38, 33 + bob), fill=INK)
            d.rectangle((34, 27 + bob, 37, 31 + bob), fill=WHITE)
            # Left dumbbell
            d.rectangle((6, 23 + bob, 9, 25 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((6, 31 + bob, 9, 33 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(7, 25 + bob), (7, 31 + bob)], fill=DUMBBELL_BAR, width=2)
            # Right dumbbell
            d.rectangle((37, 23 + bob, 40, 25 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((37, 31 + bob, 40, 33 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(38, 25 + bob), (38, 31 + bob)], fill=DUMBBELL_BAR, width=2)

        elif arm_mode == 1:
            # Overhead dumbbell press
            # Left arm up
            d.line([(12, 28 + bob), (9, 21 + bob), (8, 14 + bob)], fill=INK, width=4)
            d.line([(12, 28 + bob), (9, 21 + bob), (8, 14 + bob)], fill=WHITE, width=2)
            # Right arm up
            d.line([(34, 28 + bob), (37, 21 + bob), (38, 14 + bob)], fill=INK, width=4)
            d.line([(34, 28 + bob), (37, 21 + bob), (38, 14 + bob)], fill=WHITE, width=2)
            # Left dumbbell overhead
            d.rectangle((4, 11 + bob, 11, 13 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((4, 17 + bob, 11, 19 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(7, 13 + bob), (7, 17 + bob)], fill=DUMBBELL_BAR, width=2)
            # Right dumbbell overhead
            d.rectangle((35, 11 + bob, 42, 13 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((35, 17 + bob, 42, 19 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(38, 13 + bob), (38, 17 + bob)], fill=DUMBBELL_BAR, width=2)

        elif arm_mode == 2:
            # Squat holding dumbbells at side
            # Left arm down
            d.rectangle((7, 30 + bob, 12, 38 + bob), fill=INK)
            d.rectangle((8, 31 + bob, 11, 36 + bob), fill=WHITE)
            # Right arm down
            d.rectangle((34, 30 + bob, 39, 38 + bob), fill=INK)
            d.rectangle((35, 31 + bob, 38, 36 + bob), fill=WHITE)
            # Dumbbells at side
            d.rectangle((5, 37 + bob, 10, 39 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((5, 29 + bob, 10, 31 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(7, 31 + bob), (7, 37 + bob)], fill=DUMBBELL_BAR, width=2)
            d.rectangle((36, 37 + bob, 41, 39 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((36, 29 + bob, 41, 31 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(38, 31 + bob), (38, 37 + bob)], fill=DUMBBELL_BAR, width=2)

        elif arm_mode == 3:
            # Triumph flex
            d.line([(12, 28 + bob), (6, 24 + bob), (7, 16 + bob)], fill=INK, width=4)
            d.line([(12, 28 + bob), (6, 24 + bob), (7, 16 + bob)], fill=WHITE, width=2)
            d.line([(34, 28 + bob), (40, 24 + bob), (39, 16 + bob)], fill=INK, width=4)
            d.line([(34, 28 + bob), (40, 24 + bob), (39, 16 + bob)], fill=WHITE, width=2)
            # Dumbbells tilted in victory
            d.rectangle((4, 13 + bob, 10, 15 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((4, 19 + bob, 10, 21 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(7, 15 + bob), (7, 19 + bob)], fill=DUMBBELL_BAR, width=2)
            d.rectangle((36, 13 + bob, 42, 15 + bob), fill=DUMBBELL_WEIGHT)
            d.rectangle((36, 19 + bob, 42, 21 + bob), fill=DUMBBELL_WEIGHT)
            d.line([(39, 15 + bob), (39, 19 + bob)], fill=DUMBBELL_BAR, width=2)

        file_path = OUT / f"workout_{frame}.png"
        img.save(file_path, optimize=True)
        print(f"Generated {file_path}")


def generate_charging_frames():
    # 4 frames: robot connected to power cable, battery filling on screen
    for frame in range(1, 5):
        img = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)

        bob = 0 if frame in (1, 3) else 1
        draw_base_robot(d, bob=bob)

        # Standard resting arms
        d.rectangle((9, 27 + bob, 14, 34 + bob), fill=INK)
        d.rectangle((10, 28 + bob, 12, 32 + bob), fill=WHITE)
        d.rectangle((11, 33 + bob, 15, 36 + bob), fill=EDGE)
        d.rectangle((34, 27 + bob, 39, 34 + bob), fill=INK)
        d.rectangle((35, 28 + bob, 37, 32 + bob), fill=WHITE)

        # Power cable plugged into robot base on the left
        # Cable entering from left corner (0, 44) to robot body (14, 34)
        cable_points = [(0, 45), (4, 45), (7, 43), (9, 38 + bob), (13, 34 + bob)]
        d.line(cable_points, fill=INK, width=3)
        d.line(cable_points, fill=EDGE, width=1)
        # Power plug socket on robot
        d.rectangle((11, 32 + bob, 14, 36 + bob), fill=EDGE)
        d.rectangle((12, 33 + bob, 13, 35 + bob), fill=YELLOW_LIGHTNING)

        # Screen: Charging battery visualization
        # Clear screen area
        d.rectangle((16, 12 + bob, 30, 22 + bob), fill=SCREEN)

        # Battery outline (18, 14) to (27, 20) with positive terminal at (28, 16..18)
        d.rectangle((18, 14 + bob, 27, 20 + bob), outline=CYAN, width=1)
        d.rectangle((28, 16 + bob, 28, 18 + bob), fill=CYAN)

        # Battery fill levels across 4 frames
        fill_width = frame * 2  # 2, 4, 6, 8 pixels
        d.rectangle((19, 15 + bob, 19 + fill_width - 1, 19 + bob), fill=GREEN_BATTERY)

        # Central lightning bolt icon over battery
        if frame in (2, 4):
            # Glowing yellow lightning icon
            bolt = [
                (23, 14 + bob), (21, 17 + bob), (23, 17 + bob),
                (22, 20 + bob), (25, 16 + bob), (23, 16 + bob)
            ]
            d.polygon(bolt, fill=YELLOW_LIGHTNING)

        # Small energy pulses around antenna / head
        if frame % 2 == 1:
            d.point((7, 9 + bob), fill=CYAN)
            d.point((39, 9 + bob), fill=CYAN)

        file_path = OUT / f"charging_{frame}.png"
        img.save(file_path, optimize=True)
        print(f"Generated {file_path}")


if __name__ == "__main__":
    generate_workout_frames()
    generate_charging_frames()
