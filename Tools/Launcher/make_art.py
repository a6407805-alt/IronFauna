"""Generates the IronFauna launcher's built-in art (background.png, launcher.ico, emblem.png).
The launcher swaps the background for launcher-art.jpg from the latest release once the Unity publisher uploads one.
Run: python3 Tools/Launcher/make_art.py  (writes into Launcher/Assets/)"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Launcher", "Assets")
os.makedirs(OUT, exist_ok=True)
CRIMSON = (200, 40, 46); CYAN = (36, 229, 255); YELLOW = (230, 184, 0); SLATE = (14, 18, 26)
random.seed(7)

def background(w=1920, h=1080):
    img = Image.new("RGB", (w, h), SLATE)
    px = img.load()
    for y in range(h):
        for x in range(0, w):
            # cool vertical falloff + a warm glow behind where the hero art sits (right side)
            d = math.hypot((x - w * 0.68) / w, (y - h * 0.45) / h)
            glow = max(0.0, 1.0 - d * 1.9)
            r = int(16 + 120 * glow ** 2.2); g = int(20 + 40 * glow ** 2); b = int(30 + 60 * glow ** 1.5)
            px[x, y] = (r, g, b)
    d = ImageDraw.Draw(img, "RGBA")
    # hex grid
    s = 34
    hh = s * math.sqrt(3)
    for row in range(int(h / hh) + 2):
        for col in range(int(w / (s * 1.5)) + 2):
            cx = col * s * 1.5; cy = row * hh + (hh / 2 if col % 2 else 0)
            pts = [(cx + s * math.cos(math.radians(a)), cy + s * math.sin(math.radians(a))) for a in range(0, 360, 60)]
            a = 10 + int(14 * random.random())
            d.polygon(pts, outline=(120, 150, 190, a))
    # diagonal light blades
    for i, (col, x0, wid, al) in enumerate([(CRIMSON, 1050, 150, 70), (CYAN, 1330, 40, 60), (CRIMSON, 1500, 18, 90), (YELLOW, 980, 6, 110)]):
        blade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        bd = ImageDraw.Draw(blade)
        bd.polygon([(x0, 0), (x0 + wid, 0), (x0 + wid - 520, h), (x0 - 520, h)], fill=col + (al,))
        blade = blade.filter(ImageFilter.GaussianBlur(18 if wid > 30 else 4))
        img.paste(blade, (0, 0), blade)
    # huge faint emblem
    em = emblem(900, outline_only=True)
    img.paste(em, (1060, 120), em)
    # vignette
    vig = Image.new("L", (w, h), 255)
    vd = ImageDraw.Draw(vig)
    for i in range(60):
        vd.rectangle([i * 6, i * 4, w - i * 6, h - i * 4], outline=int(90 + 165 * (i / 60) ** 0.6))
    vig = vig.filter(ImageFilter.GaussianBlur(60))
    dark = Image.new("RGB", (w, h), (4, 6, 10))
    img = Image.composite(img, dark, vig)
    return img

def emblem(size, outline_only=False):
    """A hex badge with a stylised mech visor: the IronFauna mark."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = size / 2
    def hexp(r, rot=30):
        return [(c + r * math.cos(math.radians(a + rot)), c + r * math.sin(math.radians(a + rot))) for a in range(0, 360, 60)]
    if outline_only:
        d.polygon(hexp(size * 0.48), outline=CRIMSON + (55,), width=max(2, size // 90))
        d.polygon(hexp(size * 0.40), outline=CYAN + (30,), width=max(1, size // 200))
        d.polygon([(c - size * 0.26, c - size * 0.04), (c + size * 0.26, c - size * 0.04), (c + size * 0.18, c + size * 0.08), (c - size * 0.18, c + size * 0.08)], outline=CYAN + (60,), width=max(2, size // 120))
        return im
    d.polygon(hexp(size * 0.48), fill=(26, 31, 40, 255), outline=CRIMSON + (255,), width=max(2, size // 16))
    d.polygon(hexp(size * 0.34), fill=(14, 18, 26, 255))
    # visor
    d.polygon([(c - size * 0.27, c - size * 0.07), (c + size * 0.27, c - size * 0.07), (c + size * 0.19, c + size * 0.09), (c - size * 0.19, c + size * 0.09)], fill=CYAN + (255,))
    d.rectangle([c - size * 0.03, c - size * 0.30, c + size * 0.03, c - size * 0.14], fill=YELLOW + (255,))
    return im

background().save(os.path.join(OUT, "background.png"), optimize=True)
big = emblem(512)
big.save(os.path.join(OUT, "emblem.png"))
big.save(os.path.join(OUT, "launcher.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote", os.listdir(OUT))
