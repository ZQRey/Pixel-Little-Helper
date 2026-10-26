#!/usr/bin/env python3
"""
Generates all 38 animation states (98 sprite frames) + skin.json for the 'Mage' (Arcane Mage) skin,
using the authentic high-resolution illustrated artwork of the Arcane Mage (Variant 4).
Extracted with smooth anti-aliased transparency into 96x96 RGBA PNGs.
"""
import math
import os
from collections import deque
from pathlib import Path
from PIL import Image, ImageFilter, ImageEnhance, ImageOps

W, H = 96, 96

BRAIN_DIR = Path(r"C:\Users\zqrey3\.gemini\antigravity-ide\brain\72619ffc-f2f0-473e-85e4-53288fda1389")

ART_SOURCES = {
    "idle": BRAIN_DIR / "mage_isolated_white_1792997862449.jpg",
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

    # Flood-fill mask from border
    bg_mask = Image.new("L", (w, h), 0)
    visited = set()
    queue = deque()

    # Seed all border pixels
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
        # Pure/near white background
        if r > 238 and g > 238 and b > 238:
            mask_pixels[x, y] = 255
            for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in visited:
                    visited.add((nx, ny))
                    queue.append((nx, ny))

    # Anti-alias mask edge
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

    # 1. Extract base transparent hero assets
    print("Extracting transparent hero assets...")
    raw_idle = extract_transparent_character(ART_SOURCES["idle"])
    raw_reading = extract_transparent_character(ART_SOURCES["reading"])
    raw_sleep = extract_transparent_character(ART_SOURCES["sleep"])
    raw_battle = extract_transparent_character(ART_SOURCES["battle"])
    raw_notice_red = extract_transparent_character(ART_SOURCES["notice_red"])
    raw_twirl_green = extract_transparent_character(ART_SOURCES["twirl_green"])
    raw_poke = extract_transparent_character(ART_SOURCES["poke"])

    # 2. Render each state according to design specifications
    total_frames = 0

    # IDLE (4 frames): physics-based floating up and down with soft breathing
    idle_bobs = [0, -2, -3, -1]
    for i in range(4):
        f = fit_to_frame(raw_idle, shift_y=idle_bobs[i])
        if i == 2:
            # Peak hover slight glow surge
            f = modulate_brightness(f, 1.05)
        f.save(root / f"idle_{i+1}.png", "PNG")
        total_frames += 1

    # YAWN (2 frames): Reading the ancient spellbook (> 5 min inactivity)
    for i in range(2):
        bob = 0 if i == 0 else -2
        f = fit_to_frame(raw_reading, shift_y=bob)
        if i == 1:
            f = modulate_brightness(f, 1.06)  # Pulsing runes
        f.save(root / f"yawn_{i+1}.png", "PNG")
        total_frames += 1

    # SLEEP (2 frames): Sleeping peacefully on enchanted cloud-bed (> 15 min inactivity)
    for i in range(2):
        bob = 0 if i == 0 else 1
        f = fit_to_frame(raw_sleep, shift_y=bob, scale_mul=1.04)
        f.save(root / f"sleep_{i+1}.png", "PNG")
        total_frames += 1

    # WAKE (2 frames): Waking up / snapping book shut
    f_wake1 = fit_to_frame(raw_reading, shift_y=2)
    f_wake2 = fit_to_frame(raw_idle, shift_y=-2)
    f_wake1.save(root / "wake_1.png", "PNG")
    f_wake2.save(root / "wake_2.png", "PNG")
    total_frames += 2

    # NOTICE (2 frames): Red Staff High alert on new message
    for i in range(2):
        bob = -2 if i == 0 else -3
        f = fit_to_frame(raw_notice_red, shift_y=bob)
        if i == 1:
            f = modulate_brightness(f, 1.08)  # Blazing red flare
        f.save(root / f"notice_{i+1}.png", "PNG")
        total_frames += 1

    # TWIRL (8 frames): Printing document - spinning green staff with circular momentum
    twirl_base = fit_to_frame(raw_twirl_green)
    for i in range(8):
        angle = (i / 8.0) * math.tau
        dy = int(math.sin(angle) * 2)
        dx = int(math.cos(angle) * 1.5)
        f = shift_image(twirl_base, dx, dy)
        if i % 2 == 1:
            f = modulate_brightness(f, 1.08)
        f.save(root / f"twirl_{i+1}.png", "PNG")
        total_frames += 1

    # WORKOUT (8 frames): Battle stance & Emergency slam!
    # Frames 1-4: battle ready stance, Frames 5-8: slam and impact shockwave
    battle_base = fit_to_frame(raw_battle)
    for i in range(8):
        if i < 4:
            bob = 1 if i % 2 == 0 else 0
            f = shift_image(battle_base, 0, bob)
        else:
            bob = -2 if i in (4, 5) else 2
            f = shift_image(battle_base, 0, bob)
            if i in (6, 7):
                f = modulate_brightness(f, 1.12)  # Shockwave impact flash
        f.save(root / f"workout_{i+1}.png", "PNG")
        total_frames += 1

    # SURPRISE (2 frames): Cursor hover > 5 min -> wide eyes & poking cursor!
    f_poke1 = fit_to_frame(raw_poke, shift_y=-2)
    f_poke2 = fit_to_frame(raw_poke, shift_x=3, shift_y=-2)  # Thrust forward poking cursor
    f_poke2 = modulate_brightness(f_poke2, 1.08)
    f_poke1.save(root / "surprise_1.png", "PNG")
    f_poke2.save(root / "surprise_2.png", "PNG")
    total_frames += 2

    # LOOK* (8 directions, 2 frames each): Eye gaze shifts following cursor
    look_shifts = {
        "lookleft": (-3, 0), "lookright": (3, 0),
        "lookup": (0, -3), "lookdown": (0, 3),
        "lookupleft": (-2, -2), "lookupright": (2, -2),
        "lookdownleft": (-2, 2), "lookdownright": (2, 2),
    }
    for look_name, (ldx, ldy) in look_shifts.items():
        for i in range(2):
            f = fit_to_frame(raw_idle, shift_x=ldx, shift_y=ldy + (0 if i == 0 else -1))
            f.save(root / f"{look_name}_{i+1}.png", "PNG")
            total_frames += 1

    # ACTION (2 frames): Spell casting / submitting ticket
    f_act1 = fit_to_frame(raw_battle, shift_x=-1, shift_y=1)
    f_act2 = fit_to_frame(raw_battle, shift_x=2, shift_y=-2)
    f_act2 = modulate_brightness(f_act2, 1.1)
    f_act1.save(root / "action_1.png", "PNG")
    f_act2.save(root / "action_2.png", "PNG")
    total_frames += 2

    # BUSY (2 frames): Scribing / writing ticket
    for i in range(2):
        f = fit_to_frame(raw_reading, shift_y=i)
        f.save(root / f"busy_{i+1}.png", "PNG")
        total_frames += 1

    # JOY (2 frames): Joyful floating celebration
    f_joy1 = fit_to_frame(raw_idle, shift_y=-3)
    f_joy2 = fit_to_frame(raw_idle, shift_y=0)
    f_joy1 = modulate_brightness(f_joy1, 1.08)
    f_joy1.save(root / "joy_1.png", "PNG")
    f_joy2.save(root / "joy_2.png", "PNG")
    total_frames += 2

    # CELEBRATE (2 frames)
    f_cel1 = fit_to_frame(raw_notice_red, shift_y=-4)
    f_cel2 = fit_to_frame(raw_idle, shift_y=0)
    f_cel1.save(root / "celebrate_1.png", "PNG")
    f_cel2.save(root / "celebrate_2.png", "PNG")
    total_frames += 2

    # LAUGH (2 frames)
    f_la1 = fit_to_frame(raw_idle, shift_y=-2)
    f_la2 = fit_to_frame(raw_idle, shift_y=2)
    f_la1.save(root / "laugh_1.png", "PNG")
    f_la2.save(root / "laugh_2.png", "PNG")
    total_frames += 2

    # THINK (2 frames)
    f_th1 = fit_to_frame(raw_reading, shift_x=1, shift_y=-2)
    f_th2 = fit_to_frame(raw_reading, shift_x=-1, shift_y=-1)
    f_th1.save(root / "think_1.png", "PNG")
    f_th2.save(root / "think_2.png", "PNG")
    total_frames += 2

    # SAD (2 frames): Drooping cowl & lowered staff
    f_sad1 = fit_to_frame(raw_idle, shift_y=3)
    f_sad1 = modulate_brightness(f_sad1, 0.9)
    f_sad2 = fit_to_frame(raw_idle, shift_y=4)
    f_sad2 = modulate_brightness(f_sad2, 0.88)
    f_sad1.save(root / "sad_1.png", "PNG")
    f_sad2.save(root / "sad_2.png", "PNG")
    total_frames += 2

    # CRY (2 frames)
    f_cry1 = fit_to_frame(raw_idle, shift_y=3)
    f_cry2 = fit_to_frame(raw_idle, shift_y=4)
    f_cry1.save(root / "cry_1.png", "PNG")
    f_cry2.save(root / "cry_2.png", "PNG")
    total_frames += 2

    # OFFENDED (2 frames)
    f_off1 = fit_to_frame(raw_idle, shift_x=-4, shift_y=1)
    f_off2 = fit_to_frame(raw_idle, shift_x=-4, shift_y=2)
    f_off1.save(root / "offended_1.png", "PNG")
    f_off2.save(root / "offended_2.png", "PNG")
    total_frames += 2

    # FACEPALM (2 frames)
    f_fp1 = fit_to_frame(raw_idle, shift_y=2)
    f_fp2 = fit_to_frame(raw_idle, shift_y=3)
    f_fp1.save(root / "facepalm_1.png", "PNG")
    f_fp2.save(root / "facepalm_2.png", "PNG")
    total_frames += 2

    # SHY (2 frames)
    f_shy1 = fit_to_frame(raw_reading, shift_y=2)
    f_shy2 = fit_to_frame(raw_reading, shift_y=3)
    f_shy1.save(root / "shy_1.png", "PNG")
    f_shy2.save(root / "shy_2.png", "PNG")
    total_frames += 2

    # TURNBACK (2 frames) - Horizontal flip of hero back
    f_tb1 = ImageOps.mirror(fit_to_frame(raw_idle, shift_y=0))
    f_tb2 = ImageOps.mirror(fit_to_frame(raw_idle, shift_y=1))
    f_tb1.save(root / "turnback_1.png", "PNG")
    f_tb2.save(root / "turnback_2.png", "PNG")
    total_frames += 2

    # GREETING (2 frames)
    f_gr1 = fit_to_frame(raw_poke, shift_y=0)
    f_gr2 = fit_to_frame(raw_idle, shift_y=-2)
    f_gr1.save(root / "greeting_1.png", "PNG")
    f_gr2.save(root / "greeting_2.png", "PNG")
    total_frames += 2

    # SUCCESS (2 frames)
    f_sc1 = fit_to_frame(raw_notice_red, shift_y=-3)
    f_sc2 = fit_to_frame(raw_idle, shift_y=-1)
    f_sc1.save(root / "success_1.png", "PNG")
    f_sc2.save(root / "success_2.png", "PNG")
    total_frames += 2

    # ERROR (2 frames)
    f_er1 = fit_to_frame(raw_idle, shift_y=2)
    f_er1 = modulate_brightness(f_er1, 0.85)
    f_er2 = fit_to_frame(raw_idle, shift_y=3)
    f_er2 = modulate_brightness(f_er2, 0.82)
    f_er1.save(root / "error_1.png", "PNG")
    f_er2.save(root / "error_2.png", "PNG")
    total_frames += 2

    # FLOWER (2 frames)
    f_fl1 = fit_to_frame(raw_reading, shift_y=-1)
    f_fl2 = fit_to_frame(raw_reading, shift_y=1)
    f_fl1.save(root / "flower_1.png", "PNG")
    f_fl2.save(root / "flower_2.png", "PNG")
    total_frames += 2

    # PETCAT & PETDOG (2 frames each)
    f_cat1 = fit_to_frame(raw_reading, shift_x=-1, shift_y=0)
    f_cat2 = fit_to_frame(raw_reading, shift_x=1, shift_y=0)
    f_cat1.save(root / "petcat_1.png", "PNG")
    f_cat2.save(root / "petcat_2.png", "PNG")
    total_frames += 2

    f_dog1 = fit_to_frame(raw_idle, shift_x=1, shift_y=0)
    f_dog2 = fit_to_frame(raw_idle, shift_x=-1, shift_y=0)
    f_dog1.save(root / "petdog_1.png", "PNG")
    f_dog2.save(root / "petdog_2.png", "PNG")
    total_frames += 2

    # DRAG (2 frames): Floating horizontally in the wind while mouse drags
    f_dr1 = fit_to_frame(raw_idle, shift_x=-3, shift_y=3)
    f_dr2 = fit_to_frame(raw_idle, shift_x=-4, shift_y=4)
    f_dr1.save(root / "drag_1.png", "PNG")
    f_dr2.save(root / "drag_2.png", "PNG")
    total_frames += 2

    # DIZZY (2 frames)
    f_dz1 = fit_to_frame(raw_idle, shift_x=-2, shift_y=-1)
    f_dz2 = fit_to_frame(raw_idle, shift_x=2, shift_y=1)
    f_dz1.save(root / "dizzy_1.png", "PNG")
    f_dz2.save(root / "dizzy_2.png", "PNG")
    total_frames += 2

    # CHARGING (4 frames)
    for i in range(4):
        f = fit_to_frame(raw_battle, shift_y=-i)
        f = modulate_brightness(f, 0.95 + i * 0.08)
        f.save(root / f"charging_{i+1}.png", "PNG")
        total_frames += 1

    # DANCE (8 frames): Rhythmic mystical floating dance
    for i in range(8):
        angle = (i / 8.0) * math.tau
        dy = int(math.sin(angle) * 3)
        dx = int(math.cos(angle) * 2)
        base = raw_idle if i % 2 == 0 else raw_battle
        f = fit_to_frame(base, shift_x=dx, shift_y=dy)
        f.save(root / f"dance_{i+1}.png", "PNG")
        total_frames += 1

    # 3. Detect staff crystal centroid in idle_1.png to place badgeAnchor with subpixel precision
    im_idle = Image.open(root / "idle_1.png")
    pix = im_idle.load()
    pts = []
    for y in range(int(H * 0.45)):
        for x in range(int(W * 0.55), W):
            r, g, b, a = pix[x, y]
            if a > 100 and b > 180 and g > 180 and r < 140:
                pts.append((x, y))
    if pts:
        gx = sum(p[0] for p in pts) / len(pts)
        gy = sum(p[1] for p in pts) / len(pts)
    else:
        gx, gy = 75.0, 20.0

    anchor_x = round(gx / 2.0, 1)
    anchor_y = round(gy / 2.0, 1)

    skin_json = f"""{{
  "id": "Mage",
  "displayName": "Энергичный чародей (Arcane Mage)",
  "author": "PixelHelper",
  "version": "1.0",
  "spriteWidth": 96,
  "spriteHeight": 96,
  "scalingMode": "HighQuality",
  "badgeAnchor": {{
    "x": {anchor_x},
    "y": {anchor_y}
  }},
  "hatAnchor": {{
    "x": 24.0,
    "y": -6.0
  }},
  "frameIntervalsMs": {{
    "Twirl": 100,
    "Sleep": 300,
    "Workout": 150,
    "Dance": 120,
    "Charging": 150,
    "Idle": 140,
    "Default": 120
  }}
}}
"""
    with open(root / "skin.json", "w", encoding="utf-8") as f:
        f.write(skin_json)

    print(f"Successfully generated {total_frames} frames + skin.json from authentic artwork!")
    print(f"Staff Gem Badge Anchor: x={anchor_x}, y={anchor_y} (detected crystal at {gx:.1f}, {gy:.1f})")

if __name__ == "__main__":
    main()
