"""
Pixel-font signs for the lo-fi look: numbers drawn on the texel grid (3x5 digits) instead of downscaled vector text,
so they survive a 225-line screen, point sampling and dithering. Flat, extreme colours (dithering only shows up in
mid-tones). Each texel is written as an 8x8 block so point sampling stays exact.

    python Tools/gen_pixel_signs.py

Writes Assets/_Project/Textures/ArtTest/signs_pixel/:
    unit_<n>.png   door number plate, 15 x 9 texels (201~409)
    floor_<f>.png  painted floor number in a circle, 21 x 21 texels (2x digit), transparent outside (1~4)
    elev_<f>.png   elevator floor indicator, 7 x 9 texels, amber on black (1~4)
"""
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Textures", "ArtTest", "signs_pixel")
os.makedirs(OUT, exist_ok=True)
BLOCK = 8

DIGITS = {
    "0": ("111", "101", "101", "101", "111"), "1": ("010", "110", "010", "010", "111"),
    "2": ("111", "001", "111", "100", "111"), "3": ("111", "001", "111", "001", "111"),
    "4": ("101", "101", "111", "001", "001"), "5": ("111", "100", "111", "001", "111"),
    "6": ("111", "100", "111", "101", "111"), "7": ("111", "001", "001", "001", "001"),
    "8": ("111", "101", "111", "101", "111"), "9": ("111", "101", "111", "001", "111"),
}


def save(texels, name):
    """texels: list of rows of RGBA tuples."""
    h, w = len(texels), len(texels[0])
    im = Image.new("RGBA", (w, h))
    for y, row in enumerate(texels):
        for x, c in enumerate(row):
            im.putpixel((x, y), c)
    im.resize((w * BLOCK, h * BLOCK), Image.NEAREST).save(os.path.join(OUT, name + ".png"))


def stamp(texels, text, x0, y0, color, scale=1):
    for i, ch in enumerate(text):
        for r, bits in enumerate(DIGITS[ch]):
            for c, b in enumerate(bits):
                if b == "1":
                    for sy in range(scale):
                        for sx in range(scale):
                            texels[y0 + r * scale + sy][x0 + (i * 4 + c) * scale + sx] = color


# door number plates: border, cream plate, black digits
for floor in (2, 3, 4):
    for i in range(1, 10):
        n = f"{floor}0{i}"
        W, H = 15, 9
        border, plate, ink = (58, 54, 48, 255), (226, 221, 206, 255), (22, 20, 18, 255)
        t = [[border if x in (0, W - 1) or y in (0, H - 1) else plate for x in range(W)] for y in range(H)]
        stamp(t, n, 2, 2, ink)
        save(t, "unit_" + n)

# painted floor numbers: thick ring and a 2x-scaled digit, transparent elsewhere
for floor in (1, 2, 3, 4):
    S = 21
    paint, clear = (26, 64, 54, 255), (0, 0, 0, 0)
    t = [[clear] * S for _ in range(S)]
    c = (S - 1) / 2
    for y in range(S):
        for x in range(S):
            d = ((x - c) ** 2 + (y - c) ** 2) ** 0.5
            if 8.4 <= d <= 10.4:
                t[y][x] = paint
    stamp(t, str(floor), 7, 5, paint, scale=2)
    save(t, f"floor_{floor}")

# elevator floor indicator: amber digit on black glass
for floor in (1, 2, 3, 4):
    W, H = 7, 9
    t = [[(10, 8, 6, 255)] * W for _ in range(H)]
    stamp(t, str(floor), 2, 2, (255, 150, 40, 255))
    save(t, f"elev_{floor}")
print("pixel signs ->", OUT)
