#!/usr/bin/env python3
"""
Generates all 38 animation states (98 sprite frames) + skin.json for the 'Mage' (Arcane Mage) skin.
High-definition smooth 2D digital art (96x96 px) with anti-aliasing, rich layered robes,
flaming turquoise animated eyes, twisted wooden staff with a glowing crystal, and physics-driven motion.
Pure Python standard library (math + zlib + struct).
"""
import math
import os
import struct
import zlib
from pathlib import Path

W, H = 96, 96

# =========================================================================
# DRAWING PRIMITIVES WITH ANTI-ALIASING & ALPHA BLENDING
# =========================================================================
def blend_pixel(canvas, x, y, r, g, b, a):
    if not (0 <= x < W and 0 <= y < H) or a <= 0:
        return
    idx = (y * W + x) * 4
    if a >= 255:
        canvas[idx]   = r
        canvas[idx+1] = g
        canvas[idx+2] = b
        canvas[idx+3] = 255
    else:
        alpha = a / 255.0
        inv = 1.0 - alpha
        bg_a = canvas[idx+3] / 255.0
        out_a = alpha + bg_a * inv
        if out_a > 0:
            canvas[idx]   = min(255, int((r * alpha + canvas[idx] * bg_a * inv) / out_a))
            canvas[idx+1] = min(255, int((g * alpha + canvas[idx+1] * bg_a * inv) / out_a))
            canvas[idx+2] = min(255, int((b * alpha + canvas[idx+2] * bg_a * inv) / out_a))
            canvas[idx+3] = min(255, int(out_a * 255))

def draw_circle_aa(canvas, cx, cy, radius, r, g, b, a=255, glow_radius=0):
    total_r = radius + glow_radius
    x0 = max(0, int(math.floor(cx - total_r - 1)))
    x1 = min(W, int(math.ceil(cx + total_r + 2)))
    y0 = max(0, int(math.floor(cy - total_r - 1)))
    y1 = min(H, int(math.ceil(cy + total_r + 2)))
    for y in range(y0, y1):
        for x in range(x0, x1):
            d = math.hypot(x - cx, y - cy)
            if d <= radius - 0.5:
                blend_pixel(canvas, x, y, r, g, b, a)
            elif d < radius + 0.5:
                cov = (radius + 0.5 - d)
                blend_pixel(canvas, x, y, r, g, b, int(a * cov))
            elif glow_radius > 0 and d < total_r:
                factor = (1.0 - (d - radius) / glow_radius) ** 2
                blend_pixel(canvas, x, y, r, g, b, int(a * factor * 0.45))

def draw_ellipse_aa(canvas, cx, cy, rx, ry, r, g, b, a=255, angle=0):
    cos_a = math.cos(-angle)
    sin_a = math.sin(-angle)
    max_r = max(rx, ry) + 2
    x0 = max(0, int(math.floor(cx - max_r)))
    x1 = min(W, int(math.ceil(cx + max_r)))
    y0 = max(0, int(math.floor(cy - max_r)))
    y1 = min(H, int(math.ceil(cy + max_r)))
    for y in range(y0, y1):
        for x in range(x0, x1):
            dx = x - cx
            dy = y - cy
            nx = (dx * cos_a - dy * sin_a) / max(rx, 0.1)
            ny = (dx * sin_a + dy * cos_a) / max(ry, 0.1)
            dist_sq = nx * nx + ny * ny
            if dist_sq <= 0.82:
                blend_pixel(canvas, x, y, r, g, b, a)
            elif dist_sq < 1.18:
                factor = max(0.0, min(1.0, (1.18 - dist_sq) / 0.36))
                blend_pixel(canvas, x, y, r, g, b, int(a * factor))

def draw_capsule_line(canvas, x0, y0, x1, y1, width, r, g, b, a=255):
    dx = x1 - x0
    dy = y1 - y0
    l_sq = dx * dx + dy * dy
    rad = width / 2.0
    bx0 = max(0, int(min(x0, x1) - rad - 1))
    bx1 = min(W, int(max(x0, x1) + rad + 2))
    by0 = max(0, int(min(y0, y1) - rad - 1))
    by1 = min(H, int(max(y0, y1) + rad + 2))
    for y in range(by0, by1):
        for x in range(bx0, bx1):
            if l_sq == 0:
                d = math.hypot(x - x0, y - y0)
            else:
                t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / l_sq))
                d = math.hypot(x - (x0 + t * dx), y - (y0 + t * dy))
            if d <= rad - 0.5:
                blend_pixel(canvas, x, y, r, g, b, a)
            elif d < rad + 0.5:
                factor = rad + 0.5 - d
                blend_pixel(canvas, x, y, r, g, b, int(a * factor))

def draw_polygon(canvas, points, r, g, b, a=255):
    if len(points) < 3: return
    min_y = max(0, int(math.floor(min(p[1] for p in points))))
    max_y = min(H - 1, int(math.ceil(max(p[1] for p in points))))
    n = len(points)
    for y in range(min_y, max_y + 1):
        intersections = []
        py = y + 0.5
        for i in range(n):
            p1 = points[i]
            p2 = points[(i + 1) % n]
            if (p1[1] <= py < p2[1]) or (p2[1] <= py < p1[1]):
                if abs(p2[1] - p1[1]) > 1e-5:
                    ix = p1[0] + (py - p1[1]) * (p2[0] - p1[0]) / (p2[1] - p1[1])
                    intersections.append(ix)
        intersections.sort()
        for i in range(0, len(intersections) - 1, 2):
            x_start = max(0, int(math.floor(intersections[i])))
            x_end = min(W - 1, int(math.ceil(intersections[i + 1])))
            for x in range(x_start, x_end + 1):
                blend_pixel(canvas, x, y, r, g, b, a)

def write_png(canvas, path):
    raw = bytearray()
    for y in range(H):
        raw.append(0)
        raw.extend(canvas[y*W*4 : (y+1)*W*4])
    compressed = zlib.compress(bytes(raw), 9)
    ihdr = struct.pack('>IIBBBBB', W, H, 8, 6, 0, 0, 0)
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(png)

# =========================================================================
# COLOR PALETTE TOKENS (Variant 4: Arcane Mage)
# =========================================================================
C_ROBE_OUTLINE = (20, 10, 35)      # Deep shadow outline
C_ROBE_SHADOW  = (38, 20, 62)      # Nightshade shadow
C_ROBE_BASE    = (65, 34, 102)     # Rich royal dark purple
C_ROBE_MID     = (90, 48, 140)     # Vibrant violet drape
C_ROBE_LIGHT   = (126, 74, 186)    # Mystical highlight
C_HOOD_VOID    = (12, 6, 20)       # Dark face cavity
C_GOLD_TRIM    = (240, 185, 55)    # Arcane embroidery
C_GOLD_SHADOW  = (180, 130, 30)

C_WOOD_DARK    = (45, 26, 16)      # Ancient gnarled branch
C_WOOD_MID     = (82, 50, 30)      # Weathered bark
C_WOOD_LIGHT   = (120, 78, 48)

# Core Magic: Electric Cyan / Turquoise
C_CYAN_WHITE   = (230, 255, 255)
C_CYAN_CORE    = (0, 229, 255)
C_CYAN_TEAL    = (0, 172, 193)
C_CYAN_GLOW    = (0, 210, 245)

# Alerts / Modes
C_RED_CORE     = (255, 220, 220)
C_RED_MAGIC    = (255, 36, 72)
C_RED_GLOW     = (255, 80, 110)

C_GREEN_CORE   = (220, 255, 230)
C_GREEN_MAGIC  = (0, 230, 118)
C_GREEN_GLOW   = (60, 255, 160)

C_YELLOW_CORE  = (255, 255, 220)
C_YELLOW_MAGIC = (255, 214, 0)
C_YELLOW_GLOW  = (255, 235, 100)

# =========================================================================
# CHARACTER RENDER ENGINE
# =========================================================================
def render_arcane_mage(state, frame_idx):
    c = bytearray(W * H * 4)

    # 1. State physics & parameters
    bob_y = 0
    robe_sway = 0.0
    staff_mode = "right_vertical"  # right_vertical, high_up, spinning, two_handed, poking, hidden
    staff_angle = 0.0
    magic_color = "cyan"           # cyan, red, green, yellow
    eye_mode = "flame_normal"
    eye_offset = (0, 0)
    extra_prop = None              # "book", "paper_blast", "bed_sleep", "shockwave", "flower", "cat", "dog"
    aura_type = "normal"

    # Physics tuning across states
    if state == "idle":
        bobs = [0, -2, -3, -1]
        bob_y = bobs[frame_idx % 4]
        robe_sway = math.sin(frame_idx * math.pi / 2.0) * 0.12
        staff_angle = math.sin(frame_idx * math.pi / 2.0) * 0.05
    elif state == "sleep":
        bob_y = [1, -1][frame_idx % 2]
        staff_mode = "horizontal_cuddled"
        magic_color = "yellow"
        eye_mode = "closed_peaceful"
        extra_prop = "bed_sleep"
        aura_type = "stars"
    elif state == "yawn":  # 5 min idle: Reading ancient grimoire
        bob_y = [-1, 1][frame_idx % 2]
        staff_mode = "hidden"
        extra_prop = "book"
        eye_mode = "reading_down"
        aura_type = "floating_runes"
    elif state == "wake":  # Waking up / snapping book shut
        bob_y = [2, -2][frame_idx % 2]
        staff_mode = "right_vertical"
        eye_mode = "flame_flare"
        aura_type = "spark_burst"
    elif state == "notice":  # Message received: Crimson red, staff held high!
        bob_y = -3
        staff_mode = "high_up"
        magic_color = "red"
        eye_mode = "flame_fierce"
        aura_type = "radial_burst"
    elif state == "twirl":  # Printing document: spinning green staff!
        step = (frame_idx / 8.0) * math.tau
        bob_y = int(math.sin(step) * 2)
        staff_mode = "spinning"
        staff_angle = step
        magic_color = "green"
        eye_mode = "flame_normal"
        aura_type = "green_ring"
    elif state == "action":  # Submitting ticket: paper blast!
        staff_mode = "thrust_forward"
        extra_prop = "paper_blast"
        eye_mode = "flame_flare"
        aura_type = "spark_burst"
    elif state == "busy":  # Writing ticket with quill
        staff_mode = "hidden"
        extra_prop = "writing_quill"
        eye_mode = "reading_down"
    elif state == "workout":  # Emergency stance & ground slam!
        if frame_idx < 4:
            bob_y = 2
            staff_mode = "two_handed_crouch"
            eye_mode = "flame_fierce"
        else:
            bob_y = -1
            staff_mode = "slam_down"
            extra_prop = "shockwave"
            magic_color = "red" if frame_idx % 2 == 0 else "yellow"
            eye_mode = "flame_flare"
    elif state == "surprise":  # Cursor hover > 5 min -> poke cursor!
        bob_y = -2
        if frame_idx == 0:
            staff_mode = "right_vertical"
            eye_mode = "huge_round"
        else:
            staff_mode = "poking"
            eye_mode = "huge_round"
            aura_type = "boop_spark"
    elif state.startswith("look"):
        # Directional gaze following cursor
        look_map = {
            "lookleft": (-3, 0),
            "lookright": (3, 0),
            "lookup": (0, -3),
            "lookdown": (0, 3),
            "lookupleft": (-2.5, -2.5),
            "lookupright": (2.5, -2.5),
            "lookdownleft": (-2.5, 2.5),
            "lookdownright": (2.5, 2.5),
        }
        eye_offset = look_map.get(state, (0, 0))
        eye_mode = "flame_directional"
    elif state == "joy":
        bob_y = [-3, 1][frame_idx % 2]
        eye_mode = "joy_arcs"
        staff_mode = "high_up"
        aura_type = "stars"
    elif state == "sad":
        bob_y = 3
        eye_mode = "sad_flames"
        staff_angle = -0.15
    elif state == "laugh":
        bob_y = [-2, 2][frame_idx % 2]
        eye_mode = "laugh_crescents"
        staff_angle = 0.1
    elif state == "think":
        eye_mode = "think_up"
        aura_type = "floating_runes"
    elif state == "celebrate":
        bob_y = [-4, 0][frame_idx % 2]
        staff_mode = "high_up"
        eye_mode = "joy_arcs"
        aura_type = "confetti"
    elif state == "offended":
        eye_mode = "look_away"
        staff_mode = "staff_on_back"
    elif state == "facepalm":
        eye_mode = "dimmed"
        extra_prop = "hand_to_face"
    elif state == "flower":
        extra_prop = "flower"
        eye_mode = "joy_arcs"
    elif state == "cry":
        eye_mode = "crying_tears"
        bob_y = 2
    elif state == "turnback":
        eye_mode = "hidden_back"
        staff_mode = "staff_on_back"
    elif state == "shy":
        eye_mode = "shy_glance"
        bob_y = 1
    elif state == "petcat":
        extra_prop = "cat"
        eye_mode = "joy_arcs"
    elif state == "petdog":
        extra_prop = "dog"
        eye_mode = "joy_arcs"
    elif state == "drag":
        bob_y = 4
        robe_sway = -0.3
        staff_angle = -0.4
        eye_mode = "huge_round"
    elif state == "dizzy":
        eye_mode = "spiral"
        robe_sway = math.sin(frame_idx * math.pi) * 0.25
        staff_angle = math.cos(frame_idx * math.pi) * 0.3
    elif state == "charging":
        bob_y = -frame_idx
        aura_type = "charging_nova"
        magic_color = "cyan"
        eye_mode = "flame_flare"
    elif state == "dance":
        angle_step = (frame_idx / 8.0) * math.tau
        bob_y = int(math.sin(angle_step) * 3)
        robe_sway = math.sin(angle_step) * 0.2
        staff_angle = math.cos(angle_step) * 0.25
        aura_type = "stars"
    elif state == "greeting":
        staff_mode = "right_vertical"
        extra_prop = "hand_wave"
        eye_mode = "joy_arcs"
    elif state == "success":
        staff_mode = "high_up"
        aura_type = "victory_ring"
        eye_mode = "joy_arcs"
    elif state == "error":
        staff_angle = -0.2
        aura_type = "error_smoke"
        eye_mode = "dimmed"

    # Color picking helper
    if magic_color == "red":
        col_core, col_magic, col_glow = C_RED_CORE, C_RED_MAGIC, C_RED_GLOW
    elif magic_color == "green":
        col_core, col_magic, col_glow = C_GREEN_CORE, C_GREEN_MAGIC, C_GREEN_GLOW
    elif magic_color == "yellow":
        col_core, col_magic, col_glow = C_YELLOW_CORE, C_YELLOW_MAGIC, C_YELLOW_GLOW
    else:
        col_core, col_magic, col_glow = C_CYAN_WHITE, C_CYAN_CORE, C_CYAN_GLOW

    # Base coords
    base_x = 48
    base_y = 52 + bob_y

    # =====================================================================
    # 2. BACKGROUND AURAS / PROPS (Enchanted Bed, Shockwave, Rings)
    # =====================================================================
    if extra_prop == "bed_sleep":
        # Magical glowing floating cloud-bed behind wizard
        draw_ellipse_aa(c, base_x, base_y + 10, 36, 16, 40, 25, 70, a=220)
        draw_ellipse_aa(c, base_x, base_y + 8, 33, 13, 75, 45, 120, a=240)
        draw_ellipse_aa(c, base_x - 18, base_y + 4, 12, 8, 220, 215, 235, a=240)  # Pillow
        # Magical floating runes around bed
        draw_circle_aa(c, base_x + 24, base_y - 12, 2.5, col_magic[0], col_magic[1], col_magic[2], glow_radius=4)
        draw_circle_aa(c, base_x - 22, base_y - 18, 2.0, col_magic[0], col_magic[1], col_magic[2], glow_radius=3)
        draw_circle_aa(c, base_x + 10, base_y - 24, 1.8, 255, 255, 200, glow_radius=3)

    if extra_prop == "shockwave":
        # Expanding ground shockwave from staff slam
        draw_ellipse_aa(c, base_x + 16, 88, 30, 8, col_magic[0], col_magic[1], col_magic[2], a=180)
        draw_ellipse_aa(c, base_x + 16, 88, 26, 6, col_core[0], col_core[1], col_core[2], a=220)
        draw_circle_aa(c, base_x + 16, 86, 6, 255, 255, 255, glow_radius=12)

    if aura_type == "green_ring":
        # Twirling spinning circular energy ribbon
        draw_ellipse_aa(c, base_x + 8, base_y, 34, 30, col_magic[0], col_magic[1], col_magic[2], a=130)
        draw_ellipse_aa(c, base_x + 8, base_y, 31, 27, 0, 0, 0, a=255)  # Cutout hole for ring effect

    if aura_type == "charging_nova":
        # Arcane nova gathering at feet and crystal
        draw_ellipse_aa(c, base_x, 86, 28, 8, col_magic[0], col_magic[1], col_magic[2], a=160)
        draw_circle_aa(c, base_x, 86, 4, 255, 255, 255, glow_radius=8)

    # =====================================================================
    # 3. BACK CAPE & CLOTH PHYSICS
    # =====================================================================
    # Flowing lower mantle / back cape
    cape_spread = 22 + math.sin(frame_idx * 1.5) * 2
    cape_tip_l = (base_x - cape_spread - robe_sway * 15, base_y + 36)
    cape_tip_r = (base_x + cape_spread - robe_sway * 15, base_y + 36)
    cape_pts = [
        (base_x - 14, base_y - 4),
        (base_x + 14, base_y - 4),
        (cape_tip_r[0] + 4, cape_tip_r[1]),
        (base_x, base_y + 32),
        (cape_tip_l[0] - 4, cape_tip_l[1]),
    ]
    draw_polygon(c, cape_pts, C_ROBE_OUTLINE[0], C_ROBE_OUTLINE[1], C_ROBE_OUTLINE[2], 255)
    cape_inner = [
        (base_x - 12, base_y - 2),
        (base_x + 12, base_y - 2),
        (cape_tip_r[0] + 1, cape_tip_r[1] - 2),
        (base_x, base_y + 30),
        (cape_tip_l[0] - 1, cape_tip_l[1] - 2),
    ]
    draw_polygon(c, cape_inner, C_ROBE_SHADOW[0], C_ROBE_SHADOW[1], C_ROBE_SHADOW[2], 255)

    # =====================================================================
    # 4. STAFF (If behind body or spinning)
    # =====================================================================
    def draw_gem(gx, gy, scale=1.0):
        # Gemstone crystal: faceted rhombus
        r_w = 5.5 * scale
        r_h = 9.0 * scale
        pts_gem = [
            (gx, gy - r_h),
            (gx + r_w, gy),
            (gx, gy + r_h),
            (gx - r_w, gy),
        ]
        # Glow
        draw_circle_aa(c, gx, gy, 6 * scale, col_magic[0], col_magic[1], col_magic[2], a=190, glow_radius=10 * scale)
        draw_polygon(c, pts_gem, col_magic[0], col_magic[1], col_magic[2], 255)
        # Inner faceted highlight
        pts_facet = [
            (gx, gy - r_h * 0.75),
            (gx + r_w * 0.5, gy),
            (gx, gy + r_h * 0.75),
            (gx - r_w * 0.5, gy),
        ]
        draw_polygon(c, pts_facet, col_core[0], col_core[1], col_core[2], 255)
        # Magic sparks
        draw_circle_aa(c, gx + 5 * scale, gy - 6 * scale, 1.2 * scale, 255, 255, 255, glow_radius=3)
        draw_circle_aa(c, gx - 4 * scale, gy + 5 * scale, 1.0 * scale, col_magic[0], col_magic[1], col_magic[2], glow_radius=2)

    staff_gem_pos = None

    if staff_mode == "staff_on_back":
        # Staff strapped diagonally across back
        sx0, sy0 = base_x - 18, base_y + 30
        sx1, sy1 = base_x + 18, base_y - 28
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.0, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.5, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1, 0.9)
        staff_gem_pos = (sx1, sy1)

    # =====================================================================
    # 5. ROBE BODY (Main Front Layer)
    # =====================================================================
    # Main torso & floating robes
    body_pts = [
        (base_x - 15, base_y - 4),
        (base_x + 15, base_y - 4),
        (base_x + 19 + robe_sway * 8, base_y + 28),
        (base_x + 8 + robe_sway * 5, base_y + 31),
        (base_x - 8 + robe_sway * 5, base_y + 31),
        (base_x - 19 + robe_sway * 8, base_y + 28),
    ]
    draw_polygon(c, body_pts, C_ROBE_OUTLINE[0], C_ROBE_OUTLINE[1], C_ROBE_OUTLINE[2], 255)

    body_inner = [
        (base_x - 13, base_y - 2),
        (base_x + 13, base_y - 2),
        (base_x + 17 + robe_sway * 8, base_y + 26),
        (base_x + 7 + robe_sway * 5, base_y + 29),
        (base_x - 7 + robe_sway * 5, base_y + 29),
        (base_x - 17 + robe_sway * 8, base_y + 26),
    ]
    draw_polygon(c, body_inner, C_ROBE_BASE[0], C_ROBE_BASE[1], C_ROBE_BASE[2], 255)

    # Center drapery crease and highlight
    draw_capsule_line(c, base_x, base_y - 2, base_x + robe_sway * 6, base_y + 28, 3.0, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 220)
    draw_capsule_line(c, base_x - 6, base_y + 4, base_x - 10 + robe_sway * 6, base_y + 26, 2.0, C_ROBE_LIGHT[0], C_ROBE_LIGHT[1], C_ROBE_LIGHT[2], 160)

    # Gold embroidered hem at bottom
    draw_capsule_line(c, base_x - 17 + robe_sway * 8, base_y + 27, base_x + 17 + robe_sway * 8, base_y + 27, 2.0, C_GOLD_TRIM[0], C_GOLD_TRIM[1], C_GOLD_TRIM[2], 220)

    # Arcane sash / belt
    draw_ellipse_aa(c, base_x, base_y + 6, 14, 3.5, C_ROBE_OUTLINE[0], C_ROBE_OUTLINE[1], C_ROBE_OUTLINE[2], 240)
    draw_ellipse_aa(c, base_x, base_y + 6, 13, 2.2, C_GOLD_TRIM[0], C_GOLD_TRIM[1], C_GOLD_TRIM[2], 255)
    draw_circle_aa(c, base_x, base_y + 6, 3.0, col_magic[0], col_magic[1], col_magic[2], a=240, glow_radius=4)

    # =====================================================================
    # 6. HOOD & HEAD
    # =====================================================================
    hood_cx = base_x
    hood_cy = base_y - 14

    # Pointed hood cone
    hood_pts = [
        (hood_cx - 19, hood_cy + 8),
        (hood_cx + 19, hood_cy + 8),
        (hood_cx + 14, hood_cy - 8),
        (hood_cx + 3 + math.sin(frame_idx * 0.8) * 2, hood_cy - 24),  # Tip curving back
        (hood_cx - 14, hood_cy - 8),
    ]
    draw_polygon(c, hood_pts, C_ROBE_OUTLINE[0], C_ROBE_OUTLINE[1], C_ROBE_OUTLINE[2], 255)

    hood_inner = [
        (hood_cx - 17, hood_cy + 7),
        (hood_cx + 17, hood_cy + 7),
        (hood_cx + 12, hood_cy - 7),
        (hood_cx + 3 + math.sin(frame_idx * 0.8) * 2, hood_cy - 22),
        (hood_cx - 12, hood_cy - 7),
    ]
    draw_polygon(c, hood_inner, C_ROBE_BASE[0], C_ROBE_BASE[1], C_ROBE_BASE[2], 255)

    # Gold rim around hood edge
    draw_capsule_line(c, hood_cx - 17, hood_cy + 8, hood_cx + 17, hood_cy + 8, 2.5, C_GOLD_TRIM[0], C_GOLD_TRIM[1], C_GOLD_TRIM[2], 240)

    # Pitch-dark void inside the hood
    draw_ellipse_aa(c, hood_cx, hood_cy + 2, 14, 9, C_ROBE_OUTLINE[0], C_ROBE_OUTLINE[1], C_ROBE_OUTLINE[2], 255)
    draw_ellipse_aa(c, hood_cx, hood_cy + 2, 12, 7.5, C_HOOD_VOID[0], C_HOOD_VOID[1], C_HOOD_VOID[2], 255)

    # =====================================================================
    # 7. ANIMATED FLAME EYES
    # =====================================================================
    lx = hood_cx - 5.5 + eye_offset[0]
    rx = hood_cx + 5.5 + eye_offset[0]
    ey = hood_cy + 2 + eye_offset[1]

    def draw_eye_flame(x, y, flip=False, size=1.0):
        # Fire-shaped almond eyes flickering
        flicker_y = (math.sin(frame_idx * 3.14 + (1 if flip else 0)) * 0.6)
        draw_circle_aa(c, x, y, 3.5 * size, col_magic[0], col_magic[1], col_magic[2], a=220, glow_radius=6 * size)
        draw_ellipse_aa(c, x, y + flicker_y, 2.8 * size, 4.0 * size, col_magic[0], col_magic[1], col_magic[2], 255, angle=-0.2 if flip else 0.2)
        draw_ellipse_aa(c, x, y + flicker_y, 1.4 * size, 2.5 * size, col_core[0], col_core[1], col_core[2], 255, angle=-0.2 if flip else 0.2)

    if eye_mode == "flame_normal" or eye_mode == "flame_directional":
        draw_eye_flame(lx, ey, flip=False, size=1.0)
        draw_eye_flame(rx, ey, flip=True, size=1.0)
    elif eye_mode == "flame_flare":
        draw_eye_flame(lx, ey, flip=False, size=1.35)
        draw_eye_flame(rx, ey, flip=True, size=1.35)
    elif eye_mode == "flame_fierce":
        draw_eye_flame(lx, ey, flip=False, size=1.2)
        draw_eye_flame(rx, ey, flip=True, size=1.2)
        # Narrow intense brow shadow
        draw_capsule_line(c, lx - 4, ey - 3, lx + 3, ey - 1, 2.0, C_HOOD_VOID[0], C_HOOD_VOID[1], C_HOOD_VOID[2], 255)
        draw_capsule_line(c, rx + 4, ey - 3, rx - 3, ey - 1, 2.0, C_HOOD_VOID[0], C_HOOD_VOID[1], C_HOOD_VOID[2], 255)
    elif eye_mode == "huge_round":
        # Surprised wide round eyes
        draw_circle_aa(c, lx, ey, 4.0, col_magic[0], col_magic[1], col_magic[2], 255, glow_radius=6)
        draw_circle_aa(c, lx, ey, 2.0, col_core[0], col_core[1], col_core[2], 255)
        draw_circle_aa(c, rx, ey, 4.0, col_magic[0], col_magic[1], col_magic[2], 255, glow_radius=6)
        draw_circle_aa(c, rx, ey, 2.0, col_core[0], col_core[1], col_core[2], 255)
    elif eye_mode == "closed_peaceful":
        # Gently curved shut slits for sleep
        draw_capsule_line(c, lx - 3, ey, lx + 3, ey, 1.8, col_magic[0], col_magic[1], col_magic[2], 240)
        draw_capsule_line(c, rx - 3, ey, rx + 3, ey, 1.8, col_magic[0], col_magic[1], col_magic[2], 240)
    elif eye_mode == "reading_down":
        # Focused downward reading slits
        draw_capsule_line(c, lx - 3, ey + 1, lx + 2, ey + 3, 2.0, col_magic[0], col_magic[1], col_magic[2], 240)
        draw_capsule_line(c, rx + 3, ey + 1, rx - 2, ey + 3, 2.0, col_magic[0], col_magic[1], col_magic[2], 240)
    elif eye_mode == "joy_arcs":
        # Upward smiling crescent eyes ^^
        draw_capsule_line(c, lx - 3, ey + 1, lx, ey - 2, 2.0, col_magic[0], col_magic[1], col_magic[2], 240)
        draw_capsule_line(c, lx, ey - 2, lx + 3, ey + 1, 2.0, col_magic[0], col_magic[1], col_magic[2], 240)
        draw_capsule_line(c, rx - 3, ey + 1, rx, ey - 2, 2.0, col_magic[0], col_magic[1], col_magic[2], 240)
        draw_capsule_line(c, rx, ey - 2, rx + 3, ey + 1, 2.0, col_magic[0], col_magic[1], col_magic[2], 240)
    elif eye_mode == "sad_flames":
        draw_ellipse_aa(c, lx, ey + 1, 2.5, 2.0, col_magic[0], col_magic[1], col_magic[2], 220)
        draw_ellipse_aa(c, rx, ey + 1, 2.5, 2.0, col_magic[0], col_magic[1], col_magic[2], 220)
    elif eye_mode == "laugh_crescents":
        draw_capsule_line(c, lx - 3, ey, lx + 3, ey - 2, 2.0, col_magic[0], col_magic[1], col_magic[2], 255)
        draw_capsule_line(c, rx - 3, ey - 2, rx + 3, ey, 2.0, col_magic[0], col_magic[1], col_magic[2], 255)
    elif eye_mode == "think_up":
        draw_circle_aa(c, lx, ey - 2, 3.0, col_magic[0], col_magic[1], col_magic[2], 240, glow_radius=4)
        draw_circle_aa(c, rx, ey - 2, 3.0, col_magic[0], col_magic[1], col_magic[2], 240, glow_radius=4)
    elif eye_mode == "look_away":
        draw_ellipse_aa(c, lx - 2, ey, 2.0, 3.0, col_magic[0], col_magic[1], col_magic[2], 240)
        draw_ellipse_aa(c, rx - 2, ey, 2.0, 3.0, col_magic[0], col_magic[1], col_magic[2], 240)
    elif eye_mode == "spiral":
        draw_circle_aa(c, lx, ey, 3.5, col_magic[0], col_magic[1], col_magic[2], 220)
        draw_circle_aa(c, lx, ey, 1.8, C_HOOD_VOID[0], C_HOOD_VOID[1], C_HOOD_VOID[2], 255)
        draw_circle_aa(c, rx, ey, 3.5, col_magic[0], col_magic[1], col_magic[2], 220)
        draw_circle_aa(c, rx, ey, 1.8, C_HOOD_VOID[0], C_HOOD_VOID[1], C_HOOD_VOID[2], 255)
    elif eye_mode == "dimmed":
        draw_ellipse_aa(c, lx, ey, 2.0, 2.0, col_magic[0], col_magic[1], col_magic[2], 140)
        draw_ellipse_aa(c, rx, ey, 2.0, 2.0, col_magic[0], col_magic[1], col_magic[2], 140)
    elif eye_mode == "crying_tears":
        draw_circle_aa(c, lx, ey, 2.5, col_magic[0], col_magic[1], col_magic[2], 220)
        draw_circle_aa(c, rx, ey, 2.5, col_magic[0], col_magic[1], col_magic[2], 220)
        # Flowing tears
        draw_capsule_line(c, lx, ey + 3, lx - 2, ey + 11, 1.8, C_CYAN_WHITE[0], C_CYAN_WHITE[1], C_CYAN_WHITE[2], 250)
        draw_capsule_line(c, rx, ey + 3, rx + 2, ey + 11, 1.8, C_CYAN_WHITE[0], C_CYAN_WHITE[1], C_CYAN_WHITE[2], 250)

    # =====================================================================
    # 8. FOREGROUND PROPS (Book, Writing Quill, Flower, Pets)
    # =====================================================================
    if extra_prop == "book":
        # Open ancient spellbook floating in front of wizard
        bk_x, bk_y = base_x, base_y + 12
        draw_polygon(c, [(bk_x - 16, bk_y - 8), (bk_x, bk_y - 3), (bk_x, bk_y + 10), (bk_x - 16, bk_y + 5)], 110, 60, 30, 255) # Left cover
        draw_polygon(c, [(bk_x + 16, bk_y - 8), (bk_x, bk_y - 3), (bk_x, bk_y + 10), (bk_x + 16, bk_y + 5)], 110, 60, 30, 255) # Right cover
        draw_polygon(c, [(bk_x - 14, bk_y - 7), (bk_x, bk_y - 2), (bk_x, bk_y + 8), (bk_x - 14, bk_y + 3)], 245, 235, 210, 255) # Left page
        draw_polygon(c, [(bk_x + 14, bk_y - 7), (bk_x, bk_y - 2), (bk_x, bk_y + 8), (bk_x + 14, bk_y + 3)], 245, 235, 210, 255) # Right page
        # Floating magic glyphs above book
        draw_circle_aa(c, bk_x - 5, bk_y - 12, 1.8, col_magic[0], col_magic[1], col_magic[2], glow_radius=3)
        draw_circle_aa(c, bk_x + 6, bk_y - 14, 2.2, col_magic[0], col_magic[1], col_magic[2], glow_radius=4)
        # Wizard hands holding book edges
        draw_ellipse_aa(c, bk_x - 12, bk_y + 2, 3.5, 3.0, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 255)
        draw_ellipse_aa(c, bk_x + 12, bk_y + 2, 3.5, 3.0, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 255)

    if extra_prop == "writing_quill":
        # Holding scroll & feather quill
        draw_polygon(c, [(base_x - 12, base_y + 4), (base_x + 4, base_y + 4), (base_x + 4, base_y + 18), (base_x - 12, base_y + 18)], 245, 235, 210, 255)
        draw_capsule_line(c, base_x + 4, base_y + 12, base_x + 14, base_y - 4, 2.0, 230, 230, 250, 255)

    if extra_prop == "paper_blast":
        # Blasting sheet of paper
        draw_polygon(c, [(base_x + 18, base_y - 16), (base_x + 30, base_y - 20), (base_x + 32, base_y - 8), (base_x + 20, base_y - 4)], 255, 255, 255, 240)
        # Energy bolt from staff
        draw_capsule_line(c, base_x + 8, base_y - 2, base_x + 24, base_y - 14, 3.5, col_core[0], col_core[1], col_core[2], 255)
        draw_circle_aa(c, base_x + 24, base_y - 14, 6.0, col_magic[0], col_magic[1], col_magic[2], glow_radius=10)

    if extra_prop == "flower":
        draw_capsule_line(c, base_x - 10, base_y + 10, base_x - 18, base_y + 2, 2.0, 50, 180, 80, 255)
        draw_circle_aa(c, base_x - 19, base_y + 1, 4.0, 0, 229, 255, glow_radius=6)
        draw_circle_aa(c, base_x - 19, base_y + 1, 2.0, 255, 255, 255, 255)

    if extra_prop == "cat":
        # Spirit cat on arm
        draw_ellipse_aa(c, base_x - 16, base_y + 8, 8, 6, 120, 210, 255, a=230)
        draw_circle_aa(c, base_x - 22, base_y + 5, 4.5, 120, 210, 255, a=230)
        draw_polygon(c, [(base_x - 24, base_y + 3), (base_x - 26, base_y - 2), (base_x - 22, base_y + 1)], 120, 210, 255, 240) # Ear

    if extra_prop == "dog":
        # Spirit pup
        draw_ellipse_aa(c, base_x - 18, base_y + 14, 10, 7, 255, 190, 120, a=230)
        draw_circle_aa(c, base_x - 24, base_y + 10, 5.5, 255, 190, 120, a=230)

    # =====================================================================
    # 9. FOREGROUND STAFF RENDERING
    # =====================================================================
    if staff_mode == "right_vertical":
        # Held vertically in right hand
        sx0 = base_x + 18
        sy0 = base_y + 30
        sx1 = base_x + 20 + math.sin(staff_angle) * 10
        sy1 = base_y - 36
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.5, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.6, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        # Twisted gnarled branch wrap
        draw_capsule_line(c, sx1 - 3, sy1 + 10, sx1 + 3, sy1 + 4, 3.0, C_WOOD_LIGHT[0], C_WOOD_LIGHT[1], C_WOOD_LIGHT[2])
        draw_gem(sx1, sy1 - 2, scale=1.0)
        staff_gem_pos = (sx1, sy1 - 2)
        # Wizard sleeve and hand holding staff
        draw_capsule_line(c, base_x + 8, base_y + 4, sx0 - 2, sy0 - 18, 5.0, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 255)
        draw_ellipse_aa(c, sx0 - 1, sy0 - 18, 4.0, 3.5, C_ROBE_LIGHT[0], C_ROBE_LIGHT[1], C_ROBE_LIGHT[2], 255)

    elif staff_mode == "high_up":
        # Raised high overhead
        sx0 = base_x + 10
        sy0 = base_y + 6
        sx1 = base_x + 14
        sy1 = 12
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.5, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.6, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1 - 2, scale=1.3)
        staff_gem_pos = (sx1, sy1 - 2)
        draw_capsule_line(c, base_x + 4, base_y - 4, sx0, sy0, 5.0, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 255)

    elif staff_mode == "spinning":
        # Twirling staff in center
        spin_cx = base_x + 4
        spin_cy = base_y
        length = 42
        sx0 = spin_cx - math.cos(staff_angle) * length
        sy0 = spin_cy - math.sin(staff_angle) * length
        sx1 = spin_cx + math.cos(staff_angle) * length
        sy1 = spin_cy + math.sin(staff_angle) * length
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.0, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.4, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1, scale=1.0)
        staff_gem_pos = (sx1, sy1)

    elif staff_mode == "two_handed_crouch":
        # Holding diagonally with two hands
        sx0 = base_x - 14
        sy0 = base_y + 26
        sx1 = base_x + 22
        sy1 = base_y - 28
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.5, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.6, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1, scale=1.1)
        staff_gem_pos = (sx1, sy1)
        draw_ellipse_aa(c, base_x - 2, base_y + 8, 4.0, 3.5, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 255)
        draw_ellipse_aa(c, base_x + 8, base_y - 4, 4.0, 3.5, C_ROBE_MID[0], C_ROBE_MID[1], C_ROBE_MID[2], 255)

    elif staff_mode == "slam_down":
        # Slammed vertically down into floor
        sx0 = base_x + 16
        sy0 = 86
        sx1 = base_x + 14
        sy1 = base_y - 30
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 5.0, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 3.0, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1, scale=1.2)
        staff_gem_pos = (sx1, sy1)

    elif staff_mode == "poking":
        # Poking staff forward toward user cursor!
        sx0 = base_x + 8
        sy0 = base_y + 8
        sx1 = base_x + 38
        sy1 = base_y - 12
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.5, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.6, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1, scale=1.1)
        staff_gem_pos = (sx1, sy1)
        # Spark at the tip poking cursor
        draw_circle_aa(c, sx1 + 4, sy1 - 2, 4.0, 255, 255, 255, glow_radius=8)

    elif staff_mode == "thrust_forward":
        sx0 = base_x + 6
        sy0 = base_y + 10
        sx1 = base_x + 28
        sy1 = base_y - 20
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.5, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_gem(sx1, sy1, scale=1.1)
        staff_gem_pos = (sx1, sy1)

    elif staff_mode == "horizontal_cuddled":
        # Held horizontally sleeping on bed
        sx0 = base_x - 22
        sy0 = base_y + 16
        sx1 = base_x + 24
        sy1 = base_y + 2
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 4.0, C_WOOD_DARK[0], C_WOOD_DARK[1], C_WOOD_DARK[2])
        draw_capsule_line(c, sx0, sy0, sx1, sy1, 2.4, C_WOOD_MID[0], C_WOOD_MID[1], C_WOOD_MID[2])
        draw_gem(sx1, sy1, scale=0.9)
        staff_gem_pos = (sx1, sy1)

    return c, staff_gem_pos

# =========================================================================
# BATCH GENERATION FOR ALL 38 STATES (98 FRAMES)
# =========================================================================
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
    print(f"Generating Arcane Mage skin into: {root}")

    total_frames = 0
    gem_pos_idle = None

    for state, count in STATE_FRAME_COUNTS.items():
        for f in range(count):
            canvas, gem_pos = render_arcane_mage(state, f)
            frame_num = f + 1
            filename = f"{state}_{frame_num}.png"
            write_png(canvas, root / filename)
            total_frames += 1
            if state == "idle" and f == 0 and gem_pos:
                gem_pos_idle = gem_pos

    # Calculate Badge Anchor for skin.json (normalized to 48x48 virtual coordinates, scale = 2)
    # If gem_pos_idle is around (68, 14), normalized is (34, 7)
    gx, gy = gem_pos_idle if gem_pos_idle else (68, 14)
    badge_anchor_x = round(gx / 2.0, 1)
    badge_anchor_y = round((gy - 2) / 2.0, 1)

    # Write skin.json manifest
    skin_json = f"""{{
  "id": "Mage",
  "displayName": "Энергичный чародей (Arcane Mage)",
  "author": "PixelHelper",
  "version": "1.0",
  "spriteWidth": 96,
  "spriteHeight": 96,
  "scalingMode": "HighQuality",
  "badgeAnchor": {{
    "x": {badge_anchor_x},
    "y": {badge_anchor_y}
  }},
  "hatAnchor": {{
    "x": 16.0,
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

    print(f"Successfully generated {total_frames} frames + skin.json for Mage skin!")
    print(f"Calculated Staff Gem Badge Anchor: x={badge_anchor_x}, y={badge_anchor_y}")

if __name__ == "__main__":
    main()
