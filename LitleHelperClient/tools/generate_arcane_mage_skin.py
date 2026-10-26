#!/usr/bin/env python3
"""
Generates all 38 animation states (98 sprite frames) + skin.json for the 'Mage' (Arcane Mage) skin.
Uses authentic high-resolution illustrated artwork of the Arcane Mage (Variant 4)
with bilinear mesh cloth wave physics, floating levitation dynamics, living flame eyes,
orbiting crystal motes, and bidirectional gaze & turning tracking (Left / Right / Up / Down).
"""
import math
import os
from collections import deque
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance

W, H = 96, 96

BRAIN_DIR = Path(r"C:\Users\zqrey3\.gemini\antigravity-ide\brain\72619ffc-f2f0-473e-85e4-53288fda1389")

ART_SOURCES = {
    "idle_right": BRAIN_DIR / "mage_isolated_white_1792997862449.jpg",
    "idle_left": BRAIN_DIR / "mage_facing_left_1792998734868.jpg",
    "idle_front": BRAIN_DIR / "mage_facing_front_1792998755575.jpg",
    "reading": BRAIN_DIR / "mage_reading_book_1792997883966.jpg",
    "sleep": BRAIN_DIR / "mage_sleeping_bed_1792997901221.jpg",
    "battle": BRAIN_DIR / "mage_battle_stance_1792997920406.jpg",
    "notice_red": BRAIN_DIR / "mage_staff_high_red_1792997939520.jpg",
    "twirl_green": BRAIN_DIR / "mage_twirl_green_1792997962612.jpg",
    "poke": BRAIN_DIR / "mage_poking_cursor_1792997985434.jpg",
}

def extract_transparent_character(img_path):
    """Cleanly extracts character from white sticker/background, preserving colors and glow."""
    img = Image.open(img_path).convert("RGBA")
    w, h = img.size

    bg_mask = Image.new("L", (w, h), 0)
    visited = set()
    queue = deque()

    for x in range(w):
        queue.append((x, 0))
        queue.append((x, h - 1))
    for y in range(h):
        queue.append((0, y))
        queue.append((w - 1, y))

    for pt in queue:
        visited.add(pt)

    pixels = img.load()
    mask_pixels = bg_mask.load()

    while queue:
        x, y = queue.popleft()
        r, g, b, a = pixels[x, y]
        if r > 238 and g > 238 and b > 238:
            mask_pixels[x, y] = 255
            for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in visited:
                    visited.add((nx, ny))
                    queue.append((nx, ny))

    bg_mask = bg_mask.filter(ImageFilter.GaussianBlur(radius=1.2))
    bg_data = bg_mask.load()

    r_chan, g_chan, b_chan, _ = img.split()
    alpha_chan = Image.new("L", (w, h), 255)
    a_data = alpha_chan.load()

    for y in range(h):
        for x in range(w):
            bg_val = bg_data[x, y]
            if bg_val >= 248:
                a_data[x, y] = 0
            elif bg_val > 10:
                a_data[x, y] = int(255 * (1.0 - (bg_val / 255.0)))

    result = Image.merge("RGBA", (r_chan, g_chan, b_chan, alpha_chan))
    bbox = result.getbbox()
    if bbox:
        result = result.crop(bbox)
    return result

def fit_to_frame(im, target_w=96, target_h=96, padding=6, shift_x=0, shift_y=0, scale_mul=1.0):
    """Fits transparent sprite into 96x96 canvas with padding and optional displacement."""
    rw, rh = im.size
    max_w = target_w - padding * 2
    max_h = target_h - padding * 2
    scale = min(max_w / rw, max_h / rh) * scale_mul
    nw = max(1, int(rw * scale))
    nh = max(1, int(rh * scale))
    resized = im.resize((nw, nh), Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    pos_x = (target_w - nw) // 2 + shift_x
    pos_y = (target_h - nh) // 2 + shift_y
    canvas.paste(resized, (pos_x, pos_y), resized)
    return canvas

def shift_image(im, dx, dy):
    """Pans RGBA image within canvas."""
    res = Image.new("RGBA", im.size, (0, 0, 0, 0))
    res.paste(im, (dx, dy), im)
    return res

def modulate_brightness(im, factor):
    r, g, b, a = im.split()
    rgb = Image.merge("RGB", (r, g, b))
    rgb = ImageEnhance.Brightness(rgb).enhance(factor)
    nr, ng, nb = rgb.split()
    return Image.merge("RGBA", (nr, ng, nb, a))

def deform_mesh(base_img, disp_func, n_strips=24):
    """
    Applies seamless bilinear quad mesh deformation across vertical strips.
    disp_func(y) returns (dx, dy) for row y.
    """
    w, h = base_img.size
    strip_h = h / float(n_strips)
    mesh = []

    for i in range(n_strips):
        y0 = i * strip_h
        y1 = (i + 1) * strip_h
        dx0, dy0 = disp_func(y0)
        dx1, dy1 = disp_func(y1)

        target = (0, int(y0), w, int(y1))
        # PIL mesh maps target pixels to source coords: source = target - disp
        src_quad = (
            -dx0, y0 - dy0,
            -dx1, y1 - dy1,
            w - dx1, y1 - dy1,
            w - dx0, y0 - dy0
        )
        mesh.append((target, src_quad))

    return base_img.transform((w, h), Image.Transform.MESH, mesh, resample=Image.Resampling.BILINEAR)

def animate_mage_float(base_img, theta, is_left=False, intensity=1.0):
    """
    Generates an organic levitation frame with cloth waves, hem billowing,
    orbiting crystal motes, and living flame eyes.
    """
    w, h = base_img.size

    # Levitation harmonics
    dy_head = -1.5 * intensity * math.sin(theta)
    dy_hem = -1.8 * intensity * math.sin(theta - math.pi / 3.0)
    billow = 1.6 * intensity * math.cos(theta)
    flip_sign = -1.0 if is_left else 1.0

    def disp(y):
        # Upper body weight: y < 42
        c_weight = max(0.0, min(1.0, (y - 40.0) / 48.0))
        w_sq = c_weight * c_weight
        dy = dy_head * (1.0 - c_weight) + dy_hem * c_weight
        # Travelling wave along hem
        wave = math.sin(y / 7.5 - theta) * 1.4 * flip_sign * w_sq
        dx = wave + (billow * flip_sign * w_sq)
        return dx, dy

    out = deform_mesh(base_img, disp)
    draw = ImageDraw.Draw(out)

    # 1. Staff crystal motes
    # Staff crystal center
    int_dy = int(round(dy_head))
    if is_left:
        crys_x, crys_y = 18, 22 + int_dy
    else:
        crys_x, crys_y = 74, 20 + int_dy

    rx, ry = 7, 4
    for k in range(3):
        angle = theta + k * (2 * math.pi / 3.0)
        px = crys_x + rx * math.cos(angle)
        py = crys_y + ry * math.sin(angle)
        alpha = int(140 + 80 * math.sin(angle))
        draw.ellipse([px - 1, py - 1, px + 1, py + 1], fill=(120, 255, 255, alpha))
        draw.point((px, py), fill=(255, 255, 255, alpha))

    # 2. Living fiery eye sparkles
    eye_offset_y = 31 + int_dy
    eye_x = 42 if is_left else 46
    spark_flicker = math.sin(theta * 2.0)
    if spark_flicker > 0.2:
        draw.point((eye_x - 4, eye_offset_y - 2), fill=(100, 255, 255, 200))
        draw.point((eye_x + 5, eye_offset_y - 3), fill=(180, 255, 255, 220))

    return out

def animate_gaze(base_img, dx_head, dy_head, cloth_phase, is_left=False):
    """
    Generates a directional gaze frame with head tilt and natural cloth drift.
    """
    def disp(y):
        h_weight = max(0.0, min(1.0, (55.0 - y) / 25.0))
        c_weight = max(0.0, min(1.0, (y - 45.0) / 45.0))
        dy = dy_head * h_weight
        dx = dx_head * h_weight + math.sin(y / 7.0 + cloth_phase) * 1.2 * (c_weight ** 2)
        return dx, dy

    out = deform_mesh(base_img, disp)
    draw = ImageDraw.Draw(out)

    # Crystal motes
    int_dy = int(round(dy_head))
    crys_x = 18 if is_left else 74
    crys_y = (22 if is_left else 20) + int_dy
    for k in range(2):
        angle = cloth_phase + k * math.pi
        px = crys_x + 6 * math.cos(angle)
        py = crys_y + 4 * math.sin(angle)
        draw.ellipse([px - 1, py - 1, px + 1, py + 1], fill=(120, 255, 255, 180))

    return out

STATE_FRAME_COUNTS = {
    "idle": 4, "charging": 4,
    "twirl": 8, "dance": 8, "workout": 8,
    "action": 2, "busy": 2, "celebrate": 2, "cry": 2, "dizzy": 2, "drag": 2,
    "error": 2, "facepalm": 2, "flower": 2, "greeting": 2, "joy": 2, "laugh": 2,
    "lookdown": 2, "lookdownleft": 2, "lookdownright": 2, "lookleft": 2, "lookright": 2,
    "lookup": 2, "lookupleft": 2, "lookupright": 2, "notice": 2, "offended": 2,
    "petcat": 2, "petdog": 2, "sad": 2, "shy": 2, "sleep": 2, "success": 2,
    "surprise": 2, "think": 2, "turnback": 2, "wake": 2, "yawn": 2
}

def main():
    root = Path(__file__).resolve().parent.parent / "Assets" / "Skins" / "Mage"
    root.mkdir(parents=True, exist_ok=True)
    print(f"Generating authentic Arcane Mage skin into: {root}")

    print("Extracting transparent hero assets...")
    raw_right = extract_transparent_character(ART_SOURCES["idle_right"])
    raw_left = extract_transparent_character(ART_SOURCES["idle_left"])
    raw_front = extract_transparent_character(ART_SOURCES["idle_front"])
    raw_reading = extract_transparent_character(ART_SOURCES["reading"])
    raw_sleep = extract_transparent_character(ART_SOURCES["sleep"])
    raw_battle = extract_transparent_character(ART_SOURCES["battle"])
    raw_notice_red = extract_transparent_character(ART_SOURCES["notice_red"])
    raw_twirl_green = extract_transparent_character(ART_SOURCES["twirl_green"])
    raw_poke = extract_transparent_character(ART_SOURCES["poke"])

    right_base = fit_to_frame(raw_right)
    left_base = fit_to_frame(raw_left)
    front_base = fit_to_frame(raw_front)

    total_frames = 0

    # 1. IDLE (4 frames): Majestic levitation with bilinear mesh cloth waves,
    # billowing robes, living flame eyes, and orbiting staff crystal motes
    print("Generating Idle frames (living cloth wave & levitation)...")
    for i in range(4):
        theta = i * (math.tau / 4.0)
        f = animate_mage_float(right_base, theta, is_left=False, intensity=1.0)
        f.save(root / f"idle_{i+1}.png", "PNG")
        total_frames += 1

    # 2. CHARGING (4 frames): Arcane power gathering with amplified floating & glowing core
    print("Generating Charging frames...")
    for i in range(4):
        theta = i * (math.tau / 4.0)
        f = animate_mage_float(right_base, theta, is_left=False, intensity=1.5)
        f = modulate_brightness(f, 1.05 + 0.08 * math.sin(theta))
        f.save(root / f"charging_{i+1}.png", "PNG")
        total_frames += 1

    # 3. DIRECTIONAL GAZE & TURNING (8 directions, 2 frames each):
    # Left directions USE raw_left (fully turned left, staff left, looking left)
    # Right directions USE raw_right (fully turned right, staff right, looking right)
    # Up and Down directions USE raw_front with head & eye tilts
    print("Generating Directional Gaze frames (Look Left / Right / Up / Down / Diagonals)...")
    look_configs = {
        "lookleft": (left_base, -2.0, 0.0, True),
        "lookupleft": (left_base, -1.8, -2.5, True),
        "lookdownleft": (left_base, -1.8, 2.5, True),
        "lookright": (right_base, 2.0, 0.0, False),
        "lookupright": (right_base, 1.8, -2.5, False),
        "lookdownright": (right_base, 1.8, 2.5, False),
        "lookup": (front_base, 0.0, -3.0, False),
        "lookdown": (front_base, 0.0, 3.0, False),
    }
    for look_name, (base_im, h_dx, h_dy, is_l) in look_configs.items():
        for i in range(2):
            phase = i * math.pi
            f = animate_gaze(base_im, h_dx, h_dy, phase, is_left=is_l)
            if i == 1:
                f = modulate_brightness(f, 1.03)
            f.save(root / f"{look_name}_{i+1}.png", "PNG")
            total_frames += 1

    # 4. TURNBACK (2 frames): Smooth transition turning between right and left
    f_turn1 = animate_gaze(front_base, 0.0, 0.0, 0.0, is_left=False)
    f_turn2 = animate_gaze(left_base, -1.0, 0.0, math.pi / 2.0, is_left=True)
    f_turn1.save(root / "turnback_1.png", "PNG")
    f_turn2.save(root / "turnback_2.png", "PNG")
    total_frames += 2

    # 5. YAWN (2 frames): Reading ancient spellbook (> 5 min inactivity)
    for i in range(2):
        bob = 0 if i == 0 else -1
        f = fit_to_frame(raw_reading, shift_y=bob)
        if i == 1:
            f = modulate_brightness(f, 1.07)  # Pulsing runes
        f.save(root / f"yawn_{i+1}.png", "PNG")
        total_frames += 1

    # 6. SLEEP (2 frames): Sleeping peacefully on enchanted cloud-bed (> 15 min inactivity)
    for i in range(2):
        bob = 0 if i == 0 else 1
        f = fit_to_frame(raw_sleep, shift_y=bob, scale_mul=1.03)
        if i == 1:
            f = modulate_brightness(f, 0.96)
        f.save(root / f"sleep_{i+1}.png", "PNG")
        total_frames += 1

    # 7. WAKE (2 frames): Waking up / snapping book shut
    f_wake1 = fit_to_frame(raw_reading, shift_y=1)
    f_wake2 = animate_mage_float(right_base, 0.0, intensity=1.2)
    f_wake1.save(root / "wake_1.png", "PNG")
    f_wake2.save(root / "wake_2.png", "PNG")
    total_frames += 2

    # 8. NOTICE (2 frames): Red Staff High alert on new message
    for i in range(2):
        bob = -1 if i == 0 else -2
        f = fit_to_frame(raw_notice_red, shift_y=bob)
        if i == 1:
            f = modulate_brightness(f, 1.08)  # Blazing red flare
        f.save(root / f"notice_{i+1}.png", "PNG")
        total_frames += 1

    # 9. TWIRL (8 frames): Printing document - spinning green staff with circular momentum
    twirl_base = fit_to_frame(raw_twirl_green)
    for i in range(8):
        angle = (i / 8.0) * math.tau
        dy = int(round(math.sin(angle) * 1.8))
        dx = int(round(math.cos(angle) * 1.5))
        f = shift_image(twirl_base, dx, dy)
        if i % 2 == 1:
            f = modulate_brightness(f, 1.06)
        f.save(root / f"twirl_{i+1}.png", "PNG")
        total_frames += 1

    # 10. WORKOUT (8 frames): Battle stance & Emergency slam!
    # Frames 1-4: combat ready stance, Frames 5-8: slam and impact shockwave
    battle_base = fit_to_frame(raw_battle)
    for i in range(8):
        if i < 4:
            bob = 1 if i % 2 == 0 else 0
            f = shift_image(battle_base, 0, bob)
        else:
            bob = -2 if i in (4, 5) else 2
            f = shift_image(battle_base, 0, bob)
            if i in (6, 7):
                f = modulate_brightness(f, 1.12)
        f.save(root / f"workout_{i+1}.png", "PNG")
        total_frames += 1

    # 11. SURPRISE (2 frames): Cursor hover > 5 min -> wide eyes & poking cursor!
    f_poke1 = fit_to_frame(raw_poke, shift_y=-1)
    f_poke2 = fit_to_frame(raw_poke, shift_x=3, shift_y=-1)
    f_poke2 = modulate_brightness(f_poke2, 1.08)
    f_poke1.save(root / "surprise_1.png", "PNG")
    f_poke2.save(root / "surprise_2.png", "PNG")
    total_frames += 2

    # 12. ACTION (2 frames): Spell casting / submitting ticket
    f_act1 = fit_to_frame(raw_battle, shift_x=-1, shift_y=1)
    f_act2 = fit_to_frame(raw_battle, shift_x=2, shift_y=-2)
    f_act2 = modulate_brightness(f_act2, 1.1)
    f_act1.save(root / "action_1.png", "PNG")
    f_act2.save(root / "action_2.png", "PNG")
    total_frames += 2

    # 13. BUSY (2 frames): Scribing / writing ticket
    for i in range(2):
        f = fit_to_frame(raw_reading, shift_y=i)
        f.save(root / f"busy_{i+1}.png", "PNG")
        total_frames += 1

    # 14. JOY (2 frames): Joyful floating celebration
    f_joy1 = animate_mage_float(right_base, math.pi / 2.0, intensity=1.5)
    f_joy2 = animate_mage_float(right_base, 3.0 * math.pi / 2.0, intensity=1.0)
    f_joy1 = modulate_brightness(f_joy1, 1.08)
    f_joy1.save(root / "joy_1.png", "PNG")
    f_joy2.save(root / "joy_2.png", "PNG")
    total_frames += 2

    # 15. CELEBRATE (2 frames)
    f_cel1 = fit_to_frame(raw_notice_red, shift_y=-3)
    f_cel2 = animate_mage_float(right_base, 0.0)
    f_cel1.save(root / "celebrate_1.png", "PNG")
    f_cel2.save(root / "celebrate_2.png", "PNG")
    total_frames += 2

    # 16. LAUGH (2 frames)
    f_la1 = animate_mage_float(right_base, 0.0, intensity=0.8)
    f_la2 = animate_mage_float(right_base, math.pi, intensity=1.4)
    f_la1.save(root / "laugh_1.png", "PNG")
    f_la2.save(root / "laugh_2.png", "PNG")
    total_frames += 2

    # 17. THINK (2 frames)
    f_th1 = fit_to_frame(raw_reading, shift_x=1, shift_y=-1)
    f_th2 = fit_to_frame(raw_reading, shift_x=-1, shift_y=0)
    f_th1.save(root / "think_1.png", "PNG")
    f_th2.save(root / "think_2.png", "PNG")
    total_frames += 2

    # 18. SAD (2 frames): Drooping cowl & lowered staff
    f_sad1 = fit_to_frame(raw_right, shift_y=2)
    f_sad1 = modulate_brightness(f_sad1, 0.9)
    f_sad2 = fit_to_frame(raw_right, shift_y=3)
    f_sad2 = modulate_brightness(f_sad2, 0.88)
    f_sad1.save(root / "sad_1.png", "PNG")
    f_sad2.save(root / "sad_2.png", "PNG")
    total_frames += 2

    # 19. CRY (2 frames)
    f_cry1 = fit_to_frame(raw_right, shift_y=3)
    f_cry1 = modulate_brightness(f_cry1, 0.85)
    f_cry2 = fit_to_frame(raw_right, shift_y=4)
    f_cry2 = modulate_brightness(f_cry2, 0.82)
    f_cry1.save(root / "cry_1.png", "PNG")
    f_cry2.save(root / "cry_2.png", "PNG")
    total_frames += 2

    # 20. ERROR (2 frames): Staff sparks and defensive tilt
    f_err1 = fit_to_frame(raw_battle, shift_x=-2, shift_y=1)
    f_err2 = fit_to_frame(raw_battle, shift_x=1, shift_y=-1)
    f_err2 = modulate_brightness(f_err2, 1.1)
    f_err1.save(root / "error_1.png", "PNG")
    f_err2.save(root / "error_2.png", "PNG")
    total_frames += 2

    # 21. SUCCESS (2 frames): Staff thrust success
    f_suc1 = fit_to_frame(raw_battle, shift_y=-2)
    f_suc2 = animate_mage_float(right_base, 0.0)
    f_suc1 = modulate_brightness(f_suc1, 1.1)
    f_suc1.save(root / "success_1.png", "PNG")
    f_suc2.save(root / "success_2.png", "PNG")
    total_frames += 2

    # 22. GREETING (2 frames): Welcoming bow with staff
    f_gr1 = animate_mage_float(right_base, 0.0, intensity=0.5)
    f_gr2 = animate_mage_float(front_base, 0.0, intensity=0.8)
    f_gr1.save(root / "greeting_1.png", "PNG")
    f_gr2.save(root / "greeting_2.png", "PNG")
    total_frames += 2

    # 23. SHY (2 frames): Hiding behind staff
    f_shy1 = fit_to_frame(raw_left, shift_x=1, shift_y=1)
    f_shy2 = fit_to_frame(raw_left, shift_x=2, shift_y=2)
    f_shy1.save(root / "shy_1.png", "PNG")
    f_shy2.save(root / "shy_2.png", "PNG")
    total_frames += 2

    # 24. OFFENDED (2 frames): Turning back in pride
    f_off1 = fit_to_frame(raw_left, shift_x=-2, shift_y=0)
    f_off2 = fit_to_frame(raw_left, shift_x=-3, shift_y=1)
    f_off1.save(root / "offended_1.png", "PNG")
    f_off2.save(root / "offended_2.png", "PNG")
    total_frames += 2

    # 25. DIZZY (2 frames)
    f_diz1 = fit_to_frame(raw_right, shift_x=-2, shift_y=2)
    f_diz2 = fit_to_frame(raw_left, shift_x=2, shift_y=2)
    f_diz1.save(root / "dizzy_1.png", "PNG")
    f_diz2.save(root / "dizzy_2.png", "PNG")
    total_frames += 2

    # 26. DRAG (2 frames): Hovering in grasp of mouse
    f_drag1 = animate_mage_float(right_base, 0.0, intensity=1.5)
    f_drag2 = animate_mage_float(right_base, math.pi, intensity=1.5)
    f_drag1.save(root / "drag_1.png", "PNG")
    f_drag2.save(root / "drag_2.png", "PNG")
    total_frames += 2

    # 27. FACEPALM (2 frames)
    f_fp1 = fit_to_frame(raw_right, shift_y=1)
    f_fp2 = fit_to_frame(raw_right, shift_y=2)
    f_fp1.save(root / "facepalm_1.png", "PNG")
    f_fp2.save(root / "facepalm_2.png", "PNG")
    total_frames += 2

    # 28. FLOWER (2 frames): Conjuring magical flower / glowing sprout
    f_fl1 = fit_to_frame(raw_reading, shift_y=-1)
    f_fl2 = fit_to_frame(raw_reading, shift_y=0)
    f_fl2 = modulate_brightness(f_fl2, 1.08)
    f_fl1.save(root / "flower_1.png", "PNG")
    f_fl2.save(root / "flower_2.png", "PNG")
    total_frames += 2

    # 29. PETCAT (2 frames)
    f_cat1 = fit_to_frame(raw_poke, shift_y=1)
    f_cat2 = fit_to_frame(raw_poke, shift_y=0)
    f_cat1.save(root / "petcat_1.png", "PNG")
    f_cat2.save(root / "petcat_2.png", "PNG")
    total_frames += 2

    # 30. PETDOG (2 frames)
    f_dog1 = fit_to_frame(raw_poke, shift_y=0)
    f_dog2 = fit_to_frame(raw_poke, shift_y=1)
    f_dog1.save(root / "petdog_1.png", "PNG")
    f_dog2.save(root / "petdog_2.png", "PNG")
    total_frames += 2

    # 31. DANCE (8 frames): Arcane dance hovering in infinity pattern
    dance_base = fit_to_frame(raw_right)
    for i in range(8):
        angle = (i / 8.0) * math.tau
        dy = int(round(math.sin(angle * 2.0) * 2.0))
        dx = int(round(math.cos(angle) * 3.0))
        use_left = (i in (2, 3, 4, 5))
        base = left_base if use_left else right_base
        f = animate_mage_float(base, angle, is_left=use_left, intensity=1.2)
        f = shift_image(f, dx, dy)
        f.save(root / f"dance_{i+1}.png", "PNG")
        total_frames += 1

    # 32. Write skin.json with smooth timing
    skin_json = """{
  "id": "Mage",
  "displayName": "Энергичный чародей (Arcane Mage)",
  "author": "PixelHelper",
  "version": "1.0",
  "spriteWidth": 96,
  "spriteHeight": 96,
  "scalingMode": "HighQuality",
  "badgeAnchor": {
    "x": 35.0,
    "y": 12.0
  },
  "hatAnchor": {
    "x": 24.0,
    "y": -6.0
  },
  "frameIntervalsMs": {
    "Twirl": 100,
    "Sleep": 300,
    "Workout": 150,
    "Dance": 120,
    "Charging": 160,
    "Idle": 220,
    "Default": 140
  }
}
"""
    (root / "skin.json").write_text(skin_json, encoding="utf-8")
    print(f"Generated {total_frames} frames successfully into {root}!")

if __name__ == "__main__":
    main()
