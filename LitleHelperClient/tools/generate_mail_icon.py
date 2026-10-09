"""Generate face_mail.png (15x11) for robot face screen badge."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets"
OUT.mkdir(exist_ok=True)

SCREEN = "#193e5c"
WHITE = "#ffffff"
ENVELOPE_BODY = "#f0f9ff"
ENVELOPE_BORDER = "#0284c7"
ENVELOPE_SHADOW = "#bae6fd"
FLAP_LINE = "#0284c7"
SEAL = "#f43f5e"

def generate_face_mail():
    img = Image.new("RGBA", (15, 11), SCREEN)
    d = ImageDraw.Draw(img)

    # Envelope rectangle 11x7 at (2, 2) to (12, 8)
    # Fill body
    for y in range(2, 9):
        for x in range(2, 13):
            d.point((x, y), fill=ENVELOPE_BODY)

    # Outline
    for x in range(2, 13):
        d.point((x, 2), fill=ENVELOPE_BORDER)
        d.point((x, 8), fill=ENVELOPE_BORDER)
    for y in range(2, 9):
        d.point((x := 2, y), fill=ENVELOPE_BORDER)
        d.point((x := 12, y), fill=ENVELOPE_BORDER)

    # Flap folds (V-shape)
    # (3, 3), (4, 4), (5, 5), (6, 5), (7, 5), (8, 5), (9, 4), (10, 3) - or meeting at center (7, 6)
    # (3, 3) -> (4, 4) -> (5, 5) -> (6, 6) -> (7, 6) -> (8, 6) -> (9, 5) -> (10, 4) -> (11, 3)
    d.point((3, 3), fill=FLAP_LINE)
    d.point((4, 4), fill=FLAP_LINE)
    d.point((5, 5), fill=FLAP_LINE)
    d.point((6, 6), fill=FLAP_LINE)
    d.point((7, 6), fill=FLAP_LINE)
    d.point((8, 6), fill=FLAP_LINE)
    d.point((9, 5), fill=FLAP_LINE)
    d.point((10, 4), fill=FLAP_LINE)
    d.point((11, 3), fill=FLAP_LINE)

    # Flap shadow under fold
    for x in range(4, 11):
        if y == 3: pass
    d.point((6, 5), fill=ENVELOPE_SHADOW)
    d.point((7, 5), fill=ENVELOPE_SHADOW)
    d.point((8, 5), fill=ENVELOPE_SHADOW)

    # Cute tiny red heart/stamp seal at center of flap
    d.point((7, 6), fill=SEAL)

    # Corner highlights
    d.point((3, 2), fill=WHITE)
    d.point((4, 2), fill=WHITE)

    target = OUT / "face_mail.png"
    img.save(target, optimize=True)
    print(f"Generated {target} ({img.size})")

if __name__ == "__main__":
    generate_face_mail()
