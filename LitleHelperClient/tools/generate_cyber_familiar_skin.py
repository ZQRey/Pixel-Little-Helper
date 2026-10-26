#!/usr/bin/env python3
"""
Generates all 38 animation states (98 sprite frames) for the CyberFamiliar skin.
High-definition smooth 2D digital art (96x96 px) with anti-aliasing, soft pastel fur,
grounded slate-navy contours, charming anime eyes with sparkling catchlights,
and subtle glowing cyber accents (eye-friendly, non-glaring).
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
                blend_pixel(canvas, x, y, r, g, b, int(a * factor * 0.3))

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
# CHARACTER RENDER ENGINE: CyberFamiliar (Soft, Charming & Eye-Friendly)
# =========================================================================
# Color Tokens:
C_OUTLINE      = (35, 43, 56)      # #232B38 Soft dark slate navy
C_FUR_BASE     = (239, 236, 230)   # #EFECE6 Warm soft pastel cream
C_FUR_SAGE     = (216, 230, 224)   # #D8E6E0 Gentle sage gradient
C_FUR_SHADOW   = (188, 204, 198)   # #BCCC6 Soft ambient shading
C_CHEST_TUFT   = (248, 246, 242)   # #F8F6F2 Light chest fluff
C_EAR_INNER    = (244, 208, 220)   # #F4D0DC Soft warm pastel pink
C_EAR_SHADOW   = (225, 175, 195)   # #E1AFC3
C_CYBER_CYAN   = (56, 189, 248)    # #38BDF8 Soft electric cyan
C_CYBER_TEAL   = (20, 184, 166)    # #14B8A6 Emerald cyan jewel tone
C_BLUSH        = (251, 113, 133)   # #FB7185 Soft rose cheek blush
C_COLLAR       = (30, 41, 59)      # #1E293B Dark navy velvet band

def render_cyber_familiar(state, frame_idx):
    c = bytearray(W * H * 4)
    
    # State parameters
    bob_y = 0
    ear_tilt = 0.0
    tail_sway = 0.0
    eye_state = "open"
    mouth_state = "smile"
    paw_action = "rest"
    glow_intensity = 1.0
    aura_type = "normal"
    prop_type = None

    if state == "idle":
        bobs = [0, -2, 0, 2]
        bob_y = bobs[frame_idx % 4]
        tail_sway = math.sin(frame_idx * math.pi / 2) * 0.22
        if frame_idx == 2: eye_state = "blink"
    elif state == "sleep":
        bob_y = [1, -1][frame_idx % 2]
        eye_state = "closed_happy"
        mouth_state = "sleepy"
        aura_type = "sleep_zzz"
    elif state == "yawn":
        bob_y = -3 if frame_idx == 1 else 0
        eye_state = "closed_squint"
        mouth_state = "yawn"
        ear_tilt = 0.15
    elif state == "wake":
        bob_y = [2, -2][frame_idx % 2]
        eye_state = "sparkle"
        mouth_state = "open"
        ear_tilt = -0.1
    elif state == "drag":
        bob_y = 4
        paw_action = "dangle"
        eye_state = "surprised"
        mouth_state = "small_o"
    elif state == "dizzy":
        eye_state = "spiral"
        mouth_state = "wavy"
        ear_tilt = math.sin(frame_idx * math.pi) * 0.35
    elif state == "lookleft":
        eye_state = "look_left"
        ear_tilt = -0.12
    elif state == "lookright":
        eye_state = "look_right"
        ear_tilt = 0.12
    elif state == "lookup":
        bob_y = -2
        eye_state = "look_up"
    elif state == "lookdown":
        bob_y = 2
        eye_state = "look_down"
    elif state == "lookupleft":
        bob_y = -2
        ear_tilt = -0.1
        eye_state = "look_up_left"
    elif state == "lookupright":
        bob_y = -2
        ear_tilt = 0.1
        eye_state = "look_up_right"
    elif state == "lookdownleft":
        bob_y = 2
        ear_tilt = -0.08
        eye_state = "look_down_left"
    elif state == "lookdownright":
        bob_y = 2
        ear_tilt = 0.08
        eye_state = "look_down_right"
    elif state == "twirl":
        angle_step = (frame_idx / 8.0) * math.tau
        bob_y = int(math.sin(angle_step) * 4)
        tail_sway = math.cos(angle_step) * 0.5
        aura_type = "spin_ring"
    elif state == "offended":
        eye_state = "look_away"
        mouth_state = "pout"
        ear_tilt = 0.25
    elif state == "greeting":
        paw_action = "wave"
        eye_state = "open"
        mouth_state = "smile"
        tail_sway = 0.25 if frame_idx == 1 else -0.25
    elif state == "action":
        eye_state = "sparkle"
        aura_type = "action_burst"
    elif state == "notice":
        bob_y = -3
        eye_state = "sparkle"
        aura_type = "exclamation"
    elif state == "joy":
        bob_y = [-4, 0][frame_idx % 2]
        eye_state = "joy_arcs"
        mouth_state = "wide_smile"
        tail_sway = 0.35
        aura_type = "joy_sparkles"
    elif state == "sad":
        bob_y = 3
        ear_tilt = 0.2
        eye_state = "sad_watery"
        mouth_state = "frown"
    elif state == "laugh":
        bob_y = [-3, 1][frame_idx % 2]
        eye_state = "laugh_brackets"
        mouth_state = "laugh_open"
        paw_action = "paws_to_cheeks"
    elif state == "surprise":
        bob_y = -4
        eye_state = "huge_round"
        mouth_state = "small_o"
        ear_tilt = -0.15
    elif state == "think":
        eye_state = "look_up"
        paw_action = "paw_to_chin"
        aura_type = "think_dots"
    elif state == "celebrate":
        bob_y = [-5, 1][frame_idx % 2]
        eye_state = "joy_arcs"
        mouth_state = "wide_smile"
        paw_action = "paws_up"
        aura_type = "confetti"
    elif state == "facepalm":
        eye_state = "closed_squint"
        paw_action = "paw_to_face"
        mouth_state = "frown"
    elif state == "cry":
        eye_state = "crying_streams"
        mouth_state = "wavy"
        bob_y = 2
    elif state == "workout":
        step = frame_idx % 8
        bob_y = [-3, 0, -5, 2, 4, 1, -2, -4][step]
        eye_state = "closed_squint" if (step % 2 == 1) else "open"
        paw_action = "workout_paws"
    elif state == "flower":
        prop_type = "hologram_flower"
        paw_action = "hold_flower"
    elif state == "dance":
        step = frame_idx % 8
        bob_y = int(math.sin(step * math.pi / 2) * 5)
        tail_sway = math.cos(step * math.pi / 2) * 0.6
        ear_tilt = math.sin(step * math.pi / 2) * 0.3
        eye_state = "joy_arcs" if (step % 2 == 0) else "open"
        paw_action = "dance_step"
    elif state == "petcat":
        paw_action = "cat_knead"
        eye_state = "joy_arcs"
        mouth_state = "cat_smile"
    elif state == "petdog":
        tail_sway = math.sin(frame_idx * math.pi) * 0.6
        paw_action = "wave"
        eye_state = "sparkle"
        mouth_state = "wide_smile"
    elif state == "turnback":
        eye_state = "none"
    elif state == "shy":
        eye_state = "shy_peek"
        mouth_state = "smile"
        aura_type = "blush_full"
        paw_action = "shield_tail"
    elif state == "charging":
        aura_type = "charging_vortex"
        glow_intensity = 1.0 + (frame_idx % 4) * 0.3
    elif state == "busy":
        paw_action = "type_keybd"
        eye_state = "look_down"
        aura_type = "data_stream"
    elif state == "success":
        eye_state = "sparkle"
        mouth_state = "wide_smile"
        aura_type = "star_crest"
    elif state == "error":
        eye_state = "worried"
        mouth_state = "frown"
        aura_type = "glitch_aura"

    head_cx = 48
    head_cy = 44 + bob_y
    body_cx = 48
    body_cy = 68 + bob_y

    # --- 1. Fluffy Fox Tail (Drawn in background) ---
    tail_base_x = 54
    tail_base_y = body_cy + 4
    t_angle = 0.38 + tail_sway
    tail_tip_x = tail_base_x + math.cos(t_angle) * 26
    tail_tip_y = tail_base_y - math.sin(t_angle) * 22
    tc_x = (tail_base_x + tail_tip_x) / 2
    tc_y = (tail_base_y + tail_tip_y) / 2

    # Tail dark contour outline
    draw_ellipse_aa(c, tc_x, tc_y, 14, 21, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255, angle=t_angle)
    # Tail warm fur body
    draw_ellipse_aa(c, tc_x - 0.5, tc_y + 0.5, 12, 18, C_FUR_SHADOW[0], C_FUR_SHADOW[1], C_FUR_SHADOW[2], 255, angle=t_angle)
    draw_ellipse_aa(c, tc_x - 1, tc_y, 10, 16, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255, angle=t_angle)
    # Tail glowing cyber tip
    draw_circle_aa(c, tail_tip_x, tail_tip_y, 6.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
    draw_circle_aa(c, tail_tip_x, tail_tip_y, 5.0, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 240, glow_radius=4)
    draw_circle_aa(c, tail_tip_x - 1, tail_tip_y + 1, 2.5, 255, 255, 255, 240)

    # --- 2. Cyber Ears (Behind head) ---
    left_ear_x, left_ear_y = head_cx - 19, head_cy - 16
    right_ear_x, right_ear_y = head_cx + 19, head_cy - 16
    
    # Left Ear: Dark contour -> Sage fur -> Pastel pink inner -> Cyan jewel accent
    l_tilt = -0.32 + ear_tilt
    draw_ellipse_aa(c, left_ear_x - 2, left_ear_y - 4, 10.5, 19, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255, angle=l_tilt)
    draw_ellipse_aa(c, left_ear_x - 2, left_ear_y - 4, 8.5, 17, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255, angle=l_tilt)
    draw_ellipse_aa(c, left_ear_x - 1, left_ear_y - 3, 5.5, 12.5, C_EAR_INNER[0], C_EAR_INNER[1], C_EAR_INNER[2], 255, angle=l_tilt)
    draw_ellipse_aa(c, left_ear_x, left_ear_y - 2, 3.5, 9, C_EAR_SHADOW[0], C_EAR_SHADOW[1], C_EAR_SHADOW[2], 220, angle=l_tilt)
    # Cyber jewel accent on ear tip
    draw_circle_aa(c, left_ear_x - 7, left_ear_y - 14, 2.5, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 255)
    draw_circle_aa(c, left_ear_x - 7, left_ear_y - 14, 1.2, 255, 255, 255, 255)
    
    # Right Ear: Dark contour -> Sage fur -> Pastel pink inner -> Cyan jewel accent
    r_tilt = 0.32 + ear_tilt
    draw_ellipse_aa(c, right_ear_x + 2, right_ear_y - 4, 10.5, 19, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255, angle=r_tilt)
    draw_ellipse_aa(c, right_ear_x + 2, right_ear_y - 4, 8.5, 17, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255, angle=r_tilt)
    draw_ellipse_aa(c, right_ear_x + 1, right_ear_y - 3, 5.5, 12.5, C_EAR_INNER[0], C_EAR_INNER[1], C_EAR_INNER[2], 255, angle=r_tilt)
    draw_ellipse_aa(c, right_ear_x, right_ear_y - 2, 3.5, 9, C_EAR_SHADOW[0], C_EAR_SHADOW[1], C_EAR_SHADOW[2], 220, angle=r_tilt)
    # Cyber jewel accent on ear tip
    draw_circle_aa(c, right_ear_x + 7, right_ear_y - 14, 2.5, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 255)
    draw_circle_aa(c, right_ear_x + 7, right_ear_y - 14, 1.2, 255, 255, 255, 255)

    # --- 3. Body Chassis / Torso ---
    # Body dark contour base
    draw_ellipse_aa(c, body_cx, body_cy, 19.5, 15.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
    # Body warm fur & pastel sage shading
    draw_ellipse_aa(c, body_cx, body_cy, 17.5, 13.5, C_FUR_SHADOW[0], C_FUR_SHADOW[1], C_FUR_SHADOW[2], 255)
    draw_ellipse_aa(c, body_cx, body_cy - 1.5, 16, 12, C_FUR_SAGE[0], C_FUR_SAGE[1], C_FUR_SAGE[2], 255)
    draw_ellipse_aa(c, body_cx, body_cy - 3, 13, 9, C_CHEST_TUFT[0], C_CHEST_TUFT[1], C_CHEST_TUFT[2], 255)

    # --- 4. Cyber Collar & Energy Pendant ---
    collar_y = head_cy + 17
    # Dark collar strap
    draw_capsule_line(c, head_cx - 15, collar_y - 1, head_cx + 15, collar_y - 1, 4.5, C_COLLAR[0], C_COLLAR[1], C_COLLAR[2], 255)
    # Glowing power gem in center
    gem_col = C_CYBER_CYAN
    if state == "error": gem_col = (244, 63, 94)
    elif state == "success": gem_col = (34, 197, 94)
    draw_circle_aa(c, head_cx, collar_y + 1, 4.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
    draw_circle_aa(c, head_cx, collar_y + 1, 3.2, gem_col[0], gem_col[1], gem_col[2], 255, glow_radius=4)
    draw_circle_aa(c, head_cx - 1, collar_y, 1.4, 255, 255, 255, 250)

    # --- 5. Chibi Head (Cute contoured shape) ---
    # Dark contour outline
    draw_ellipse_aa(c, head_cx, head_cy, 26.5, 22.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
    # Lower shadow
    draw_ellipse_aa(c, head_cx, head_cy + 1.5, 24.5, 20.5, C_FUR_SHADOW[0], C_FUR_SHADOW[1], C_FUR_SHADOW[2], 255)
    # Warm pastel cream head
    draw_ellipse_aa(c, head_cx, head_cy - 0.5, 23.5, 19.5, C_FUR_SAGE[0], C_FUR_SAGE[1], C_FUR_SAGE[2], 255)
    draw_ellipse_aa(c, head_cx, head_cy - 2, 21.5, 17.5, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)

    # Forehead Cyber Diamond Glyph
    draw_circle_aa(c, head_cx, head_cy - 12, 3.0, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
    draw_circle_aa(c, head_cx, head_cy - 12, 2.0, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 240, glow_radius=3)
    draw_circle_aa(c, head_cx, head_cy - 12, 1.0, 255, 255, 255, 255)

    # --- 6. Facial Features & Expressive Anime Eyes ---
    if state != "turnback":
        # Soft rosy blushing cheeks
        blush_alpha = 150 if "blush" in aura_type or state == "shy" else 95
        draw_ellipse_aa(c, head_cx - 13.5, head_cy + 6.5, 5.0, 3.0, C_BLUSH[0], C_BLUSH[1], C_BLUSH[2], blush_alpha)
        draw_ellipse_aa(c, head_cx + 13.5, head_cy + 6.5, 5.0, 3.0, C_BLUSH[0], C_BLUSH[1], C_BLUSH[2], blush_alpha)

        # Tiny cute nose
        draw_circle_aa(c, head_cx, head_cy + 4.5, 1.4, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)

        # Mouth expressions
        mouth_y = head_cy + 8.5
        if mouth_state == "smile" or mouth_state == "cat_smile":
            # Sweet curved kitty mouth :3
            draw_capsule_line(c, head_cx - 3.5, mouth_y - 0.5, head_cx, mouth_y + 1.2, 1.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_capsule_line(c, head_cx, mouth_y + 1.2, head_cx + 3.5, mouth_y - 0.5, 1.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        elif mouth_state == "wide_smile" or mouth_state == "laugh_open":
            draw_capsule_line(c, head_cx - 4.0, mouth_y - 1.0, head_cx + 4.0, mouth_y - 1.0, 2.0, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_ellipse_aa(c, head_cx, mouth_y + 1.5, 3.5, 2.5, 244, 63, 94, 255)
        elif mouth_state == "small_o" or mouth_state == "yawn":
            draw_circle_aa(c, head_cx, mouth_y + 1.0, 2.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_circle_aa(c, head_cx, mouth_y + 1.0, 1.5, 244, 63, 94, 255)
        elif mouth_state == "frown" or mouth_state == "pout":
            draw_capsule_line(c, head_cx - 3.0, mouth_y + 1.5, head_cx, mouth_y, 1.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_capsule_line(c, head_cx, mouth_y, head_cx + 3.0, mouth_y + 1.5, 1.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        elif mouth_state == "sleepy":
            draw_capsule_line(c, head_cx - 2.5, mouth_y, head_cx + 2.5, mouth_y, 1.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 220)

        # Eye Positions
        eye_y = head_cy + 1.0
        lex, rex = head_cx - 9.5, head_cx + 9.5

        if eye_state == "open" or eye_state.startswith("look_"):
            p_dx, p_dy = 0.0, 0.0
            if eye_state == "look_left": p_dx, p_dy = -2.2, 0.0
            elif eye_state == "look_right": p_dx, p_dy = 2.2, 0.0
            elif eye_state == "look_up": p_dx, p_dy = 0.0, -2.2
            elif eye_state == "look_down": p_dx, p_dy = 0.0, 2.2
            elif eye_state == "look_up_left": p_dx, p_dy = -1.8, -1.8
            elif eye_state == "look_up_right": p_dx, p_dy = 1.8, -1.8
            elif eye_state == "look_down_left": p_dx, p_dy = -1.8, 1.8
            elif eye_state == "look_down_right": p_dx, p_dy = 1.8, 1.8

            for ex in (lex, rex):
                # 1. Dark Eye Base Socket
                draw_ellipse_aa(c, ex, eye_y, 5.0, 6.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                # 2. Iris Jewel Gradient (Deep navy top -> Vibrant teal-cyan bottom)
                draw_ellipse_aa(c, ex + p_dx, eye_y + 0.8 + p_dy, 4.0, 5.5, 15, 23, 42, 255)
                draw_ellipse_aa(c, ex + p_dx, eye_y + 2.2 + p_dy, 3.4, 4.0, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 255)
                draw_circle_aa(c, ex + p_dx, eye_y + 3.2 + p_dy, 2.2, C_CYBER_TEAL[0], C_CYBER_TEAL[1], C_CYBER_TEAL[2], 255)
                # 3. Sparkling Catchlights
                draw_circle_aa(c, ex + p_dx - 1.5, eye_y - 1.6 + p_dy, 1.8, 255, 255, 255, 255)
                draw_circle_aa(c, ex + p_dx + 1.5, eye_y + 2.2 + p_dy, 1.0, 255, 255, 255, 220)
                # 4. Upper Eyelash / Eyelid Arc (Giving warm anime expression)
                draw_capsule_line(c, ex - 5.5, eye_y - 2.8, ex, eye_y - 5.5, 2.4, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                draw_capsule_line(c, ex, eye_y - 5.5, ex + 5.5, eye_y - 2.8, 2.4, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                # Delicate corner flick
                flick_x = ex + 6.8 if ex > head_cx else ex - 6.8
                draw_capsule_line(c, ex + 5.0 if ex > head_cx else ex - 5.0, eye_y - 3.2, flick_x, eye_y - 4.5, 1.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)

        elif eye_state in ("closed_happy", "joy_arcs"):
            # Happy curved anime arcs ^ ^
            for ex in (lex, rex):
                draw_capsule_line(c, ex - 4.5, eye_y + 0.5, ex, eye_y - 3.0, 2.4, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                draw_capsule_line(c, ex, eye_y - 3.0, ex + 4.5, eye_y + 0.5, 2.4, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)

        elif eye_state in ("closed_squint", "blink"):
            for ex in (lex, rex):
                draw_capsule_line(c, ex - 4.5, eye_y - 0.5, ex + 4.5, eye_y - 0.5, 2.4, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)

        elif eye_state == "laugh_brackets":
            # > <
            draw_capsule_line(c, lex - 4.0, eye_y - 2.5, lex + 2.0, eye_y, 2.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_capsule_line(c, lex - 4.0, eye_y + 2.5, lex + 2.0, eye_y, 2.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_capsule_line(c, rex + 4.0, eye_y - 2.5, rex - 2.0, eye_y, 2.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_capsule_line(c, rex + 4.0, eye_y + 2.5, rex - 2.0, eye_y, 2.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)

        elif eye_state == "sparkle":
            for ex in (lex, rex):
                draw_ellipse_aa(c, ex, eye_y, 5.5, 7.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                draw_ellipse_aa(c, ex, eye_y + 1.0, 4.5, 6.0, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 255)
                # 4-point glowing star
                draw_capsule_line(c, ex - 3.5, eye_y + 1.0, ex + 3.5, eye_y + 1.0, 1.8, 255, 255, 255, 255)
                draw_capsule_line(c, ex, eye_y - 2.5, ex, eye_y + 4.5, 1.8, 255, 255, 255, 255)

        elif eye_state == "spiral":
            for ex in (lex, rex):
                draw_circle_aa(c, ex, eye_y, 5.0, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                draw_circle_aa(c, ex, eye_y, 3.8, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 255)
                draw_circle_aa(c, ex, eye_y, 2.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                draw_circle_aa(c, ex + 1, eye_y, 1.0, 255, 255, 255, 255)

        elif eye_state in ("huge_round", "surprised"):
            for ex in (lex, rex):
                draw_circle_aa(c, ex, eye_y, 6.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                draw_circle_aa(c, ex, eye_y, 4.8, C_CYBER_CYAN[0], C_CYBER_CYAN[1], C_CYBER_CYAN[2], 255)
                draw_circle_aa(c, ex - 1.5, eye_y - 1.5, 2.0, 255, 255, 255, 255)
                draw_circle_aa(c, ex + 1.5, eye_y + 1.8, 1.0, 255, 255, 255, 220)

        elif eye_state == "crying_streams":
            for ex in (lex, rex):
                draw_ellipse_aa(c, ex, eye_y, 4.5, 3.5, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
                # Glowing cyan tears
                draw_capsule_line(c, ex, eye_y + 2, ex, eye_y + 15, 2.5, 56, 189, 248, 220)
                draw_circle_aa(c, ex, eye_y + 15, 2.2, 224, 242, 254, 255)

    # --- 7. Cute Front Paws ---
    paw_y = body_cy + 5
    if paw_action == "rest":
        # Two cute paws resting neatly in front
        for px in (head_cx - 6, head_cx + 6):
            draw_circle_aa(c, px, paw_y, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_circle_aa(c, px, paw_y - 0.5, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
            # Subtle toe line
            draw_capsule_line(c, px, paw_y - 1, px, paw_y + 2, 1.2, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 200)
    elif paw_action == "wave":
        # Left paw rest, right paw waving high
        draw_circle_aa(c, head_cx - 6, paw_y, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        draw_circle_aa(c, head_cx - 6, paw_y - 0.5, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
        wy = head_cy + 8
        draw_circle_aa(c, head_cx + 17, wy, 5.0, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        draw_circle_aa(c, head_cx + 17, wy, 3.8, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
    elif paw_action == "paws_to_cheeks":
        draw_circle_aa(c, head_cx - 13, head_cy + 9, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        draw_circle_aa(c, head_cx - 13, head_cy + 9, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
        draw_circle_aa(c, head_cx + 13, head_cy + 9, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        draw_circle_aa(c, head_cx + 13, head_cy + 9, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
    elif paw_action == "paw_to_chin":
        draw_circle_aa(c, head_cx - 6, paw_y, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        draw_circle_aa(c, head_cx - 6, paw_y - 0.5, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
        draw_circle_aa(c, head_cx + 4, head_cy + 11, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
        draw_circle_aa(c, head_cx + 4, head_cy + 11, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
    elif paw_action == "paws_up":
        for px in (head_cx - 15, head_cx + 15):
            draw_circle_aa(c, px, head_cy + 4, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_circle_aa(c, px, head_cy + 4, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
    elif paw_action == "hold_flower":
        for px in (head_cx - 6, head_cx + 6):
            draw_circle_aa(c, px, paw_y, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_circle_aa(c, px, paw_y - 0.5, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)
        # Hologram lotus
        fx, fy = head_cx, paw_y - 8
        draw_circle_aa(c, fx, fy, 4.5, 244, 63, 94, 255, glow_radius=4)
        draw_capsule_line(c, fx - 5, fy - 1, fx + 5, fy - 1, 2.5, 255, 148, 194, 240)
        draw_capsule_line(c, fx, fy - 5, fx, fy + 1, 2.5, 255, 148, 194, 240)
        draw_circle_aa(c, fx, fy - 1, 1.8, 250, 204, 21, 255)
    else:
        for px in (head_cx - 6, head_cx + 6):
            draw_circle_aa(c, px, paw_y, 4.8, C_OUTLINE[0], C_OUTLINE[1], C_OUTLINE[2], 255)
            draw_circle_aa(c, px, paw_y - 0.5, 3.6, C_FUR_BASE[0], C_FUR_BASE[1], C_FUR_BASE[2], 255)

    # --- 8. State Props / Emotes ---
    if aura_type == "sleep_zzz":
        draw_capsule_line(c, head_cx + 20, head_cy - 20, head_cx + 27, head_cy - 20, 2.0, 56, 189, 248, 240)
        draw_capsule_line(c, head_cx + 27, head_cy - 20, head_cx + 20, head_cy - 14, 2.0, 56, 189, 248, 240)
        draw_capsule_line(c, head_cx + 20, head_cy - 14, head_cx + 27, head_cy - 14, 2.0, 56, 189, 248, 240)
    elif aura_type == "exclamation":
        draw_capsule_line(c, head_cx, head_cy - 34, head_cx, head_cy - 26, 3.0, 250, 204, 21, 255)
        draw_circle_aa(c, head_cx, head_cy - 22, 2.0, 250, 204, 21, 255)
    elif aura_type == "confetti":
        colors = [(244, 63, 94), (168, 85, 247), (250, 204, 21), (56, 189, 248)]
        for i in range(4):
            col = colors[(i + frame_idx) % 4]
            cx_p = head_cx - 24 + i * 16
            cy_p = head_cy - 22 + int(math.sin(i * 1.5 + frame_idx) * 8)
            draw_circle_aa(c, cx_p, cy_p, 2.2, col[0], col[1], col[2], 255)

    return c

def main():
    out_dir = Path('LitleHelperClient/Assets/Skins/CyberFamiliar')
    out_dir.mkdir(parents=True, exist_ok=True)
    
    catalog = {
        'idle': 4,
        'sleep': 2,
        'yawn': 2,
        'wake': 2,
        'drag': 2,
        'dizzy': 2,
        'lookleft': 2,
        'lookright': 2,
        'lookup': 2,
        'lookdown': 2,
        'lookupleft': 2,
        'lookupright': 2,
        'lookdownleft': 2,
        'lookdownright': 2,
        'twirl': 8,
        'offended': 2,
        'greeting': 2,
        'action': 2,
        'notice': 2,
        'joy': 2,
        'sad': 2,
        'laugh': 2,
        'surprise': 2,
        'think': 2,
        'celebrate': 2,
        'facepalm': 2,
        'cry': 2,
        'workout': 8,
        'flower': 2,
        'dance': 8,
        'petcat': 2,
        'petdog': 2,
        'turnback': 2,
        'shy': 2,
        'charging': 4,
        'busy': 2,
        'success': 2,
        'error': 2,
    }
    
    total = 0
    for state, count in catalog.items():
        for frame in range(1, count + 1):
            canvas = render_cyber_familiar(state, frame - 1)
            filename = f"{state}_{frame}.png"
            write_png(canvas, out_dir / filename)
            total += 1
            
    print(f"Generated {total} CyberFamiliar frames in {out_dir}")

if __name__ == '__main__':
    main()
