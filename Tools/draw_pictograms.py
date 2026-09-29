"""
Draws stage-3 pictograms in code (black shapes on white, like the Codex-generated set) into %TEMP%/nocx/pictos so
`python Tools/gen_pictograms.py convert` can turn them into UI pictograms. Stand-ins for when Codex image
generation is unavailable; `gen_pictograms.py gen <name>` replaces any of them later (delete the raw PNG first).

    python Tools/draw_pictograms.py [name ...]
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

WORK = os.path.join(os.environ.get("TEMP", "/tmp"), "nocx", "pictos")
N = 2048          # drawn at 2x, saved at 1024
INK, PAPER = 0, 255
W = 64            # standard stroke width (at 2048)


def canvas():
    im = Image.new("L", (N, N), PAPER)
    return im, ImageDraw.Draw(im)


def thick(d, pts, w=W, fill=INK):
    """Polyline with round caps and joints."""
    for a, b in zip(pts, pts[1:]):
        d.line([a, b], fill=fill, width=int(w))
    for p in pts:
        r = w / 2
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)


def disc(d, c, r, fill=INK):
    d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=fill)


def ring(d, c, r, w=W, fill=INK):
    d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], outline=fill, width=int(w))


def rect(d, x0, y0, x1, y1, w=W, fill=None, radius=0):
    if fill is not None:
        d.rounded_rectangle([x0, y0, x1, y1], radius=radius, fill=fill)
    else:
        d.rounded_rectangle([x0, y0, x1, y1], radius=radius, outline=INK, width=int(w))


def arrow(d, a, b, w=W * 1.2, head=190):
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    base = (b[0] - math.cos(ang) * head * 0.8, b[1] - math.sin(ang) * head * 0.8)
    thick(d, [a, base], w)
    left = (b[0] - math.cos(ang - 0.5) * head, b[1] - math.sin(ang - 0.5) * head)
    right = (b[0] - math.cos(ang + 0.5) * head, b[1] - math.sin(ang + 0.5) * head)
    d.polygon([b, left, right], fill=INK)


def arcs(d, c, n=3, start=-40, end=40, r0=120, step=110, w=W * 0.8):
    for i in range(n):
        r = r0 + i * step
        d.arc([c[0] - r, c[1] - r, c[0] + r, c[1] + r], start, end, fill=INK, width=int(w))


def person(d, cx, feet, h, walk=0, facing=1, arms=True, head_turn=0):
    """ISO-sign person. walk: 0 standing, 1 striding (side view, facing ±1)."""
    r = h * 0.085
    head = (cx + head_turn * r * 0.6, feet - h + r)
    disc(d, head, r)
    sh = (cx, head[1] + r * 1.75)
    hip = (cx, feet - h * 0.46)
    bw = h * 0.17
    thick(d, [sh, hip], bw)
    lw = h * 0.1
    if walk:
        f = facing
        thick(d, [hip, (cx + f * h * 0.12, feet - h * 0.24), (cx + f * h * 0.2, feet - lw / 2)], lw)
        thick(d, [hip, (cx - f * h * 0.05, feet - h * 0.22), (cx - f * h * 0.2, feet - lw / 2)], lw)
        if arms:
            thick(d, [(sh[0], sh[1] + lw * 0.3), (cx + f * h * 0.14, sh[1] + h * 0.16), (cx + f * h * 0.2, sh[1] + h * 0.3)], lw * 0.85)
            thick(d, [(sh[0], sh[1] + lw * 0.3), (cx - f * h * 0.12, sh[1] + h * 0.18), (cx - f * h * 0.14, sh[1] + h * 0.32)], lw * 0.85)
    else:
        thick(d, [(cx - bw * 0.28, hip[1]), (cx - bw * 0.3, feet - lw / 2)], lw)
        thick(d, [(cx + bw * 0.28, hip[1]), (cx + bw * 0.3, feet - lw / 2)], lw)
        if arms:
            thick(d, [(cx - bw * 0.62, sh[1] + lw * 0.2), (cx - bw * 0.72, hip[1] + h * 0.04)], lw * 0.85)
            thick(d, [(cx + bw * 0.62, sh[1] + lw * 0.2), (cx + bw * 0.72, hip[1] + h * 0.04)], lw * 0.85)


def person_mask(cx, feet, h, walk=0, facing=1):
    m = Image.new("L", (N, N), 0)
    md = ImageDraw.Draw(m)
    person(Proxy(md), cx, feet, h, walk, facing)
    return m


class Proxy:
    """Draws INK as 255 onto a mask."""

    def __init__(self, d):
        self.d = d

    def line(self, pts, fill=INK, width=1):
        self.d.line(pts, fill=255, width=width)

    def ellipse(self, box, fill=INK, outline=None, width=1):
        self.d.ellipse(box, fill=255)


def dashed_outline(im, mask, w=26, dash=60):
    """Outline of a filled mask, cut into dashes (a figure that is not really there)."""
    grown = mask.filter(ImageFilter.MaxFilter(w | 1))
    edge = ImageChops.subtract(grown, mask)
    stripes = Image.new("L", (N, N), 0)
    sd = ImageDraw.Draw(stripes)
    for k in range(-N, 2 * N, dash * 2):
        sd.polygon([(k, 0), (k + dash, 0), (k + dash + N, N), (k + N, N)], fill=255)
    edge = ImageChops.multiply(edge, stripes)
    im.paste(INK, mask=edge)


def slash(im):
    """ISO prohibition bar across the whole icon, with a white keep-out edge."""
    d = ImageDraw.Draw(im)
    a, b = (330, 330), (N - 330, N - 330)
    d.line([a, b], fill=PAPER, width=int(W * 3.4))
    thick(d, [a, b], W * 1.6)


def footprints(d, x, y, n, step, s=1.0, angle=0.0, stagger=46):
    ca, sa = math.cos(angle), math.sin(angle)
    for i in range(n):
        side = 1 if i % 2 == 0 else -1
        px = x + ca * i * step - sa * side * stagger * s
        py = y + sa * i * step + ca * side * stagger * s
        rx, ry = 44 * s, 26 * s
        d.ellipse([px - rx, py - ry, px + rx, py + ry], fill=INK)
        d.ellipse([px + ca * 62 * s - 22 * s, py + sa * 62 * s - 20 * s, px + ca * 62 * s + 22 * s, py + sa * 62 * s + 20 * s], fill=INK)


def elevator(d, x0, y0, x1, y1, open_=0.0, frame=W):
    """Front view of an elevator: frame, two door panels (open_ 0 closed .. 1 fully open)."""
    rect(d, x0, y0, x1, y1, frame)
    iw = (x1 - x0) - frame * 2
    half = iw / 2
    gap = 14
    panel = half * (1 - open_ * 0.85)
    ix0, ix1 = x0 + frame, x1 - frame
    top, bot = y0 + frame, y1 - frame
    d.rectangle([ix0, top, ix0 + panel - gap, bot], fill=INK)
    d.rectangle([ix1 - panel + gap, top, ix1, bot], fill=INK)


def radio(d, cx, cy, s=1.0):
    w, h = 250 * s, 430 * s
    rect(d, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, fill=INK, radius=50 * s)
    thick(d, [(cx - w * 0.28, cy - h / 2), (cx - w * 0.28, cy - h / 2 - 190 * s)], 46 * s)
    d.rectangle([cx - w * 0.3, cy - h * 0.32, cx + w * 0.3, cy - h * 0.06], fill=PAPER)
    for i in range(3):
        yy = cy + h * 0.08 + i * 50 * s
        d.line([(cx - w * 0.28, yy), (cx + w * 0.28, yy)], fill=PAPER, width=int(18 * s))
    # push-to-talk button on the left side
    d.rectangle([cx - w / 2 - 44 * s, cy - h * 0.2, cx - w / 2 + 4, cy + h * 0.05], fill=INK)


def door(d, x0, y0, x1, y1):
    rect(d, x0, y0, x1, y1, W)
    disc(d, (x1 - 110, (y0 + y1) / 2 + 40), 38)


def monitor(d, cx, cy, w=520, h=380):
    rect(d, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, W, radius=24)
    thick(d, [(cx, cy + h / 2), (cx, cy + h / 2 + 110)], W)
    thick(d, [(cx - 130, cy + h / 2 + 120), (cx + 130, cy + h / 2 + 120)], W)


def hand_point(d, tip, s=1.0, facing=-1):
    """A hand with the index finger pointing (toward facing: -1 left, 1 right)."""
    f = facing
    px, py = tip
    thick(d, [(px, py), (px - f * 230 * s, py)], 70 * s)
    rect(d, px - f * 520 * s if f > 0 else px + 230 * s, py - 60 * s, px - f * 230 * s if f > 0 else px + 520 * s, py + 230 * s, fill=INK, radius=60 * s)
    thick(d, [(px - f * 470 * s, py + 230 * s), (px - f * 640 * s, py + 420 * s)], 150 * s)


# ------------------------------------------------------------------------------------------------ icons
def e_emptyfloor():
    im, d = canvas()
    elevator(d, 520, 380, 1528, 1640, open_=1.0)
    thick(d, [(300, 1700), (1748, 1700)], W)
    d.polygon([(1024, 200), (930, 320), (1118, 320)], fill=INK)
    return im


def e_passenger():
    im, d = canvas()
    rect(d, 420, 300, 1628, 1720, W)
    rect(d, 520, 520, 760, 1080, W * 0.6)            # mirror on the left wall
    dashed_outline(im, person_mask(1300, 1500, 1050), w=30)
    person(d, 1080, 1700, 1100)
    small = person_mask(640, 1040, 470)
    dashed_outline(im, small, w=18, dash=34)
    return im


def e_follower():
    im, d = canvas()
    person(d, 1480, 1560, 1100, walk=1, facing=1)
    footprints(d, 330, 1680, 6, 150, s=1.0)
    return im


def e_echo():
    im, d = canvas()
    pts = [(260, 1780)]
    x, y = 260, 1780
    for i in range(5):
        x += 250
        pts.append((x, y))
        y -= 170
        pts.append((x, y))
    pts += [(1790, y), (1790, 1780)]
    d.polygon(pts, fill=INK)
    person(d, 1560, y + 10, 700, walk=1, facing=1)
    arcs(d, (560, 1420), n=3, start=200, end=290, r0=140, step=120)
    return im


def e_mimic():
    im, d = canvas()
    door(d, 420, 300, 1180, 1760)
    for c in ((760, 820), (840, 990), (720, 1130)):
        for a in range(0, 360, 60):
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            thick(d, [(c[0] + ca * 36, c[1] + sa * 36), (c[0] + ca * 80, c[1] + sa * 80)], 22)
    dashed_outline(im, person_mask(1540, 1760, 1250), w=30)
    return im


def a_no_buttons():
    im, d = canvas()
    for i in range(4):
        ring(d, (1560, 440 + i * 330), 120, W * 0.8)
    # hand from the left, index finger reaching for a button
    thick(d, [(1330, 1100), (1010, 1100)], 110)                 # index finger
    rect(d, 600, 1000, 1030, 1420, fill=INK, radius=110)         # fist
    for k in range(3):
        rect(d, 900, 1180 + k * 80, 1060, 1250 + k * 80, fill=INK, radius=40)
    thick(d, [(760, 1400), (560, 1760)], 260)                    # wrist
    slash(im)
    return im


def a_remote_close():
    im, d = canvas()
    monitor(d, 520, 900, 620, 460)
    elevator(d, 1060, 430, 1820, 1560, open_=0.5)
    arrow(d, (1080, 1720), (1360, 1720), w=W, head=140)
    arrow(d, (1800, 1720), (1520, 1720), w=W, head=140)
    return im


def a_no_talk():
    im, d = canvas()
    radio(d, 1320, 1080, 1.4)
    d.ellipse([420, 880, 760, 1180], outline=INK, width=int(W))
    arcs(d, (700, 1030), n=2, start=-30, end=30, r0=200, step=110)
    slash(im)
    return im


def a_no_lookback():
    im, d = canvas()
    person(d, 1024, 1720, 1300, walk=0, head_turn=-1)
    c = (1024, 560)
    d.arc([c[0] - 330, c[1] - 330, c[0] + 330, c[1] + 330], 200, 330, fill=INK, width=int(W))
    d.polygon([(700, 470), (640, 640), (820, 590)], fill=INK)
    slash(im)
    return im


def a_ride_down():
    im, d = canvas()
    elevator(d, 380, 380, 1160, 1480, open_=0.0)
    arrow(d, (1560, 380), (1560, 1560), w=W * 1.4, head=240)
    thick(d, [(260, 1760), (1788, 1760)], W * 1.3)
    return im


def a_stop_nearest():
    im, d = canvas()
    thick(d, [(360, 200), (360, 1850)], W * 0.7)
    thick(d, [(1060, 200), (1060, 1850)], W * 0.7)
    elevator(d, 440, 900, 980, 1620, open_=0.0, frame=W * 0.8)
    thick(d, [(260, 760), (1160, 760)], W)               # the next floor
    # open palm (stop)
    rect(d, 1320, 820, 1720, 1300, fill=INK, radius=90)
    for i, fx in enumerate((1360, 1460, 1560, 1660)):
        thick(d, [(fx + 20, 840), (fx + 20, 520 + abs(i - 1.5) * 60)], 80)
    thick(d, [(1330, 1100), (1210, 900)], 90)
    thick(d, [(1440, 1300), (1440, 1560)], 200)
    return im


def a_walk_out():
    im, d = canvas()
    elevator(d, 260, 380, 1040, 1640, open_=1.0)
    person(d, 1300, 1640, 1050, walk=1, facing=1)
    arrow(d, (1500, 1800), (1860, 1800), w=W, head=160)
    return im


def a_ptt_release():
    im, d = canvas()
    radio(d, 1260, 1080, 1.4)
    # open hand drawn back from the side button
    rect(d, 420, 960, 720, 1260, fill=INK, radius=70)
    for i in range(4):
        thick(d, [(720, 990 + i * 75), (890, 930 + i * 95)], 56)
    for k in range(3):
        thick(d, [(900 + k * 10, 760 - k * 90), (960 + k * 30, 700 - k * 110)], 34)
    return im


def a_walk_in_step():
    im, d = canvas()
    person(d, 1260, 1440, 1150, walk=1, facing=1)
    footprints(d, 260, 1650, 9, 175, s=0.9)
    return im


def a_ignore():
    im, d = canvas()
    person(d, 1100, 1640, 1200, walk=1, facing=1)
    arcs(d, (560, 1000), n=3, start=120, end=240, r0=120, step=110)
    arrow(d, (1380, 1800), (1840, 1800), w=W, head=170)
    return im


def a_dont_open():
    im, d = canvas()
    door(d, 600, 280, 1448, 1780)
    slash(im)
    return im


ICONS = {f.__name__: f for f in (e_emptyfloor, e_passenger, e_follower, e_echo, e_mimic, a_no_buttons, a_remote_close,
                                 a_no_talk, a_no_lookback, a_ride_down, a_stop_nearest, a_walk_out, a_ptt_release,
                                 a_walk_in_step, a_ignore, a_dont_open)}

if __name__ == "__main__":
    os.makedirs(WORK, exist_ok=True)
    for name in sys.argv[1:] or ICONS:
        img = ICONS[name]().resize((1024, 1024), Image.LANCZOS)
        img.save(os.path.join(WORK, name + ".png"))
        print("drew", name)
