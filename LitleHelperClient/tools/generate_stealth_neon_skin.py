#!/usr/bin/env python3
"""
Generates all 34 animation states (90 sprite frames) for the StealthNeon skin.
Transforms the Classic 16-bit pixel art into the dark titanium / cyber neon theme.
Uses pure Python standard library (zlib + struct).
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
        raw.append(0)
        raw.extend(pixels[y*w*4 : (y+1)*w*4])
    compressed = zlib.compress(bytes(raw), 9)
    
    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', compressed) + chunk(b'IEND', b'')
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(png)

# Stealth Neon Color Mapping
CMAP = {
    (23, 52, 81, 255):   (15, 17, 26, 255),    # #0F111A Deep obsidian outline
    (25, 62, 92, 255):   (11, 12, 16, 255),    # #0B0C10 True OLED black
    (228, 247, 255, 255): (82, 88, 117, 255),  # #525875 Matte gunmetal highlight
    (102, 188, 231, 255): (46, 50, 69, 255),   # #2E3245 Matte stealth titanium
    (57, 123, 170, 255):  (27, 29, 43, 255),   # #1B1D2B Deep graphite shadow
    (124, 244, 237, 255): (0, 240, 255, 255),  # #00F0FF Electric cyber cyan
    (255, 255, 255, 255): (200, 250, 255, 255),# #C8FAFF Cyber spark
}

# Flower / prop colors mapping
PROP_MAP = {
    (255, 80, 80, 255):   (244, 63, 94, 255),  # Neon rose
    (255, 200, 0, 255):   (250, 204, 21, 255), # Neon gold
    (50, 200, 50, 255):   (16, 185, 129, 255), # Emerald
}

def transform_frame(classic_pixels, state, frame_idx):
    canvas = bytearray(classic_pixels)
    for i in range(0, len(canvas), 4):
        c = tuple(canvas[i:i+4])
        if c in CMAP:
            canvas[i:i+4] = bytes(CMAP[c])
        elif c in PROP_MAP:
            canvas[i:i+4] = bytes(PROP_MAP[c])
        elif c[3] > 0:
            # For any slightly varied shade in classic, map by luminance
            lum = (c[0]*299 + c[1]*587 + c[2]*114) // 1000
            if lum < 50:
                canvas[i:i+4] = bytes((15, 17, 26, c[3]))
            elif lum < 100:
                canvas[i:i+4] = bytes((27, 29, 43, c[3]))
            elif lum < 160:
                canvas[i:i+4] = bytes((46, 50, 69, c[3]))
            elif lum < 220:
                canvas[i:i+4] = bytes((82, 88, 117, c[3]))
            else:
                canvas[i:i+4] = bytes((200, 250, 255, c[3]))
    
    # Add subtle neon cyan accents
    def set_px(x, y, rgba):
        if 0 <= x < W and 0 <= y < H:
            idx = (y * W + x) * 4
            canvas[idx:idx+4] = bytes(rgba)

    def get_px(x, y):
        if 0 <= x < W and 0 <= y < H:
            idx = (y * W + x) * 4
            return tuple(canvas[idx:idx+4])
        return (0, 0, 0, 0)

    neon_cyan = (0, 240, 255, 255)
    neon_glow = (14, 165, 233, 255)
    
    # Check if headphones exist at normal positions (left ~8..10, right ~37..39)
    # Head bob offset
    bob = 0
    if state == 'idle':
        bob = [1, 0, -1, 0][frame_idx % 4]
    elif state in ('joy', 'laugh', 'celebrate'):
        bob = -2 if (frame_idx % 2 == 1) else 0

    hy = 14 + bob
    # Left headphone neon accent
    if get_px(9, hy)[3] > 0:
        set_px(9, hy - 1, neon_cyan)
        set_px(9, hy, neon_cyan)
        set_px(9, hy + 1, neon_glow)
    
    # Right headphone neon accent
    if get_px(38, hy)[3] > 0:
        set_px(38, hy - 1, neon_cyan)
        set_px(38, hy, neon_cyan)
        set_px(38, hy + 1, neon_glow)

    # Chest line power core accent
    cy = 31 + bob
    for cx in range(22, 26):
        if get_px(cx, cy)[3] > 0 and get_px(cx, cy)[:3] != (0, 240, 255):
            set_px(cx, cy, neon_cyan)

    # Hover thruster beam at bottom
    by = 44
    if get_px(23, by)[3] > 0 or get_px(24, by)[3] > 0:
        set_px(23, 44, neon_cyan)
        set_px(24, 44, neon_cyan)
        set_px(23, 45, neon_glow)
        set_px(24, 45, neon_glow)

    return canvas

def main():
    classic_dir = Path('LitleHelperClient/Assets/Skins/Classic')
    out_dir = Path('LitleHelperClient/Assets/Skins/StealthNeon')
    out_dir.mkdir(parents=True, exist_ok=True)
    
    count = 0
    for p in sorted(classic_dir.glob('*.png')):
        stem = p.stem # e.g. "idle_1"
        parts = stem.rsplit('_', 1)
        state = parts[0]
        frame_idx = int(parts[1]) - 1 if len(parts) > 1 and parts[1].isdigit() else 0
        
        classic_pixels = read_png(p)
        stealth_pixels = transform_frame(classic_pixels, state, frame_idx)
        write_png(stealth_pixels, W, H, out_dir / p.name)
        count += 1
        
    print(f"Generated {count} StealthNeon frames in {out_dir}")

if __name__ == '__main__':
    main()
