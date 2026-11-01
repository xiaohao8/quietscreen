# -*- coding: utf-8 -*-
"""Build a one-page presentation of the Quiet icon."""

import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ICON = os.path.join(ROOT, "packaging", "assets", "app-icon-256.png")
# use the 1024 master from the design output if present, else the 256 asset
MASTER = os.path.join(HERE, "out", "master-1024.png")
if os.path.exists(MASTER):
    ICON = MASTER

F = "C:\\Windows\\Fonts\\"
SEG = F + "segoeui.ttf"
SEG_B = F + "segoeuib.ttf"
SEG_L = F + "segoeuil.ttf"
YAHEI = F + "msyh.ttc"
YAHEI_B = F + "msyhbd.ttc"


def font(path, size):
    return ImageFont.truetype(path, size)


def text(d, xy, s, f, fill, anchor="la", spacing=0):
    if spacing:
        x, y = xy
        for ch in s:
            d.text((x, y), ch, font=f, fill=fill, anchor=anchor)
            x += d.textlength(ch, font=f) + spacing
    else:
        d.text(xy, s, font=f, fill=fill, anchor=anchor)


def main():
    W, H = 1600, 1000
    img = Image.new("RGBA", (W, H), (245, 245, 247, 255))
    d = ImageDraw.Draw(img)

    # very soft background wash
    for y in range(H):
        t = y / H
        c = (
            int(247 - 4 * t),
            int(247 - 4 * t),
            int(249 - 3 * t),
            255,
        )
        d.line([(0, y), (W, y)], fill=c)

    # header
    text(d, (80, 58), "QUIET  \u00b7  \u5c4f\u606f", font(YAHEI_B, 24), (32, 33, 38), spacing=2)
    text(d, (80, 94), "APP ICON  /  2026", font(SEG, 15), (135, 137, 145))
    d.line([(80, 132), (1520, 132)], fill=(222, 223, 228), width=1)

    # icon card
    card = (80, 176, 680, 776)
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle((card[0] + 10, card[1] + 22, card[2] + 10, card[3] + 22), 44,
                         fill=(20, 22, 38, 55))
    shadow = shadow.filter(ImageFilter.GaussianBlur(28))
    img = Image.alpha_composite(img, shadow)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle(card, 44, fill=(255, 255, 255, 255), outline=(232, 233, 238), width=1)

    icon = Image.open(ICON).convert("RGBA").resize((540, 540), Image.LANCZOS)
    img.alpha_composite(icon, (110, 216))

    # right column
    x = 748
    text(d, (x, 205), "Quiet \u00b7 \u5c4f\u606f", font(YAHEI_B, 54), (24, 25, 32))
    text(d, (x, 278), "Screen off. Work on.", font(SEG_L, 34), (78, 82, 105))
    text(d, (x, 330), "\u5c4f\u5e55\u7184\u4e86\uff0c\u6d3b\u7167\u5e72\u3002", font(YAHEI, 22), (90, 93, 108))

    body = (
        "A midnight squircle. One warm crescent. A laptop with its\n"
        "screen truly off \u2014 quiet, precise, and legible from 1024 px\n"
        "down to the Windows taskbar."
    )
    yy = 392
    for line in body.split("\n"):
        text(d, (x, yy), line, font(SEG, 20), (105, 108, 122))
        yy += 30

    # palette
    text(d, (x, 505), "PALETTE", font(SEG_B, 14), (140, 142, 150), spacing=2)
    sw = [("#2A3374", (42, 51, 116)), ("#0A0C1D", (10, 12, 29)),
          ("#FFE2A8", (255, 226, 168)), ("#F6F7FC", (246, 247, 252))]
    px = x
    for name, rgb in sw:
        d.ellipse((px, 536, px + 34, 570), fill=rgb + (255,), outline=(220, 222, 230), width=1)
        text(d, (px, 580), name, font(SEG, 13), (125, 127, 136))
        px += 92

    # specs
    text(d, (x, 640), "SYSTEM", font(SEG_B, 14), (140, 142, 150), spacing=2)
    specs = [
        ("Grid", "1024 \u00d7 1024, transparent"),
        ("Shape", "superellipse n = 5  (squircle)"),
        ("Light", "one source, top-left / warm moon"),
        ("Optical", "dedicated 16\u201348 px masters"),
    ]
    yy = 672
    for k, v in specs:
        text(d, (x, yy), k, font(SEG_B, 15), (70, 72, 84))
        text(d, (x + 100, yy), v, font(SEG, 15), (115, 118, 130))
        yy += 28

    # size strip
    d.rounded_rectangle((80, 812, 1520, 944), 28, fill=(22, 23, 29, 255))
    text(d, (112, 834), "SIZES", font(SEG_B, 13), (150, 152, 160), spacing=2)
    sizes = [256, 128, 64, 48, 32, 16]
    disp = {256: 72, 128: 56, 64: 44, 48: 36, 32: 24, 16: 16}
    sx = 230
    for s in sizes:
        ds = disp[s]
        ic = icon.resize((ds, ds), Image.LANCZOS)
        iy = 858 + (72 - ds) // 2
        img.alpha_composite(ic, (sx, iy))
        text(d, (sx + ds // 2, 930), str(s), font(SEG, 12), (150, 152, 160), anchor="ma")
        sx += ds + 64

    text(d, (1470, 866), "Quiet \u00b7 \u5c4f\u606f", font(YAHEI_B, 18), (230, 231, 236), anchor="ra")
    text(d, (1470, 896), "Screen off. Work on.", font(SEG_L, 15), (150, 152, 160), anchor="ra")

    out = os.path.join(HERE, "out", "icon-preview.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.convert("RGB").save(out, "PNG")
    print("wrote", out)


if __name__ == "__main__":
    main()
