#!/usr/bin/env python3
"""
Generate Pixel Little Helper branded GRUB and Syslinux themes.
Creates:
 - background.png (1920x1080) for GRUB UEFI
 - splash_bios.png (1024x768) for Syslinux Legacy BIOS
 - select_*.png (9-slice highlight bar)
 - icons/*.png (OS & action icons: Windows, Debian, Tools, Reboot, Power, etc.)
 - theme.txt for GRUB
"""
import os
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

THEME_DIR = Path("PixelHelperPXE/themes/pixel")
THEME_DIR.mkdir(parents=True, exist_ok=True)
ICONS_DIR = THEME_DIR / "icons"
ICONS_DIR.mkdir(parents=True, exist_ok=True)

# Colors from Pixel Little Helper Brand Identity
C_BG_TOP = (11, 21, 35)         # #0b1523
C_BG_BOTTOM = (17, 35, 60)      # #11233c
C_CARD_BG = (14, 28, 46, 235)   # #0e1c2e with alpha
C_CARD_BORDER = (34, 64, 104)   # #224068
C_ACCENT_BLUE = (36, 107, 253)  # #246bfd
C_CYAN_LIGHT = (130, 202, 239)  # #82caef
C_CYAN_GLOW = (151, 255, 241)   # #97fff1
C_TEXT_WHITE = (255, 255, 255)
C_TEXT_MUTED = (159, 181, 206)  # #9fb5ce
C_GREEN_ONLINE = (106, 215, 167)# #6ad7a7

def get_font(size: int, bold: bool = False):
    candidates = [
        "C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",
        "arialbd.ttf" if bold else "arial.ttf"
    ]
    for c in candidates:
        if os.path.exists(c):
            try:
                return ImageFont.truetype(c, size)
            except Exception:
                pass
    return ImageFont.load_default()

def draw_robot_face(draw: ImageDraw.ImageDraw, x: int, y: int, size: int = 56):
    # Proportional robot head from styles.css .pixel-face
    scale = size / 100.0
    w = int(100 * scale)
    h = int(88 * scale)
    r = int(16 * scale)
    
    # Outer head border & glow
    draw.rounded_rectangle([x, y, x + w, y + h], radius=r, fill=(20, 63, 86), outline=C_CYAN_LIGHT, width=max(2, int(6 * scale)))
    
    # Eyes
    eye_w = max(2, int(10 * scale))
    eye_h = max(4, int(16 * scale))
    eye1_x = x + int(24 * scale)
    eye2_x = x + w - int(24 * scale) - eye_w
    eye_y = y + int(24 * scale)
    draw.rounded_rectangle([eye1_x, eye_y, eye1_x + eye_w, eye_y + eye_h], radius=int(4 * scale), fill=C_CYAN_GLOW)
    draw.rounded_rectangle([eye2_x, eye_y, eye2_x + eye_w, eye_y + eye_h], radius=int(4 * scale), fill=C_CYAN_GLOW)
    
    # Smile
    mouth_w = int(32 * scale)
    mouth_h = int(14 * scale)
    mouth_x = x + (w - mouth_w) // 2
    mouth_y = y + int(48 * scale)
    draw.arc([mouth_x, mouth_y, mouth_x + mouth_w, mouth_y + mouth_h], start=10, end=170, fill=C_CYAN_GLOW, width=max(2, int(4 * scale)))

def generate_background_1080p():
    W, H = 1920, 1080
    im = Image.new("RGBA", (W, H))
    draw = ImageDraw.Draw(im)
    
    # Vertical gradient
    for y in range(H):
        t = y / float(H)
        r = int(C_BG_TOP[0] * (1 - t) + C_BG_BOTTOM[0] * t)
        g = int(C_BG_TOP[1] * (1 - t) + C_BG_BOTTOM[1] * t)
        b = int(C_BG_TOP[2] * (1 - t) + C_BG_BOTTOM[2] * t)
        draw.line([(0, y), (W, y)], fill=(r, g, b, 255))
        
    # Subtle radial glow behind center
    cx, cy = W // 2, 420
    for rad in range(600, 50, -50):
        alpha = int(18 * (1 - rad / 600))
        draw.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=(36, 107, 253, alpha))
        
    # Top Header Banner
    header_y = 65
    draw_robot_face(draw, 340, header_y, size=64)
    
    f_title = get_font(30, bold=True)
    f_sub = get_font(14, bold=False)
    draw.text((420, header_y + 4), "PIXEL LITTLE HELPER", font=f_title, fill=C_TEXT_WHITE)
    draw.text((422, header_y + 42), "AUTONOMOUS PXE NETWORK DEPLOYMENT", font=f_sub, fill=C_CYAN_LIGHT)
    
    # Server & Status badges on right
    f_badge = get_font(13, bold=True)
    f_badge_sub = get_font(12, bold=False)
    
    badge_x = 1580
    draw.rounded_rectangle([badge_x - 190, header_y + 8, badge_x, header_y + 36], radius=6, fill=(18, 40, 68), outline=C_CARD_BORDER)
    draw.ellipse([badge_x - 176, header_y + 18, badge_x - 166, header_y + 28], fill=C_GREEN_ONLINE)
    draw.text((badge_x - 156, header_y + 13), "UEFI Secure Boot", font=f_badge, fill=C_TEXT_WHITE)
    draw.text((badge_x - 190, header_y + 44), "TFTP + HTTP Netboot Engine", font=f_badge_sub, fill=C_TEXT_MUTED)

    # Horizontal divider
    draw.line([(340, header_y + 85), (1580, header_y + 85)], fill=(34, 64, 104, 180), width=1)
    
    # Menu container card
    card_x1, card_y1 = 340, 180
    card_x2, card_y2 = 1580, 890
    card_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    card_draw = ImageDraw.Draw(card_layer)
    card_draw.rounded_rectangle([card_x1, card_y1, card_x2, card_y2], radius=16, fill=C_CARD_BG, outline=C_CARD_BORDER, width=2)
    
    # Card Header strip
    card_draw.rounded_rectangle([card_x1 + 2, card_y1 + 2, card_x2 - 2, card_y1 + 44], radius=14, fill=(20, 39, 64, 255))
    f_card_hdr = get_font(12, bold=True)
    card_draw.text((card_x1 + 24, card_y1 + 15), "СЕТЕВАЯ УСТАНОВКА И АДМИНИСТРИРОВАНИЕ / BOOT SELECTION", font=f_card_hdr, fill=C_CYAN_LIGHT)
    
    im = Image.alpha_composite(im, card_layer)
    draw = ImageDraw.Draw(im)
    
    # Footer Instructions
    f_footer = get_font(13, bold=False)
    footer_text = "↑ / ↓ : Навигация по меню   •   Enter : Запуск выбранной ОС   •   c : Консоль GRUB   •   e : Редактировать"
    bbox = draw.textbbox((0, 0), footer_text, font=f_footer)
    tw = bbox[2] - bbox[0]
    draw.text(((W - tw) // 2, 920), footer_text, font=f_footer, fill=C_TEXT_MUTED)
    
    f_copy = get_font(11, bold=False)
    copy_text = "Pixel Little Helper PXE Engine • Server 172.16.16.61"
    draw.text((card_x1, 955), copy_text, font=f_copy, fill=(98, 124, 158))
    
    # Save as 24-bit RGB PNG for maximum GRUB compatibility
    rgb_im = im.convert("RGB")
    rgb_im.save(THEME_DIR / "background.png", "PNG", optimize=True)
    print(f"[OK] Generated {THEME_DIR / 'background.png'}")

def generate_syslinux_splash():
    # 1024x768 for Syslinux vesamenu
    W, H = 1024, 768
    im = Image.new("RGBA", (W, H))
    draw = ImageDraw.Draw(im)
    
    for y in range(H):
        t = y / float(H)
        r = int(C_BG_TOP[0] * (1 - t) + C_BG_BOTTOM[0] * t)
        g = int(C_BG_TOP[1] * (1 - t) + C_BG_BOTTOM[1] * t)
        b = int(C_BG_TOP[2] * (1 - t) + C_BG_BOTTOM[2] * t)
        draw.line([(0, y), (W, y)], fill=(r, g, b, 255))
        
    # Header: Mascot & Title
    draw_robot_face(draw, 110, 26, size=52)
    f_title = get_font(22, bold=True)
    f_sub = get_font(12, bold=False)
    draw.text((178, 28), "PIXEL LITTLE HELPER", font=f_title, fill=C_TEXT_WHITE)
    draw.text((180, 58), "AUTONOMOUS PXE BOOT MENU (BIOS / LEGACY)", font=f_sub, fill=C_CYAN_LIGHT)
    
    # Mode badge on top right
    draw.rounded_rectangle([750, 32, 914, 58], radius=6, fill=(18, 40, 68), outline=C_CARD_BORDER)
    draw.ellipse([762, 40, 772, 50], fill=C_GREEN_ONLINE)
    f_badge = get_font(11, bold=True)
    draw.text((780, 37), "Legacy BIOS Mode", font=f_badge, fill=C_TEXT_WHITE)

    # Divider line
    draw.line([(110, 88), (914, 88)], fill=(34, 64, 104, 180), width=1)
    
    # Menu container card (matching MENU VSHIFT 9, MARGIN 16, WIDTH 96)
    # Row 9 is 144px, Row 24 is 384px
    card_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    cdraw = ImageDraw.Draw(card_layer)
    card_x1, card_y1 = 110, 110
    card_x2, card_y2 = 914, 430
    cdraw.rounded_rectangle([card_x1, card_y1, card_x2, card_y2], radius=14, fill=C_CARD_BG, outline=C_CARD_BORDER, width=2)
    
    # Card Header strip
    cdraw.rounded_rectangle([card_x1 + 2, card_y1 + 2, card_x2 - 2, card_y1 + 34], radius=12, fill=(20, 39, 64, 255))
    f_card_hdr = get_font(11, bold=True)
    cdraw.text((card_x1 + 18, card_y1 + 10), "AVAILABLE NETWORK INSTALLATIONS", font=f_card_hdr, fill=C_CYAN_LIGHT)
    
    im = Image.alpha_composite(im, card_layer)
    draw = ImageDraw.Draw(im)
    
    # Footer
    f_copy = get_font(11, bold=False)
    draw.text((110, 725), "Pixel Little Helper PXE Engine • Server 172.16.16.61", font=f_copy, fill=(98, 124, 158))
    
    rgb_im = im.convert("RGB")
    rgb_im.save(THEME_DIR / "splash_bios.png", "PNG", optimize=True)
    print(f"[OK] Generated {THEME_DIR / 'splash_bios.png'}")

def generate_select_9slice():
    # Generates 9-slice images for selected_item_pixmap_style
    # Size 16x16: 4 corner, 4 edge, 1 center
    bg = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(bg)
    # Glowing pill with subtle border
    d.rounded_rectangle([0, 0, 15, 15], radius=4, fill=(36, 107, 253, 240), outline=(98, 159, 253, 255), width=1)
    
    pieces = {
        "select_nw.png": (0, 0, 4, 4),
        "select_n.png":  (4, 0, 12, 4),
        "select_ne.png": (12, 0, 16, 4),
        "select_w.png":  (0, 4, 4, 12),
        "select_c.png":  (4, 4, 12, 12),
        "select_e.png":  (12, 4, 16, 12),
        "select_sw.png": (0, 12, 4, 16),
        "select_s.png":  (4, 12, 12, 16),
        "select_se.png": (12, 12, 16, 16),
    }
    for name, box in pieces.items():
        crop = bg.crop(box)
        crop.save(THEME_DIR / name, "PNG")
    print(f"[OK] Generated 9-slice select_*.png in {THEME_DIR}")

def generate_icons():
    # Generate 24x24 icons
    size = 24
    
    # 1. Windows icon (4 modern angled blue tiles)
    win_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(win_im)
    d.polygon([(2, 5), (10, 4), (10, 11), (2, 11)], fill=(0, 164, 239))
    d.polygon([(12, 3), (22, 1), (22, 11), (12, 11)], fill=(0, 120, 215))
    d.polygon([(2, 13), (10, 13), (10, 20), (2, 19)], fill=(67, 181, 245))
    d.polygon([(12, 13), (22, 13), (22, 23), (12, 21)], fill=(36, 107, 253))
    win_im.save(ICONS_DIR / "windows.png")
    
    # 2. Debian icon (Red swirl / stylized circle)
    deb_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(deb_im)
    d.ellipse([2, 2, 21, 21], outline=(215, 10, 83), width=3)
    d.ellipse([6, 6, 17, 17], fill=(215, 10, 83))
    d.ellipse([8, 8, 15, 15], fill=(14, 28, 46))
    deb_im.save(ICONS_DIR / "debian.png")
    
    # 3. SystemRescue / Toolkit icon (Amber wrench/gear)
    rec_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(rec_im)
    d.ellipse([3, 3, 20, 20], outline=(245, 158, 11), width=2)
    d.line([(6, 12), (17, 12)], fill=(245, 158, 11), width=2)
    d.line([(12, 6), (12, 17)], fill=(245, 158, 11), width=2)
    d.ellipse([8, 8, 15, 15], fill=(245, 158, 11))
    rec_im.save(ICONS_DIR / "sysrescue.png")
    rec_im.save(ICONS_DIR / "gnu-linux.png")
    
    # 4. Clonezilla / Disk icon (Teal twin storage)
    clone_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(clone_im)
    d.rounded_rectangle([2, 4, 18, 12], radius=2, outline=(45, 212, 191), width=2)
    d.rounded_rectangle([5, 12, 21, 20], radius=2, fill=(20, 80, 80), outline=(45, 212, 191), width=2)
    d.ellipse([14, 15, 17, 18], fill=(45, 212, 191))
    clone_im.save(ICONS_DIR / "clonezilla.png")
    
    # 5. Local Hard Drive icon
    hdd_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(hdd_im)
    d.rounded_rectangle([3, 4, 20, 19], radius=3, outline=(159, 181, 206), width=2)
    d.line([(5, 15), (18, 15)], fill=(159, 181, 206), width=1)
    d.ellipse([15, 16, 17, 18], fill=C_GREEN_ONLINE)
    hdd_im.save(ICONS_DIR / "harddrive.png")
    hdd_im.save(ICONS_DIR / "disk.png")
    
    # 6. Reboot icon (Green circular arrow)
    reb_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(reb_im)
    d.arc([3, 3, 20, 20], start=30, end=300, fill=(74, 222, 128), width=2)
    d.polygon([(18, 5), (22, 10), (16, 11)], fill=(74, 222, 128))
    reb_im.save(ICONS_DIR / "reboot.png")
    
    # 7. Power off / Halt icon (Red circle with vertical line)
    pwr_im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(pwr_im)
    d.arc([3, 5, 20, 21], start=45, end=315, fill=(244, 63, 94), width=2)
    d.line([(12, 2), (12, 11)], fill=(244, 63, 94), width=2)
    pwr_im.save(ICONS_DIR / "power.png")
    pwr_im.save(ICONS_DIR / "halt.png")
    
    print(f"[OK] Generated entry icons in {ICONS_DIR}")

def generate_theme_txt():
    # theme.txt specification matching GRUB 2 gfxmenu standard
    # Background card starts at x: 340 (17.7%), y: 240 (22.2%)
    # Width: 1240 (64.5%), Height: 620 (57.4%)
    txt = """# Pixel Little Helper GRUB Theme
# High-Resolution Branded UEFI Bootloader

title-text: ""
desktop-image: "background.png"
desktop-color: "#0b1523"
terminal-font: "dejavu_14"

+ boot_menu {
    left = 370
    top = 245
    width = 1180
    height = 580
    item_font = "dejavu_14"
    selected_item_font = "dejavu_bold_14"
    item_color = "#9fb5ce"
    selected_item_color = "#ffffff"
    item_height = 40
    item_padding = 10
    item_spacing = 6
    icon_width = 24
    icon_height = 24
    icon_spacing = 14
    selected_item_pixmap_style = "select_*.png"
    scrollbar = false
}

+ progress_bar {
    id = "__timeout__"
    left = 370
    top = 845
    width = 1180
    height = 14
    font = "dejavu_11"
    text_color = "#82caef"
    fg_color = "#246bfd"
    bg_color = "#142840"
    border_color = "#224068"
    text = "Автоматическая загрузка через %d сек..."
}
"""
    (THEME_DIR / "theme.txt").write_text(txt, encoding="utf-8")
    print(f"[OK] Generated {THEME_DIR / 'theme.txt'}")

if __name__ == "__main__":
    generate_background_1080p()
    generate_syslinux_splash()
    generate_select_9slice()
    generate_icons()
    generate_theme_txt()
    print("[SUCCESS] All theme assets generated successfully!")

