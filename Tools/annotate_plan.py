"""
Annotates the top-down plan renders written by NightOffice > Build > Capture Floor Plan.

    python Tools/annotate_plan.py            -> TestResults/plan_3F.png (+ plan_1F.png), and plan_map.png (both stacked)

Reads TestResults/plan_{n}F_raw.png + plan_{n}F.json (labels, section tints and dimensions in world meters).
"""
import json
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "TestResults")
FONT = "C:/Windows/Fonts/malgun.ttf"
FONT_BOLD = "C:/Windows/Fonts/malgunbd.ttf"


def rgba(hexstr):
    h = hexstr.lstrip("#")
    if len(h) == 6:
        h += "ff"
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4, 6))


def annotate(floor, title):
    raw = os.path.join(RES, f"plan_{floor}F_raw.png")
    meta = json.load(open(os.path.join(RES, f"plan_{floor}F.json"), encoding="utf-8"))
    ppm, x0, z1 = meta["px_per_m"], meta["x0"], meta["z1"]

    def px(x, z):
        return (x - x0) * ppm, (z1 - z) * ppm

    base = Image.open(raw).convert("RGBA")
    over = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    for r in meta["rects"]:
        a, b = px(r["x0"], r["z1"]), px(r["x1"], r["z0"])
        d.rectangle([a, b], fill=rgba(r["fill"]))
    for c in meta.get("circles", []):
        cx, cy = px(c["x"], c["z"])
        rr = c["r"] * ppm
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=rgba(c["color"]), width=3)
    img = Image.alpha_composite(base, over)
    d = ImageDraw.Draw(img)

    for dim in meta["dims"]:
        a, b = px(dim["x0"], dim["z0"]), px(dim["x1"], dim["z1"])
        d.line([a, b], fill=(40, 40, 40, 255), width=2)
        for p in (a, b):
            d.ellipse([p[0] - 4, p[1] - 4, p[0] + 4, p[1] + 4], fill=(40, 40, 40, 255))
        f = ImageFont.truetype(FONT, 17)
        mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        text = dim["text"]
        w = d.textlength(text, font=f)
        vertical = abs(a[0] - b[0]) < abs(a[1] - b[1])
        pos = (mx + 8, my - 10) if vertical else (mx - w / 2, my - 26)
        d.rectangle([pos[0] - 3, pos[1] - 1, pos[0] + w + 3, pos[1] + 22], fill=(250, 250, 246, 235))
        d.text(pos, text, font=f, fill=(30, 30, 30, 255))

    for lb in meta["labels"]:
        if "ax" in lb:
            a, b = px(lb["x"], lb["z"]), px(lb["ax"], lb["az"])
            col = rgba(lb["color"])
            d.line([a, b], fill=col, width=2)
            d.ellipse([b[0] - 5, b[1] - 5, b[0] + 5, b[1] + 5], outline=col, width=2)
    for lb in meta["labels"]:
        f = ImageFont.truetype(FONT_BOLD if lb["size"] >= 20 else FONT, lb["size"])
        x, y = px(lb["x"], lb["z"])
        w = d.textlength(lb["text"], font=f)
        hgt = lb["size"] * 1.25
        d.rectangle([x - w / 2 - 4, y - hgt / 2 - 2, x + w / 2 + 4, y + hgt / 2 + 2], fill=(252, 252, 248, 225))
        d.text((x - w / 2, y - hgt / 2), lb["text"], font=f, fill=rgba(lb["color"]))

    # scale bar (bottom-left)
    sx, sy = 24, img.height - 30
    d.line([(sx, sy), (sx + 10 * ppm, sy)], fill=(20, 20, 20, 255), width=4)
    d.text((sx, sy - 28), "10 m", font=ImageFont.truetype(FONT, 18), fill=(20, 20, 20, 255))

    # title bar (title + note) + north arrow
    bar = 92
    out = Image.new("RGBA", (img.width, img.height + bar), (250, 250, 246, 255))
    out.paste(img, (0, bar))
    d = ImageDraw.Draw(out)
    d.text((18, 10), title, font=ImageFont.truetype(FONT_BOLD, 28), fill=(20, 20, 20, 255))
    d.text((20, 54), meta.get("note", ""), font=ImageFont.truetype(FONT, 18), fill=(60, 60, 60, 255))
    nx = img.width - 44
    d.polygon([(nx, 12), (nx - 12, 44), (nx + 12, 44)], fill=(20, 20, 20, 255))
    d.text((nx - 7, 48), "N", font=ImageFont.truetype(FONT_BOLD, 18), fill=(20, 20, 20, 255))
    path = os.path.join(RES, f"plan_{floor}F.png")
    out.convert("RGB").save(path)
    print("wrote", path, out.size)
    return out


if __name__ == "__main__":
    upper = annotate(3, "2F~4F 공통 평면 (3F를 위에서 본 모습)")
    ground = annotate(1, "1F 평면 (관리사무소 · 로비 · 1층 복도)")
    combo = Image.new("RGB", (upper.width, upper.height + ground.height + 12), (255, 255, 255))
    combo.paste(upper.convert("RGB"), (0, 0))
    combo.paste(ground.convert("RGB"), (0, upper.height + 12))
    path = os.path.join(RES, "plan_map.png")
    combo.save(path)
    print("wrote", path, combo.size)
