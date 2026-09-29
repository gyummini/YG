"""
Procedural, seamlessly tiling textures for the art-direction test (stand-ins until generated/hand-made ones replace
them). Writes Assets/_Project/Textures/ArtTest/<name>_albedo.png (+ _normal, _mask where it matters).

    python Tools/gen_art_textures.py

- wall     3.2 x 3.2 m (one floor height): cream paint above a dark green-grey enamel band (걸레받이 도장), grime.
- floor    2 x 2 m: terrazzo (인조석 물갈기) chips in grey cement.
- ceiling  4 x 4 m: matte white paint with a few faint water stains.
- parapet  2 x 2 m: painted concrete for the open corridor's outer wall.
- door     one door face (UV 0..1): dark painted steel, scuffs by the lock, dirt at the bottom.
- hydrant  hydrant cabinet door face (UV 0..1): red enamel with the 소화전 label.
- stainless 1 x 1 m: brushed stainless (elevator doors and panels) with smudges.
- sky      equirect night sky with city glow at the horizon (Skybox/Panoramic).
Masks follow URP Lit's metallic/smoothness map: R = metallic, A = smoothness.
The helpers (fractal, wall_layout, door_wear, ...) are shared with Tools/gen_art_gpt.py, which builds the same maps
from GPT-generated material tiles.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Textures", "ArtTest")
FONT = "C:/Windows/Fonts/malgunbd.ttf"

WALL_PAINT = np.array([0.80, 0.78, 0.72])
WALL_BAND = np.array([0.27, 0.34, 0.31])
BAND_TOP = 1.05  # meters: top of the enamel band
DOOR_PAINT = np.array([0.42, 0.37, 0.33])


def fractal(n, octaves=6, persistence=0.55, base=4, seed=None):
    """Periodic fractal noise in [0,1] (sum of band-limited periodic noises from the FFT, tiles seamlessly)."""
    rng = np.random.default_rng(seed)
    out = np.zeros((n, n))
    amp, total = 1.0, 0.0
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(n)[None, :]
    r = np.sqrt(fx * fx + fy * fy) * n
    for o in range(octaves):
        f0 = base * (2 ** o)
        band = np.exp(-((r - f0) ** 2) / (2 * (f0 * 0.5) ** 2))
        spec = (rng.normal(size=(n, n)) + 1j * rng.normal(size=(n, n))) * band
        layer = np.real(np.fft.ifft2(spec))
        layer /= np.abs(layer).max() + 1e-9
        out += layer * amp
        total += amp
        amp *= persistence
    out /= total
    return (out - out.min()) / (out.max() - out.min() + 1e-9)


def normal_from_height(h, strength=2.0):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h)
    n = np.stack([-dx, dy, nz], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save_rgb(name, rgb, out=OUT):
    os.makedirs(out, exist_ok=True)
    Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8), "RGB").save(os.path.join(out, name + ".png"))


def save_normal(name, h, strength, out=OUT):
    os.makedirs(out, exist_ok=True)
    Image.fromarray(normal_from_height(h, strength)).save(os.path.join(out, name + ".png"))


def save_mask(name, metallic, smooth, out=OUT):
    os.makedirs(out, exist_ok=True)
    a = np.dstack([metallic, np.zeros_like(metallic), np.zeros_like(metallic), smooth])
    Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8), "RGBA").save(os.path.join(out, name + ".png"))


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def resized(noise, w, h):
    return np.asarray(Image.fromarray((noise * 255).astype(np.uint8)).resize((w, h))) / 255.0


# ------------------------------------------------------------------ wall: 3.2 m square, v = height within the floor
def wall_layout(n):
    """Rows top→bottom over one 3.2 m floor height: v (meters from the floor), band and line masks, grime amount."""
    v = np.linspace(1, 0, n)[:, None] * 3.2 * np.ones((1, n))
    is_band = (v < BAND_TOP).astype(float)
    line = ((v > BAND_TOP) & (v < BAND_TOP + 0.025)).astype(float)
    n2 = fractal(n, 5, 0.6, base=2, seed=2)
    # darker toward the floor, drip streaks from the band line, scattered stains
    grime = np.clip(1.0 - v / 0.35, 0, 1) * 0.18 + (n2 > 0.8) * (n2 - 0.8) * 0.5
    streaks = fractal(n, 4, base=24, seed=4)
    streaks = np.repeat(streaks[:1, :], n, axis=0) * np.clip((1.6 - v) / 1.6, 0, 1)
    grime += np.clip(streaks - 0.66, 0, 1) * 0.35
    return v, is_band, line, np.clip(grime, 0, 0.3)


def wall(paint=None, band=None, relief=None, out=OUT):
    """paint/band: optional (n, n, 3) material tiles in [0,1] (already tiled to the wall's size); flat colors otherwise."""
    n = 2048
    v, is_band, line, grime = wall_layout(n)
    n1 = fractal(n, 6, seed=1)
    if paint is None:
        paint = np.broadcast_to(WALL_PAINT, (n, n, 3)) * (0.93 + 0.1 * n1)[..., None]
    if band is None:
        band = np.broadcast_to(WALL_BAND, (n, n, 3)) * (0.93 + 0.1 * n1)[..., None]
    col = lerp(paint, band, is_band)
    col = lerp(col, np.broadcast_to(np.array([0.2, 0.25, 0.23]), (n, n, 3)), line)
    col *= (1 - grime)[..., None]
    save_rgb("wall_albedo", col, out)
    if relief is None:
        relief = fractal(n, 7, 0.5, base=8, seed=3) * 0.6 + n1 * 0.4
    save_normal("wall_normal", relief, 3.0, out)
    save_mask("wall_mask", np.zeros((n, n)), np.where(is_band > 0, 0.32, 0.22) - grime * 0.3, out)


# ------------------------------------------------------------------ floor: terrazzo
def floor():
    n = 2048
    rng = np.random.default_rng(7)
    col = np.broadcast_to(np.array([0.46, 0.46, 0.44]), (n, n, 3)).copy()
    col *= (0.9 + 0.2 * fractal(n, 5, seed=11))[..., None]
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    chips = [(215, 212, 205), (40, 40, 42), (150, 130, 110), (180, 175, 165), (95, 92, 88)]
    for _ in range(9000):
        x, y = rng.integers(0, n, 2)
        r = int(rng.choice([3, 4, 5, 7, 9], p=[0.35, 0.3, 0.2, 0.1, 0.05]))
        c = chips[rng.integers(0, len(chips))]
        pts = [(x + r * np.cos(a) * rng.uniform(0.6, 1.2), y + r * np.sin(a) * rng.uniform(0.6, 1.2)) for a in np.linspace(0, 2 * np.pi, 7)[:-1]]
        for ox in (-n, 0, n):  # wrap so it tiles
            for oy in (-n, 0, n):
                d.polygon([(px + ox, py + oy) for px, py in pts], fill=c)
    col = np.asarray(img).astype(float) / 255.0
    wear = fractal(n, 5, base=2, seed=12)
    col *= (0.92 + 0.12 * wear)[..., None]
    save_rgb("floor_albedo", col)
    save_normal("floor_normal", fractal(n, 6, base=16, seed=13), 0.8)
    save_mask("floor_mask", np.zeros((n, n)), 0.45 + 0.2 * wear)


# ------------------------------------------------------------------ ceiling and parapet
def ceiling_stains(n):
    """A few faint water stains (0..1 amount) — rare, soft, with a slightly darker tide line."""
    stain = fractal(n, 5, base=2, seed=21)
    ring = np.clip((stain - 0.84) * 8, 0, 1) - np.clip((stain - 0.87) * 8, 0, 1)
    return np.clip(ring * 0.25 + np.clip(stain - 0.8, 0, 1) * 1.2, 0, 0.3)


def ceiling():
    n = 1024
    col = np.broadcast_to(np.array([0.82, 0.82, 0.8]), (n, n, 3)).copy() * (0.95 + 0.08 * fractal(n, 6, seed=22))[..., None]
    col = lerp(col, np.broadcast_to(np.array([0.62, 0.55, 0.42]), (n, n, 3)), ceiling_stains(n))
    save_rgb("ceiling_albedo", col)


def parapet():
    n = 1024
    col = np.broadcast_to(np.array([0.72, 0.71, 0.68]), (n, n, 3)).copy() * (0.85 + 0.2 * fractal(n, 6, seed=31))[..., None]
    col *= (1 - np.clip(fractal(n, 4, base=2, seed=32) - 0.65, 0, 1))[..., None]
    save_rgb("parapet_albedo", col)
    save_normal("parapet_normal", fractal(n, 7, base=12, seed=33), 3.0)


# ------------------------------------------------------------------ door face (UV 0..1 over 0.87 x 2.06 m)
DOOR_W, DOOR_H = 512, 1216


def door_wear():
    """(lock-zone scuff amount, bottom dirt amount) over the door face, rows top→bottom."""
    u = np.linspace(0, 1, DOOR_W)[None, :] * np.ones((DOOR_H, 1))
    vv = np.linspace(1, 0, DOOR_H)[:, None] * np.ones((1, DOOR_W))
    lockzone = np.exp(-(((u - 0.885) / 0.07) ** 2 + ((vv - 0.49) / 0.12) ** 2))
    dirt = np.clip((0.12 - vv) / 0.12, 0, 1) * 0.5
    return lockzone, dirt


def door(paint=None, out=OUT):
    """paint: optional (DOOR_H, DOOR_W, 3) painted-steel surface; flat color with noise otherwise."""
    if paint is None:
        paint = np.broadcast_to(DOOR_PAINT, (DOOR_H, DOOR_W, 3)) * (0.94 + 0.1 * resized(fractal(1024, 6, seed=41), DOOR_W, DOOR_H))[..., None]
    lockzone, dirt = door_wear()
    col = lerp(paint, np.broadcast_to(np.array([0.36, 0.33, 0.31]), (DOOR_H, DOOR_W, 3)), lockzone * 0.5)
    col *= (1 - dirt)[..., None]
    save_rgb("door_albedo", col, out)


# ------------------------------------------------------------------ hydrant cabinet door (UV 0..1 over 0.61 x 1.26 m)
def hydrant():
    w, h = 512, 1056
    img = Image.new("RGB", (w, h), (150, 18, 14))
    d = ImageDraw.Draw(img)
    d.text((w / 2, h * 0.62), "소 화 전", fill=(240, 236, 228), font=ImageFont.truetype(FONT, 78), anchor="mm")
    d.text((w / 2, h * 0.7), "FIRE HOSE", fill=(240, 236, 228), font=ImageFont.truetype(FONT, 30), anchor="mm")
    d.rectangle([w * 0.18, h * 0.78, w * 0.82, h * 0.86], outline=(240, 236, 228), width=4)
    d.text((w / 2, h * 0.82), "사용법: 문을 열고 호스를 편다", fill=(240, 236, 228), font=ImageFont.truetype(FONT, 22), anchor="mm")
    arr = np.asarray(img).astype(float) / 255.0
    arr *= (0.9 + 0.15 * resized(fractal(1024, 6, seed=51), w, h))[..., None]
    save_rgb("hydrant_albedo", arr)


# ------------------------------------------------------------------ brushed stainless (1 x 1 m)
STAINLESS = np.array([0.76, 0.77, 0.78])  # sRGB; ~0.55 linear, the reflectance of stainless steel


def stainless(brushed=None, out=OUT):
    """brushed: optional (n, n, 3) tile; otherwise horizontal hairlines from row-stretched noise. Smudges lower the smoothness."""
    n = 1024
    rng = np.random.default_rng(61)
    if brushed is None:
        walk = rng.normal(size=(n, n)).cumsum(1) / np.sqrt(n)
        walk -= np.linspace(0, 1, n)[None, :] * walk[:, -1:]  # hairlines drift along the row, closed so it tiles
        lines = np.repeat(rng.normal(size=(n, 1)), n, axis=1) * 0.5 + walk * 0.35
        lines = (lines - lines.mean()) / (lines.std() + 1e-9)
        brushed = np.broadcast_to(STAINLESS, (n, n, 3)) * (1 + 0.035 * lines)[..., None]
    smudge = np.clip(fractal(n, 5, base=10, seed=62) - 0.6, 0, 1) * 2.0  # hand-sized smears, not big blotches
    col = brushed * (1 - 0.04 * smudge)[..., None]
    save_rgb("stainless_albedo", col, out)
    save_mask("stainless_mask", np.ones((n, n)), 0.66 - 0.1 * smudge, out)


# ------------------------------------------------------------------ night sky (equirect 2:1, for Skybox/Panoramic)
def sky():
    """Overcast city night: dark navy overhead, sodium-orange light pollution at the horizon lighting the cloud base."""
    w, h = 2048, 1024
    elev = (90 - 180 * (np.arange(h) + 0.5) / h)[:, None] * np.ones((1, w))
    glow = np.exp(-np.clip(elev, 0, None) / 9.0)
    top = np.array([0.018, 0.024, 0.045])
    horizon = np.array([0.2, 0.12, 0.07])
    col = lerp(np.broadcast_to(top, (h, w, 3)), np.broadcast_to(horizon, (h, w, 3)), glow)
    clouds = fractal(1024, 6, 0.55, base=3, seed=71)
    clouds = np.asarray(Image.fromarray((clouds * 255).astype(np.uint8)).resize((w, h))) / 255.0
    band = np.clip((elev - 2) / 6, 0, 1) * np.clip((45 - elev) / 25, 0, 1)
    cover = np.clip((clouds - 0.45) * 2.5, 0, 1) * band
    col = lerp(col, col * (1.4 + 1.6 * glow)[..., None], cover * 0.8)
    col = np.where((elev < 0)[..., None], np.array([0.012, 0.012, 0.014]), col)
    save_rgb("sky_night", col)


if __name__ == "__main__":
    sky()
    stainless()
    wall()
    floor()
    ceiling()
    parapet()
    door()
    hydrant()
    print("textures ->", OUT)
