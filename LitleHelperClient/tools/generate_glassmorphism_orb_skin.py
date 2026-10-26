#!/usr/bin/env python3
"""
Generates all 34 animation states (90 sprite frames) for the GlassmorphismOrb skin.
High-definition smooth 3D-styled glassmorphism (96x96 px) with frosted glass refractions,
dynamic orbital rings, and glowing neon AI core.
Pure Python standard library (math + zlib + struct).
"""
import math
import os
import struct
import zlib
from pathlib import Path

W, H = 96, 96

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
            if dist_sq <= 0.85:
                blend_pixel(canvas, x, y, r, g, b, a)
            elif dist_sq < 1.15:
                factor = max(0.0, min(1.0, (1.15 - dist_sq) / 0.3))
                blend_pixel(canvas, x, y, r, g, b, int(a * factor))

def draw_ellipse_ring_aa(canvas, cx, cy, rx, ry, thickness, r, g, b, a=255, angle=0):
    cos_a = math.cos(-angle)
    sin_a = math.sin(-angle)
    max_r = max(rx, ry) + thickness + 2
    x0 = max(0, int(math.floor(cx - max_r)))
    x1 = min(W, int(math.ceil(cx + max_r)))
    y0 = max(0, int(math.floor(cy - max_r)))
    y1 = min(H, int(math.ceil(cy + max_r)))
    half_th = thickness / 2.0
    for y in range(y0, y1):
        for x in range(x0, x1):
            dx = x - cx
            dy = y - cy
            nx = (dx * cos_a - dy * sin_a)
            ny = (dx * sin_a + dy * cos_a)
            # Distance from ellipse perimeter
            # Normalized coordinate
            norm_d = math.hypot(nx / max(rx, 0.1), ny / max(ry, 0.1))
            approx_dist = abs(norm_d - 1.0) * min(rx, ry)
            if approx_dist <= half_th - 0.5:
                blend_pixel(canvas, x, y, r, g, b, a)
            elif approx_dist < half_th + 0.5:
                cov = max(0.0, min(1.0, half_th + 0.5 - approx_dist))
                blend_pixel(canvas, x, y, r, g, b, int(a * cov))

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
# CHARACTER RENDER ENGINE: GlassmorphismOrb
# =========================================================================
def render_glassmorphism_orb(state, frame_idx):
    c = bytearray(W * H * 4)

    # Core physics & parameters
    bob_y = 0
    core_offset_x = 0
    core_offset_y = 0
    ring_rot_1 = 0.4
    ring_rot_2 = -0.4
    ring_scale = 1.0
    core_color = (0, 240, 255) # Electric cyan default
    face_type = "happy_smile"
    aura_type = "normal"
    prop_type = None

    if state == "idle":
        bobs = [0, -2, 0, 2]
        bob_y = bobs[frame_idx % 4]
        ring_rot_1 = 0.35 + (frame_idx / 4.0) * 0.25
        ring_rot_2 = -0.35 - (frame_idx / 4.0) * 0.25
        if frame_idx == 2: face_type = "blink"
    elif state == "sleep":
        bob_y = [1, -1][frame_idx % 2]
        ring_rot_1 = 0.1
        ring_rot_2 = -0.1
        core_color = (56, 189, 248) # Muted soft blue
        face_type = "sleep_arcs"
        aura_type = "sleep_zzz"
    elif state == "yawn":
        ring_scale = 1.25 if frame_idx == 1 else 1.1
        face_type = "yawn_o"
    elif state == "wake":
        bob_y = [2, -2][frame_idx % 2]
        face_type = "bright_sparkle"
        core_color = (255, 255, 255)
    elif state == "drag":
        core_offset_x = -3
        core_offset_y = 3
        ring_rot_1 = 0.8
        face_type = "surprised_o"
    elif state == "dizzy":
        spin_step = (frame_idx * math.pi)
        ring_rot_1 = spin_step
        ring_rot_2 = -spin_step * 1.5
        face_type = "spiral"
    elif state == "lookleft":
        core_offset_x = -4
        face_type = "look_left"
    elif state == "lookright":
        core_offset_x = 4
        face_type = "look_right"
    elif state == "lookup":
        core_offset_y = -4
        face_type = "look_up"
    elif state == "lookdown":
        core_offset_y = 4
        face_type = "look_down"
    elif state == "lookupleft":
        core_offset_x = -3
        core_offset_y = -3
        face_type = "look_up_left"
    elif state == "lookupright":
        core_offset_x = 3
        core_offset_y = -3
        face_type = "look_up_right"
    elif state == "lookdownleft":
        core_offset_x = -3
        core_offset_y = 3
        face_type = "look_down_left"
    elif state == "lookdownright":
        core_offset_x = 3
        core_offset_y = 3
        face_type = "look_down_right"
    elif state == "twirl":
        step = (frame_idx / 8.0) * math.tau
        ring_rot_1 = step
        ring_rot_2 = -step
        bob_y = int(math.sin(step) * 3)
        aura_type = "spin_particles"
    elif state == "offended":
        ring_rot_1 = 0.0
        ring_rot_2 = 0.0
        core_color = (245, 158, 11) # Amber pouting
        face_type = "pout"
    elif state == "greeting":
        face_type = "happy_smile"
        aura_type = "wave_hand"
    elif state == "action":
        core_color = (168, 85, 247) # Purple neon pulse
        aura_type = "pulse_beam"
    elif state == "notice":
        core_color = (250, 204, 21) # Golden amber
        face_type = "bright_sparkle"
        aura_type = "exclamation"
    elif state == "joy":
        bob_y = [-5, 1][frame_idx % 2]
        core_color = (0, 255, 200)
        face_type = "laugh_face"
        ring_scale = 1.15
        aura_type = "joy_burst"
    elif state == "sad":
        bob_y = 3
        core_color = (96, 165, 250)
        face_type = "sad_face"
        ring_rot_1 = 0.15
        ring_rot_2 = -0.15
    elif state == "laugh":
        bob_y = [-3, 1][frame_idx % 2]
        face_type = "laugh_face"
        aura_type = "laugh_vibe"
    elif state == "surprise":
        bob_y = -4
        ring_scale = 1.3
        face_type = "surprised_o"
    elif state == "think":
        ring_rot_1 = (frame_idx * math.pi / 2)
        face_type = "think_look"
        aura_type = "binary_stream"
    elif state == "celebrate":
        bob_y = [-5, 1][frame_idx % 2]
        core_color = [(244, 63, 94), (168, 85, 247)][frame_idx % 2]
        face_type = "laugh_face"
        aura_type = "confetti"
    elif state == "facepalm":
        ring_rot_1 = math.pi / 4
        ring_rot_2 = -math.pi / 4
        face_type = "facepalm_closed"
    elif state == "cry":
        core_color = (56, 189, 248)
        face_type = "crying_streams"
    elif state == "workout":
        step = frame_idx % 8
        bob_y = [-3, 0, -5, 2, 4, 1, -2, -4][step]
        ring_rot_1 = (step / 8.0) * math.tau
        ring_rot_2 = -(step / 8.0) * math.tau
    elif state == "flower":
        prop_type = "neon_flower"
        face_type = "happy_smile"
    elif state == "dance":
        step = frame_idx % 8
        bob_y = int(math.sin(step * math.pi / 2) * 5)
        ring_rot_1 = step * 0.4
        ring_rot_2 = -step * 0.4
        face_type = "laugh_face"
        core_color = [(0, 240, 255), (236, 72, 153), (168, 85, 247), (34, 197, 94)][step % 4]
    elif state == "petcat":
        prop_type = "kitty_hologram"
        face_type = "cat_face"
    elif state == "petdog":
        prop_type = "puppy_hologram"
        face_type = "happy_smile"
    elif state == "turnback":
        face_type = "back_plate"
    elif state == "shy":
        aura_type = "frosted_blush"
        face_type = "shy_smile"
    elif state == "charging":
        step = frame_idx % 4
        ring_scale = 0.9 + step * 0.15
        aura_type = "vortex_charging"
    elif state == "busy":
        ring_rot_1 = frame_idx * 1.2
        aura_type = "radar_ring"
    elif state == "success":
        core_color = (34, 197, 94) # Emerald success
        face_type = "bright_sparkle"
        aura_type = "checkmark"
    elif state == "error":
        core_color = (239, 68, 68) # Red hazard
        face_type = "error_frown"
        aura_type = "hazard_beacon"

    cx = 48
    cy = 46 + bob_y
    orb_radius = 24

    # --- 1. Ambient Hover Light Ring at bottom ---
    base_y = cy + orb_radius + 10
    draw_ellipse_ring_aa(c, cx, base_y, 18, 5, 2.5, core_color[0], core_color[1], core_color[2], 180)
    draw_circle_aa(c, cx, base_y - 2, 8, core_color[0], core_color[1], core_color[2], 60, glow_radius=8)

    # --- 2. Rear Orbital Ring ---
    r1_rx = 34 * ring_scale
    r1_ry = 11 * ring_scale
    draw_ellipse_ring_aa(c, cx, cy, r1_rx, r1_ry, 2.5, 180, 220, 255, 160, angle=ring_rot_1)

    # --- 3. Frosted Glass Sphere Body (Refraction & Glass Depth) ---
    # Soft ambient back glow
    draw_circle_aa(c, cx, cy, orb_radius + 4, core_color[0], core_color[1], core_color[2], 60, glow_radius=10)
    # Glass volume base - solid hit-testing alpha across the whole sphere
    draw_circle_aa(c, cx, cy, orb_radius, 224, 242, 254, 140)
    # Glass frosted gradient
    draw_circle_aa(c, cx - 2, cy - 2, orb_radius - 2, 240, 249, 255, 120)

    # --- 4. Holographic Digital AI Core Inside ---
    ccx = cx + core_offset_x
    ccy = cy + core_offset_y
    core_rad = 13
    
    # Core radiant halo
    draw_circle_aa(c, ccx, ccy, core_rad, core_color[0], core_color[1], core_color[2], 180, glow_radius=7)
    draw_circle_aa(c, ccx, ccy, core_rad - 2, 15, 23, 42, 230) # Dark screen visor inside core
    
    # --- 5. Holographic Emoji Expressions on Core ---
    if face_type != "back_plate":
        fx, fy = ccx, ccy
        eye_col = core_color
        sparkle_col = (255, 255, 255)

        if face_type == "happy_smile" or face_type.startswith("look_"):
            p_dx, p_dy = 0, 0
            if face_type == "look_left": p_dx = -2
            elif face_type == "look_right": p_dx = 2
            elif face_type == "look_up": p_dy = -2
            elif face_type == "look_down": p_dy = 2
            elif face_type == "look_up_left": p_dx, p_dy = -2, -2
            elif face_type == "look_up_right": p_dx, p_dy = 2, -2
            elif face_type == "look_down_left": p_dx, p_dy = -2, 2
            elif face_type == "look_down_right": p_dx, p_dy = 2, 2

            # Happy open glowing eyes: `^ ^`
            draw_capsule_line(c, fx - 7 + p_dx, fy - 1 + p_dy, fx - 4 + p_dx, fy - 4 + p_dy, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx - 4 + p_dx, fy - 4 + p_dy, fx - 1 + p_dx, fy - 1 + p_dy, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx + 1 + p_dx, fy - 1 + p_dy, fx + 4 + p_dx, fy - 4 + p_dy, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx + 4 + p_dx, fy - 4 + p_dy, fx + 7 + p_dx, fy - 1 + p_dy, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            # Glowing smile
            draw_capsule_line(c, fx - 4 + p_dx, fy + 3 + p_dy, fx + 4 + p_dx, fy + 3 + p_dy, 2, eye_col[0], eye_col[1], eye_col[2], 255)

        elif face_type == "laugh_face":
            # Smiling wide open mouth `D`
            draw_capsule_line(c, fx - 6, fy - 2, fx - 2, fy - 4, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx + 2, fy - 4, fx + 6, fy - 2, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_circle_aa(c, fx, fy + 3, 3.5, eye_col[0], eye_col[1], eye_col[2], 255)

        elif face_type == "bright_sparkle":
            # Star eyes
            for side in (-4.5, 4.5):
                draw_capsule_line(c, fx + side - 2.5, fy - 2, fx + side + 2.5, fy - 2, 1.5, sparkle_col[0], sparkle_col[1], sparkle_col[2], 255)
                draw_capsule_line(c, fx + side, fy - 4.5, fx + side, fy + 0.5, 1.5, sparkle_col[0], sparkle_col[1], sparkle_col[2], 255)
            draw_capsule_line(c, fx - 4, fy + 3, fx + 4, fy + 3, 2, eye_col[0], eye_col[1], eye_col[2], 255)

        elif face_type == "sleep_arcs" or face_type == "blink":
            draw_capsule_line(c, fx - 6, fy - 2, fx - 1, fy - 2, 2, eye_col[0], eye_col[1], eye_col[2], 220)
            draw_capsule_line(c, fx + 1, fy - 2, fx + 6, fy - 2, 2, eye_col[0], eye_col[1], eye_col[2], 220)
            draw_capsule_line(c, fx - 3, fy + 3, fx + 3, fy + 3, 1.5, eye_col[0], eye_col[1], eye_col[2], 200)

        elif face_type == "surprised_o" or face_type == "yawn_o":
            draw_circle_aa(c, fx - 4.5, fy - 2, 2.5, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_circle_aa(c, fx + 4.5, fy - 2, 2.5, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_circle_aa(c, fx, fy + 3.5, 3, eye_col[0], eye_col[1], eye_col[2], 255)

        elif face_type == "spiral":
            for side in (-4.5, 4.5):
                draw_circle_aa(c, fx + side, fy - 2, 3, eye_col[0], eye_col[1], eye_col[2], 255)
                draw_circle_aa(c, fx + side, fy - 2, 1.5, 15, 23, 42, 255)

        elif face_type == "sad_face" or face_type == "error_frown":
            draw_capsule_line(c, fx - 5, fy - 3, fx - 1, fy - 1, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx + 1, fy - 1, fx + 5, fy - 3, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx - 4, fy + 4, fx + 4, fy + 4, 2, eye_col[0], eye_col[1], eye_col[2], 255)

        elif face_type == "crying_streams":
            draw_capsule_line(c, fx - 5, fy - 2, fx - 1, fy - 2, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx + 1, fy - 2, fx + 5, fy - 2, 2, eye_col[0], eye_col[1], eye_col[2], 255)
            draw_capsule_line(c, fx - 3, fy, fx - 3, fy + 8, 2, 56, 189, 248, 240)
            draw_capsule_line(c, fx + 3, fy, fx + 3, fy + 8, 2, 56, 189, 248, 240)

    else:
        # Back circuit plate with glowing AI core emblem
        draw_circle_aa(c, ccx, ccy, 6, 203, 213, 225, 255)
        draw_circle_aa(c, ccx, ccy, 3, 0, 240, 255, 255)

    # --- 6. Front Glass Specular Sheen & Reflections ---
    # Curved top-left specular highlight
    draw_ellipse_aa(c, cx - 8, cy - 8, 12, 6, 255, 255, 255, 140, angle=-0.5)
    draw_ellipse_aa(c, cx - 10, cy - 9, 7, 3, 255, 255, 255, 210, angle=-0.5)
    # Bottom glass rim reflection
    draw_ellipse_aa(c, cx + 6, cy + 12, 10, 4, 186, 230, 253, 110, angle=-0.3)

    # Frosted blush if shy
    if aura_type == "frosted_blush":
        draw_ellipse_aa(c, cx - 12, cy + 4, 6, 4, 244, 63, 94, 150)
        draw_ellipse_aa(c, cx + 12, cy + 4, 6, 4, 244, 63, 94, 150)

    # --- 7. Front Orbital Ring (Crossing Foreground) ---
    r2_rx = 34 * ring_scale
    r2_ry = 11 * ring_scale
    draw_ellipse_ring_aa(c, cx, cy, r2_rx, r2_ry, 2.5, 224, 242, 254, 220, angle=ring_rot_2)
    # Satellite node orbiting on ring
    node_angle = ring_rot_2 + (frame_idx * math.pi / 2)
    node_x = cx + math.cos(node_angle) * (r2_rx - 1) * math.cos(ring_rot_2) - math.sin(node_angle) * r2_ry * math.sin(ring_rot_2)
    node_y = cy + math.cos(node_angle) * (r2_rx - 1) * math.sin(ring_rot_2) + math.sin(node_angle) * r2_ry * math.cos(ring_rot_2)
    draw_circle_aa(c, node_x, node_y, 3, core_color[0], core_color[1], core_color[2], 255, glow_radius=4)
    draw_circle_aa(c, node_x, node_y, 1.5, 255, 255, 255, 255)

    # --- 8. State Props / Holograms ---
    if aura_type == "sleep_zzz":
        draw_capsule_line(c, cx + 22, cy - 18, cx + 30, cy - 18, 2, 56, 189, 248, 240)
        draw_capsule_line(c, cx + 30, cy - 18, cx + 22, cy - 12, 2, 56, 189, 248, 240)
        draw_capsule_line(c, cx + 22, cy - 12, cx + 30, cy - 12, 2, 56, 189, 248, 240)
    elif aura_type == "exclamation":
        draw_capsule_line(c, cx, cy - 36, cx, cy - 28, 3, 250, 204, 21, 255)
        draw_circle_aa(c, cx, cy - 24, 2, 250, 204, 21, 255)
    elif aura_type == "wave_hand":
        draw_circle_aa(c, cx + 28, cy - 10, 4, 0, 240, 255, 220, glow_radius=4)
        draw_capsule_line(c, cx + 28, cy - 10, cx + 34, cy - 16, 2, 0, 240, 255, 240)
        draw_capsule_line(c, cx + 29, cy - 10, cx + 35, cy - 13, 2, 0, 240, 255, 240)
    elif prop_type == "neon_flower":
        draw_circle_aa(c, cx, cy, 5, 244, 63, 94, 240, glow_radius=4)
        draw_circle_aa(c, cx, cy, 2.5, 250, 204, 21, 255)
    elif aura_type == "checkmark":
        draw_capsule_line(c, cx - 6, cy, cx - 2, cy + 4, 2.5, 34, 197, 94, 255)
        draw_capsule_line(c, cx - 2, cy + 4, cx + 6, cy - 4, 2.5, 34, 197, 94, 255)
    elif aura_type == "hazard_beacon":
        draw_capsule_line(c, cx, cy - 36, cx - 6, cy - 24, 2.5, 239, 68, 68, 255)
        draw_capsule_line(c, cx, cy - 36, cx + 6, cy - 24, 2.5, 239, 68, 68, 255)
        draw_capsule_line(c, cx - 6, cy - 24, cx + 6, cy - 24, 2.5, 239, 68, 68, 255)

    return c

def main():
    out_dir = Path('LitleHelperClient/Assets/Skins/GlassmorphismOrb')
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
            canvas = render_glassmorphism_orb(state, frame - 1)
            filename = f"{state}_{frame}.png"
            write_png(canvas, out_dir / filename)
            total += 1
            
    print(f"Generated {total} GlassmorphismOrb frames in {out_dir}")

if __name__ == '__main__':
    main()
