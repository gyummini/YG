"""
Compose TestResults/art/<shot>_<look>.png into comparison sheets (rows = looks, columns = shots).

    python Tools/art_sheet.py [looks] [out-name]      e.g.  python Tools/art_sheet.py 0ABC compare
                                                            python Tools/art_sheet.py A,GA,D,C,GC,E gpt
Looks are single letters, or comma-separated keys when any is longer (GA = the GPT concept paint-over for A).
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "TestResults", "art")
FONT = "C:/Windows/Fonts/malgunbd.ttf"
LOOKS = {
    "0": "현재 (그레이박스)",
    "A": "A 사실적",
    "B": "B 스타일화 (로우파이)",
    "C": "C 절제된 영화 톤",
    "D": "A + GPT 텍스처",
    "E": "C + GPT 텍스처",
    "GA": "A 목표 (GPT 콘셉트)",
    "GB": "B 목표 (GPT 콘셉트)",
    "GC": "C 목표 (GPT 콘셉트)",
}
SHOTS = [("corridor", "3층 복도"), ("hall", "엘리베이터 홀"), ("lobby", "1층 로비"), ("door", "세대 현관문")]


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else "0ABC"
    looks = arg.split(",") if "," in arg else list(arg)
    name = sys.argv[2] if len(sys.argv) > 2 else "compare"
    cw, ch, left, top, gap = 480, 270, 250, 50, 8
    W = left + len(SHOTS) * (cw + gap)
    H = top + len(looks) * (ch + gap)
    sheet = Image.new("RGB", (W, H), (18, 19, 21))
    d = ImageDraw.Draw(sheet)
    f_big, f_small = ImageFont.truetype(FONT, 26), ImageFont.truetype(FONT, 20)
    for j, (_, label) in enumerate(SHOTS):
        d.text((left + j * (cw + gap) + 8, 12), label, fill=(210, 210, 210), font=f_small)
    for i, look in enumerate(looks):
        y = top + i * (ch + gap)
        d.text((14, y + ch // 2 - 16), LOOKS.get(look, look), fill=(222, 190, 120), font=f_big if len(LOOKS.get(look, look)) < 9 else f_small)
        for j, (shot, _) in enumerate(SHOTS):
            p = os.path.join(ART, f"{shot}_{look}.png")
            if not os.path.exists(p):
                continue
            im = Image.open(p).convert("RGB")
            # cover-fit into the cell
            r = max(cw / im.width, ch / im.height)
            im = im.resize((int(im.width * r + 0.5), int(im.height * r + 0.5)), Image.NEAREST if look == "B" else Image.LANCZOS)
            x0, y0 = (im.width - cw) // 2, (im.height - ch) // 2
            sheet.paste(im.crop((x0, y0, x0 + cw, y0 + ch)), (left + j * (cw + gap), y))
    out = os.path.join(ART, name + ".png")
    sheet.save(out)
    print(out)


if __name__ == "__main__":
    main()
