#!/usr/bin/env python3
"""
Generates 5 distinct high-quality concept sprites for PixelHelper,
strictly adhering to the proportions, geometry, and charm of the Classic robot.
Uses pure Python standard library (zlib + struct) - no external dependencies.
"""
import os
import struct
import zlib
from pathlib import Path

W, H = 48, 48

def unfilter_png(raw, w, h):
    stride = w * 4 + 1
    unfiltered = bytearray(h * w * 4)
    for y in range(h):
        filter_type = raw[y * stride]
        curr = raw[y * stride + 1 : (y + 1) * stride]
        prev = unfiltered[(y - 1) * w * 4 : y * w * 4] if y > 0 else bytes(w * 4)
        out_row = bytearray(w * 4)
        for x in range(w * 4):
            c = curr[x]
            a = out_row[x - 4] if x >= 4 else 0
            b = prev[x]
            d = prev[x - 4] if x >= 4 else 0
            if filter_type == 0: v = c
            elif filter_type == 1: v = (c + a) & 0xFF
            elif filter_type == 2: v = (c + b) & 0xFF
            elif filter_type == 3: v = (c + ((a + b) >> 1)) & 0xFF
            elif filter_type == 4:
                p = a + b - d
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - d)
                pr = a if pa <= pb and pa <= pc else (b if pb <= pc else d)
                v = (c + pr) & 0xFF
            out_row[x] = v
        unfiltered[y * w * 4 : (y + 1) * w * 4] = out_row
    return unfiltered

def read_png(path):
    data = open(path, 'rb').read()
    idat = b''.join(data[i+8:i+8+struct.unpack('>I', data[i:i+4])[0]] 
                    for i in range(8, len(data)-12) if data[i+4:i+8] == b'IDAT')
    raw = zlib.decompress(idat)
    return unfilter_png(raw, W, H)

def write_png(pixels, w, h, path):
    raw = bytearray()
    for y in range(h):
        raw.append(0)  # filter type 0
        raw.extend(pixels[y*w*4 : (y+1)*w*4])
    compressed = zlib.compress(bytes(raw), 9)
    
    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(png)

def upscale_nearest(pixels, w, h, scale):
    out_w = w * scale
    out_h = h * scale
    out = bytearray(out_w * out_h * 4)
    for y in range(out_h):
        src_y = y // scale
        for x in range(out_w):
            src_x = x // scale
            src_idx = (src_y * w + src_x) * 4
            dst_idx = (y * out_w + x) * 4
            out[dst_idx : dst_idx + 4] = pixels[src_idx : src_idx + 4]
    return out, out_w, out_h

# Load classic pixels
classic_raw = read_png('LitleHelperClient/Assets/Skins/Classic/idle_1.png')

# Classic Palette mapping
C_TRANSPARENT = (0, 0, 0, 0)
C_INK         = (23, 52, 81, 255)    # #173451
C_SCREEN      = (25, 62, 92, 255)    # #193E5C
C_WHITE_HI    = (228, 247, 255, 255) # #E4F7FF
C_BODY_LIGHT  = (102, 188, 231, 255) # #66BCE7
C_BODY_SHADOW = (57, 123, 170, 255)  # #397BAA
C_EYE_CYAN    = (124, 244, 237, 255) # #7CF4ED
C_SPARKLE     = (255, 255, 255, 255) # #FFFFFF

def make_concept_canvas(color_map):
    """Replaces classic colors with target concept colors"""
    canvas = bytearray(classic_raw)
    for i in range(0, len(canvas), 4):
        c = tuple(canvas[i:i+4])
        if c in color_map:
            new_c = color_map[c]
            canvas[i:i+4] = bytes(new_c)
    return canvas

def set_pixel(canvas, x, y, rgba):
    if 0 <= x < W and 0 <= y < H:
        idx = (y * W + x) * 4
        canvas[idx:idx+4] = bytes(rgba)

def get_pixel(canvas, x, y):
    if 0 <= x < W and 0 <= y < H:
        idx = (y * W + x) * 4
        return tuple(canvas[idx:idx+4])
    return (0, 0, 0, 0)

# =========================================================================
# CONCEPT 1: «CYBER GOLD» (Золотой VIP / Люкс)
# =========================================================================
def generate_concept_1():
    cmap = {
        C_INK:         (42, 30, 18, 255),    # #2A1E12 Deep bronze ink
        C_SCREEN:      (24, 22, 26, 255),    # #18161A Velvet obsidian screen
        C_WHITE_HI:    (255, 246, 214, 255), # #FFF6D6 Pale champagne highlight
        C_BODY_LIGHT:  (240, 192, 72, 255),  # #F0C048 Radiant warm gold
        C_BODY_SHADOW: (164, 112, 24, 255),  # #A47018 Rich antique gold
        C_EYE_CYAN:    (255, 222, 89, 255),  # #FFDE59 Glowing amber eyes
        C_SPARKLE:     (255, 255, 255, 255), # #FFFFFF Diamond glint
    }
    c = make_concept_canvas(cmap)
    
    # Add a luxury mini bow tie on the collar (Y=30..32, X=22..26)
    c_tie_red = (220, 38, 38, 255)
    c_tie_dark = (153, 27, 27, 255)
    c_tie_center = (254, 202, 202, 255)
    
    # Knot in center
    set_pixel(c, 24, 30, c_tie_center)
    set_pixel(c, 24, 31, c_tie_dark)
    # Left wing
    set_pixel(c, 22, 30, c_tie_red)
    set_pixel(c, 23, 30, c_tie_red)
    set_pixel(c, 22, 31, c_tie_dark)
    set_pixel(c, 23, 31, c_tie_red)
    # Right wing
    set_pixel(c, 25, 30, c_tie_red)
    set_pixel(c, 26, 30, c_tie_red)
    set_pixel(c, 25, 31, c_tie_red)
    set_pixel(c, 26, 31, c_tie_dark)
    
    return c

# =========================================================================
# CONCEPT 2: «RETRO TERMINAL» (Винтажный Макинтош 80-х)
# =========================================================================
def generate_concept_2():
    cmap = {
        C_INK:         (56, 51, 43, 255),    # #38332B Vintage dark casing edge
        C_SCREEN:      (14, 35, 20, 255),    # #0E2314 Dark CRT glass
        C_WHITE_HI:    (247, 239, 229, 255), # #F7EFE5 Warm retro beige highlight
        C_BODY_LIGHT:  (222, 203, 183, 255), # #DECBB7 Classic beige PC plastic
        C_BODY_SHADOW: (158, 140, 120, 255), # #9E8C78 Shaded plastic
        C_EYE_CYAN:    (57, 255, 20, 255),   # #39FF14 Glowing phosphor green
        C_SPARKLE:     (187, 247, 208, 255), # #BBF7D0 Phosphor highlight
    }
    c = make_concept_canvas(cmap)
    
    # 1. Floppy drive slot under the screen (Y=24..25, X=28..33)
    ink = (56, 51, 43, 255)
    dark_slot = (30, 27, 23, 255)
    led_orange = (249, 115, 22, 255)
    for x in range(29, 34):
        set_pixel(c, x, 25, dark_slot)
    set_pixel(c, 28, 25, led_orange) # Floppy read LED
    
    # 2. Cute retro antenna on top right of helmet (X=33..35, Y=1..5)
    set_pixel(c, 34, 4, ink)
    set_pixel(c, 34, 3, ink)
    set_pixel(c, 35, 2, ink)
    # Red bulb on tip
    c_red_bulb = (239, 68, 68, 255)
    set_pixel(c, 35, 1, c_red_bulb)
    set_pixel(c, 36, 1, (254, 202, 202, 255))
    set_pixel(c, 35, 0, ink)
    set_pixel(c, 36, 0, ink)
    
    return c

# =========================================================================
# CONCEPT 3: «COSMO KITTY» (Космический Нэкобот)
# =========================================================================
def generate_concept_3():
    cmap = {
        C_INK:         (34, 28, 53, 255),    # #221C35 Deep midnight plum
        C_SCREEN:      (26, 22, 43, 255),    # #1A162B Cosmic night violet
        C_WHITE_HI:    (255, 255, 255, 255), # #FFFFFF Pure crisp white
        C_BODY_LIGHT:  (229, 219, 245, 255), # #E5DBF5 Pastel lilac
        C_BODY_SHADOW: (167, 149, 198, 255), # #A795C6 Shaded lilac
        C_EYE_CYAN:    (255, 102, 178, 255), # #FF66B2 Candy neon pink eyes
        C_SPARKLE:     (255, 214, 238, 255), # #FFD6EE Pink eye sparkle
    }
    c = make_concept_canvas(cmap)
    
    ink = (34, 28, 53, 255)
    white = (255, 255, 255, 255)
    pink_inner = (255, 148, 194, 255)
    pink_glow = (255, 182, 217, 255)
    
    # Left cat ear (X=14..18, Y=2..5)
    # Top tip
    set_pixel(c, 15, 2, ink)
    set_pixel(c, 14, 3, ink)
    set_pixel(c, 15, 3, white)
    set_pixel(c, 16, 3, ink)
    set_pixel(c, 13, 4, ink)
    set_pixel(c, 14, 4, white)
    set_pixel(c, 15, 4, pink_inner)
    set_pixel(c, 16, 4, pink_glow)
    set_pixel(c, 17, 4, ink)
    set_pixel(c, 13, 5, ink)
    set_pixel(c, 14, 5, white)
    set_pixel(c, 15, 5, pink_inner)
    set_pixel(c, 16, 5, pink_inner)
    set_pixel(c, 17, 5, ink)

    # Right cat ear (X=29..33, Y=2..5)
    set_pixel(c, 32, 2, ink)
    set_pixel(c, 31, 3, ink)
    set_pixel(c, 32, 3, white)
    set_pixel(c, 33, 3, ink)
    set_pixel(c, 30, 4, ink)
    set_pixel(c, 31, 4, pink_glow)
    set_pixel(c, 32, 4, pink_inner)
    set_pixel(c, 33, 4, white)
    set_pixel(c, 34, 4, ink)
    set_pixel(c, 30, 5, ink)
    set_pixel(c, 31, 5, pink_inner)
    set_pixel(c, 32, 5, pink_inner)
    set_pixel(c, 33, 5, white)
    set_pixel(c, 34, 5, ink)
    
    # Cute little blush cheeks on the screen! (Y=18, X=16 and X=31)
    blush = (255, 107, 158, 200)
    set_pixel(c, 16, 18, blush)
    set_pixel(c, 17, 18, blush)
    set_pixel(c, 30, 18, blush)
    set_pixel(c, 31, 18, blush)
    
    return c

# =========================================================================
# CONCEPT 4: «STEALTH NEON / DARK MODE» (Тёмный Неоновый Ниндзя)
# =========================================================================
def generate_concept_4():
    cmap = {
        C_INK:         (15, 17, 26, 255),    # #0F111A Obsidian pitch outline
        C_SCREEN:      (11, 12, 16, 255),    # #0B0C10 True OLED black
        C_WHITE_HI:    (82, 88, 117, 255),   # #525875 Matte gunmetal highlight
        C_BODY_LIGHT:  (46, 50, 69, 255),    # #2E3245 Matte stealth titanium
        C_BODY_SHADOW: (27, 29, 43, 255),    # #1B1D2B Deep graphite
        C_EYE_CYAN:    (0, 240, 255, 255),   # #00F0FF Electric cyber cyan
        C_SPARKLE:     (200, 250, 255, 255), # #C8FAFF Cyber spark
    }
    c = make_concept_canvas(cmap)
    
    # Neon accent stripes on the headphones & chest
    neon_cyan = (0, 240, 255, 255)
    neon_glow = (14, 165, 233, 255)
    
    # Left headphone neon ring
    set_pixel(c, 9, 13, neon_cyan)
    set_pixel(c, 9, 14, neon_cyan)
    set_pixel(c, 9, 15, neon_glow)
    
    # Right headphone neon ring
    set_pixel(c, 38, 13, neon_cyan)
    set_pixel(c, 38, 14, neon_cyan)
    set_pixel(c, 38, 15, neon_glow)
    
    # Chest power core line (Y=31, X=22..25)
    for x in range(22, 26):
        set_pixel(c, x, 31, neon_cyan)
    
    # Anti-gravity hover beam at bottom
    set_pixel(c, 23, 44, neon_cyan)
    set_pixel(c, 24, 44, neon_cyan)
    set_pixel(c, 23, 45, neon_glow)
    set_pixel(c, 24, 45, neon_glow)

    return c

# =========================================================================
# CONCEPT 5: «ECO SPROUT» (Эко-робот с живым ростком)
# =========================================================================
def generate_concept_5():
    cmap = {
        C_INK:         (19, 38, 25, 255),    # #132619 Deep pine forest ink
        C_SCREEN:      (20, 41, 27, 255),    # #14291B Botanical greenhouse dark
        C_WHITE_HI:    (240, 253, 244, 255), # #F0FDF4 Mint porcelain highlight
        C_BODY_LIGHT:  (134, 239, 172, 255), # #86EFAC Fresh spring sage
        C_BODY_SHADOW: (34, 197, 94, 255),   # #22C55E Foliage green
        C_EYE_CYAN:    (254, 240, 138, 255), # #FEF08A Warm solar sunlight eyes
        C_SPARKLE:     (255, 255, 255, 255), # #FFFFFF Sunlight gleam
    }
    c = make_concept_canvas(cmap)
    
    ink = (19, 38, 25, 255)
    sprout_stem = (34, 197, 94, 255)
    leaf_light = (110, 231, 183, 255)
    leaf_green = (16, 185, 129, 255)
    leaf_deep  = (5, 150, 105, 255)
    
    # Sprout growing out of head center (X=23..24, Y=1..5)
    # Stem
    set_pixel(c, 24, 4, sprout_stem)
    set_pixel(c, 24, 3, sprout_stem)
    set_pixel(c, 24, 2, sprout_stem)
    
    # Left leaf
    set_pixel(c, 22, 1, ink)
    set_pixel(c, 23, 1, leaf_light)
    set_pixel(c, 21, 2, ink)
    set_pixel(c, 22, 2, leaf_green)
    set_pixel(c, 23, 2, leaf_deep)
    set_pixel(c, 22, 3, ink)
    
    # Right leaf
    set_pixel(c, 25, 1, leaf_light)
    set_pixel(c, 26, 1, ink)
    set_pixel(c, 25, 2, leaf_deep)
    set_pixel(c, 26, 2, leaf_green)
    set_pixel(c, 27, 2, ink)
    set_pixel(c, 26, 3, ink)
    
    # Stem outline
    set_pixel(c, 23, 4, ink)
    set_pixel(c, 25, 4, ink)
    set_pixel(c, 23, 3, ink)
    set_pixel(c, 25, 3, ink)

    return c

def create_showcase(concepts, scale=6, pad=24):
    """Combines all concepts into a neat side-by-side presentation board"""
    n = len(concepts)
    sprite_w = W * scale
    sprite_h = H * scale
    
    card_w = sprite_w + pad * 2
    card_h = sprite_h + pad * 2 + 60
    
    total_w = card_w * n + pad * (n + 1)
    total_h = card_h + pad * 2
    
    # Background color: stylish dark studio slate (#1e222d)
    bg_r, bg_g, bg_b = 30, 34, 45
    card_r, card_g, card_b = 40, 45, 60
    board = bytearray([bg_r, bg_g, bg_b, 255] * (total_w * total_h))
    
    for idx, (title, pixels) in enumerate(concepts):
        up_pixels, up_w, up_h = upscale_nearest(pixels, W, H, scale)
        start_x = pad + idx * (card_w + pad)
        start_y = pad
        
        # Draw card container
        for cy in range(card_h):
            for cx in range(card_w):
                bx = start_x + cx
                by = start_y + cy
                b_idx = (by * total_w + bx) * 4
                board[b_idx:b_idx+4] = bytes([card_r, card_g, card_b, 255])
                
        # Draw sprite inside card
        sprite_start_x = start_x + pad
        sprite_start_y = start_y + pad
        for sy in range(up_h):
            for sx in range(up_w):
                sp_idx = (sy * up_w + sx) * 4
                a = up_pixels[sp_idx + 3]
                if a > 0:
                    bx = sprite_start_x + sx
                    by = sprite_start_y + sy
                    b_idx = (by * total_w + bx) * 4
                    # Alpha blend onto card
                    alpha = a / 255.0
                    for c_i in range(3):
                        board[b_idx + c_i] = int(up_pixels[sp_idx + c_i] * alpha + card_r * (1.0 - alpha))
    
    return board, total_w, total_h

if __name__ == '__main__':
    out_dir = Path('LitleHelperClient/Assets/Concepts')
    out_dir.mkdir(parents=True, exist_ok=True)
    
    c1 = generate_concept_1()
    c2 = generate_concept_2()
    c3 = generate_concept_3()
    c4 = generate_concept_4()
    c5 = generate_concept_5()
    
    concepts = [
        ("Classic (Оригинал)", classic_raw),
        ("1. Cyber Gold", c1),
        ("2. Retro Terminal", c2),
        ("3. Cosmo Kitty", c3),
        ("4. Stealth Neon", c4),
        ("5. Eco Sprout", c5),
    ]
    
    # Save individual 48x48 and upscaled 6x (288x288)
    for name, p in concepts:
        slug = name.split('.')[0].replace(' ', '_').lower()
        if "classic" in name.lower(): slug = "0_classic"
        elif "cyber" in name.lower(): slug = "1_cyber_gold"
        elif "retro" in name.lower(): slug = "2_retro_terminal"
        elif "kitty" in name.lower(): slug = "3_cosmo_kitty"
        elif "stealth" in name.lower(): slug = "4_stealth_neon"
        elif "eco" in name.lower(): slug = "5_eco_sprout"
        
        write_png(p, W, H, out_dir / f"{slug}.png")
        up, up_w, up_h = upscale_nearest(p, W, H, 6)
        write_png(up, up_w, up_h, out_dir / f"{slug}_x6.png")
        print(f"Generated {slug}")
        
    board, bw, bh = create_showcase(concepts, scale=6)
    write_png(board, bw, bh, out_dir / "concepts_showcase.png")
    print(f"Showcase generated: {bw}x{bh}")
