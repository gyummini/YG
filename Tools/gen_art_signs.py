"""
Sign and paper textures for the art-direction test (stand-ins; real signage should use the bundled OFL font).

    python Tools/gen_art_signs.py

Writes Assets/_Project/Textures/ArtTest/signs/: unit_<n>.png (door number plates 201~409), office.png (관리사무소
plate), floor_<f>.png (painted floor number by the elevator), firedoor.png (방화문 sticker), notices.png (게시판).
"""
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "_Project", "Textures", "ArtTest", "signs")
os.makedirs(OUT, exist_ok=True)
BOLD, REG = "C:/Windows/Fonts/malgunbd.ttf", "C:/Windows/Fonts/malgun.ttf"
random.seed(5)


def font(path, size):
    return ImageFont.truetype(path, size)


def grime(img, amount=18):
    """A little unevenness so flat colors don't read as UI."""
    px = img.load()
    w, h = img.size
    for _ in range(w * h // 40):
        x, y = random.randrange(w), random.randrange(h)
        d = random.randint(-amount, amount // 2)
        c = px[x, y]
        px[x, y] = tuple(max(0, min(255, v + d)) for v in c[:3]) + tuple(c[3:])
    return img.filter(ImageFilter.GaussianBlur(0.6))


# door number plates (cream plate, dark engraved-looking digits)
for floor in (2, 3, 4):
    for i in range(1, 10):
        n = floor * 100 + i
        img = Image.new("RGB", (256, 112), (214, 206, 186))
        d = ImageDraw.Draw(img)
        d.rectangle([3, 3, 252, 108], outline=(150, 140, 120), width=3)
        d.text((128, 58), str(n), fill=(40, 36, 32), font=font(BOLD, 78), anchor="mm")
        grime(img, 10).save(os.path.join(OUT, f"unit_{n}.png"))

# 관리사무소 plate (blue enamel)
img = Image.new("RGB", (640, 160), (28, 64, 120))
d = ImageDraw.Draw(img)
d.rectangle([6, 6, 633, 153], outline=(210, 220, 235), width=4)
d.text((320, 82), "관 리 사 무 소", fill=(240, 244, 250), font=font(BOLD, 86), anchor="mm")
grime(img).save(os.path.join(OUT, "office.png"))

# painted floor numbers by the elevator (big stencil digits on the wall)
for floor in (1, 2, 3, 4):
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([36, 36, 476, 476], outline=(30, 70, 60, 235), width=26)
    d.text((256, 270), f"{floor}", fill=(30, 70, 60, 235), font=font(BOLD, 300), anchor="mm")
    img.save(os.path.join(OUT, f"floor_{floor}.png"))

# 방화문 sticker
img = Image.new("RGB", (640, 260), (236, 232, 222))
d = ImageDraw.Draw(img)
d.rectangle([0, 0, 640, 84], fill=(190, 30, 26))
d.text((320, 44), "방 화 문", fill=(250, 246, 240), font=font(BOLD, 60), anchor="mm")
d.text((320, 140), "항상 닫아 두십시오", fill=(40, 36, 32), font=font(BOLD, 52), anchor="mm")
d.text((320, 210), "화재 시 연기와 불길을 막아 줍니다", fill=(90, 84, 76), font=font(REG, 32), anchor="mm")
grime(img, 12).save(os.path.join(OUT, "firedoor.png"))

# 게시판: cork with pinned notices
W, H = 1024, 716
img = Image.new("RGB", (W, H), (150, 112, 74))
d = ImageDraw.Draw(img)
for _ in range(2500):
    x, y = random.randrange(W), random.randrange(H)
    c = random.randint(-25, 20)
    d.point((x, y), fill=(150 + c, 112 + c, 74 + c))
papers = [
    ((40, 40, 330, 440), "공지사항", ["엘리베이터 정기 점검", "10월 2일 (수) 10:00~12:00", "점검 중 계단을 이용해", "주시기 바랍니다.", "", "관리사무소"]),
    ((370, 70, 660, 400), "관리비 납부 안내", ["9월분 관리비 고지서가", "우편함에 투입되었습니다.", "납부 기한: 10월 25일", "", "문의: 관리사무소"]),
    ((700, 30, 990, 330), "분실물", ["검정 우산 1개", "1층 로비에서 보관 중", "", "※ 밤 10시 이후", "복도 소음 자제"]),
    ((420, 430, 760, 690), "야간 순찰 안내", ["야간에는 관리 직원이", "복도를 순찰합니다.", "노크 없이 문을 열지", "마세요."]),
]
for (x0, y0, x1, y1), title, lines in papers:
    shade = Image.new("RGBA", (x1 - x0 + 8, y1 - y0 + 8), (0, 0, 0, 90))
    img.paste(shade, (x0 + 6, y0 + 8), shade)
    d.rectangle([x0, y0, x1, y1], fill=(236, 234, 226))
    d.text(((x0 + x1) / 2, y0 + 44), title, fill=(30, 30, 30), font=font(BOLD, 36), anchor="mm")
    d.line([x0 + 20, y0 + 74, x1 - 20, y0 + 74], fill=(60, 60, 60), width=2)
    for i, line in enumerate(lines):
        d.text((x0 + 22, y0 + 96 + i * 38), line, fill=(40, 40, 40), font=font(REG, 25))
    d.ellipse([(x0 + x1) / 2 - 9, y0 + 6, (x0 + x1) / 2 + 9, y0 + 24], fill=(200, 40, 40))
grime(img, 14).save(os.path.join(OUT, "notices.png"))

# 우편함 number labels: 9 x 3 atlas, rows 4xx / 3xx / 2xx from the top (Tools/blender_art_lobby.py maps each door)
CW, CH = 128, 64
img = Image.new("RGB", (9 * CW, 3 * CH), (226, 224, 214))
d = ImageDraw.Draw(img)
for row, floor in enumerate((4, 3, 2)):
    for col in range(9):
        x0, y0 = col * CW, row * CH
        d.rectangle([x0 + 2, y0 + 2, x0 + CW - 3, y0 + CH - 3], outline=(120, 118, 110), width=2)
        d.text((x0 + CW / 2, y0 + CH / 2 + 1), f"{floor}0{col + 1}", fill=(30, 30, 34), font=font(BOLD, 40), anchor="mm")
grime(img, 10).save(os.path.join(OUT, "mailbox_labels.png"))
print("signs ->", OUT)
