# -*- coding: utf-8 -*-
"""
Export the Quiet icon to every Microsoft Store / MSIX asset size.

Run from the repository root:
    python design/export_assets.py

Masters are rendered at 2048 px (standard) and 2048 px (optical, for small
sizes), then reduced with Lanczos.  The optical master has a thicker bezel,
a larger moon and a fuller squircle, so 16-48 px icons stay legible.
"""

import io
import os
import struct
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import make_icon  # noqa: E402

PACK = os.path.join(ROOT, "packaging", "assets")
SRC_ASSETS = os.path.join(ROOT, "src", "QuietScreen", "Assets")
OUT = os.path.join(HERE, "out", "release")
os.makedirs(PACK, exist_ok=True)
os.makedirs(SRC_ASSETS, exist_ok=True)
os.makedirs(OUT, exist_ok=True)


def lerp(c1, c2, t):
    return tuple(int(round(a + (b - a) * t)) for a, b in zip(c1, c2))


def gradient_bg(w, h):
    """The same midnight gradient as the icon, full-bleed for the wide tile."""
    top = (0x2A, 0x33, 0x74)
    mid = (0x16, 0x1B, 0x3E)
    low = (0x08, 0x09, 0x15)
    bot = (0x03, 0x04, 0x08)
    stops = [(0.0, top), (0.26, mid), (0.60, low), (1.0, bot)]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    v = yy / max(h - 1, 1)
    rgb = np.zeros((h, w, 3), dtype=np.float32)
    for i in range(len(stops) - 1):
        p0, c0 = stops[i]
        p1, c1 = stops[i + 1]
        m = (v >= p0) & (v <= p1)
        t = np.clip((v - p0) / max(p1 - p0, 1e-6), 0, 1)[..., None]
        col = (np.array(c0, dtype=np.float32) / 255.0) * (1 - t) + (
            np.array(c1, dtype=np.float32) / 255.0
        ) * t
        rgb[m] = col[m]
    # cool glow behind the device
    cy, cx = h * 0.44, w * 0.50
    d = np.sqrt(((xx - cx) / (w * 0.62)) ** 2 + ((yy - cy) / (h * 0.85)) ** 2)
    g = np.clip(1 - d, 0, 1) ** 2.1
    glow = np.array(make_icon.GLOW, dtype=np.float32) / 255.0
    rgb = np.clip(rgb + glow * g[..., None] * 0.55, 0, 1)
    # vignette
    dv = np.sqrt(((xx - w / 2) / (w * 0.62)) ** 2 + ((yy - h / 2) / (h * 0.62)) ** 2)
    vig = np.clip(1.05 - dv, 0, 1)[..., None]
    rgb = rgb * (0.86 + 0.14 * vig)
    return Image.fromarray(np.clip(rgb * 255 + 0.5, 0, 255).astype(np.uint8), "RGB")


def glyph_from_master(icon):
    """Cut the laptop (not the squircle) out of the 1024 master."""
    S = 1024
    frame = make_icon.rounded_rect(S, 228, 274, 796, 668, 46, 1.2)
    base = make_icon.rounded_polygon(
        S, [(214, 664), (810, 664), (832, 700), (192, 700)], 14, 1.2
    )
    notch = make_icon.disc(S, 512, 700, 15, 1.0)
    alpha = np.clip(frame + base - notch, 0, 1)
    arr = np.array(icon.convert("RGBA"), dtype=np.uint8)
    arr[..., 3] = np.clip(alpha * 255.0, 0, 255).astype(np.uint8)
    out = Image.fromarray(arr, "RGBA")
    box = out.getbbox()
    return out.crop(box)


def wide_tile(glyph, w, h, glyph_scale=0.80):
    bg = gradient_bg(w, h).convert("RGBA")
    gh = int(round(h * glyph_scale))
    gw = int(round(gh * glyph.width / glyph.height))
    g = glyph.resize((gw, gh), Image.LANCZOS)

    # soft contact shadow under the device
    shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sh_alpha = g.split()[3].point(lambda a: int(a * 0.55))
    shadow.paste((0, 0, 0, 255), ((w - gw) // 2, (h - gh) // 2 + int(h * 0.03)), sh_alpha)
    shadow = shadow.filter(ImageFilter.GaussianBlur(max(2, int(h * 0.035))))
    bg = Image.alpha_composite(bg, shadow)

    x = (w - gw) // 2
    y = (h - gh) // 2
    bg.alpha_composite(g, (x, y))
    return bg


def save(img, name, rgb=False):
    path = os.path.join(PACK, name)
    (img.convert("RGB") if rgb else img).save(path, "PNG")
    return path


def ico_entry_bmp(img):
    size = img.size[0]
    px = img.convert("RGBA").load()
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    and_row = ((size + 31) // 32) * 4
    and_mask = bytes(and_row * size)
    header = struct.pack(
        "<IiiHHIIiiII",
        40,
        size,
        size * 2,
        1,
        32,
        0,
        len(xor) + len(and_mask),
        0,
        0,
        0,
        0,
    )
    return header + bytes(xor) + and_mask


def ico_entry_png(img):
    buf = io.BytesIO()
    img.save(buf, "PNG")
    return buf.getvalue()


def build_ico(entries, path):
    """entries: list of (size, PIL image). BMP < 64 px, PNG >= 64 px."""
    data = []
    for size, img in entries:
        blob = ico_entry_bmp(img) if size < 64 else ico_entry_png(img)
        data.append((size, blob))
    count = len(data)
    offset = 6 + 16 * count
    out = bytearray(struct.pack("<HHH", 0, 1, count))
    for size, blob in data:
        w = 0 if size >= 256 else size
        out += struct.pack(
            "<BBBBHHII",
            w,
            w,
            0,
            0,
            1,
            32,
            len(blob),
            offset,
        )
        offset += len(blob)
    for _, blob in data:
        out += blob
    with open(path, "wb") as f:
        f.write(out)
    return path


def main():
    print("rendering masters ...")
    std = make_icon.render("a", 2048, optical=False)          # 7.2% transparent margin
    opt = make_icon.render("a", 2048, optical=True)           # bolder, for <=48
    std_full = make_icon.render("a", 2048, body_ratio=0.96, optical=False)
    opt_full = make_icon.render("a", 2048, body_ratio=0.96, optical=True)
    glyph = glyph_from_master(std.resize((1024, 1024), Image.LANCZOS))

    def s(size):
        return std.resize((size, size), Image.LANCZOS)

    def o(size):
        return opt.resize((size, size), Image.LANCZOS)

    def sf(size):
        return std_full.resize((size, size), Image.LANCZOS)

    def of(size):
        return opt_full.resize((size, size), Image.LANCZOS)

    print("writing master + size ladder ...")
    std.resize((1024, 1024), Image.LANCZOS).save(os.path.join(OUT, "icon-1024.png"))
    for size in (512, 256, 128, 64, 48, 32, 16):
        (o(size) if size <= 48 else s(size)).save(os.path.join(OUT, f"icon-{size}.png"))
    s(300).save(os.path.join(OUT, "store-icon-300.png"))

    print("writing MSIX logos ...")
    # Square 44 (five scales)
    for scale, size in (("100", 44), ("125", 55), ("150", 66), ("200", 88), ("400", 176)):
        save(o(size) if size <= 55 else s(size), f"Square44x44Logo.scale-{scale}.png")
    # Square 150
    for scale, size in (("100", 150), ("125", 188), ("150", 225), ("200", 300), ("400", 600)):
        save(s(size), f"Square150x150Logo.scale-{scale}.png")
    # Store logo
    for scale, size in (("100", 50), ("125", 62), ("150", 75), ("200", 100), ("400", 200)):
        save(o(size), f"StoreLogo.scale-{scale}.png")
    # Wide tile
    for scale, (w, h) in (
        ("100", (310, 150)),
        ("125", (388, 188)),
        ("150", (465, 225)),
        ("200", (620, 300)),
        ("400", (1240, 600)),
    ):
        save(wide_tile(glyph, w, h), f"Wide310x150Logo.scale-{scale}.png", rgb=True)
    # Target size icons (taskbar / explorer / jump lists)
    for size in (16, 20, 24, 30, 32, 36, 44, 48):
        save(of(size), f"Square44x44Logo.targetsize-{size}.png")
    for size in (60, 64, 72, 96, 256):
        save(sf(size), f"Square44x44Logo.targetsize-{size}.png")
    save(of(24), "Square44x44Logo.targetsize-24_altform-unplated.png")

    # Root logos without scale suffix (used by some layouts / docs)
    save(o(44), "Square44x44Logo.png")
    save(s(150), "Square150x150Logo.png")
    save(s(50), "StoreLogo.png")
    save(wide_tile(glyph, 310, 150), "Wide310x150Logo.png", rgb=True)

    print("writing app icon ...")
    app256 = s(256)
    app256.save(os.path.join(PACK, "app-icon-256.png"))
    app256.save(os.path.join(OUT, "app-icon-256.png"))
    app256.save(os.path.join(SRC_ASSETS, "app-icon-256.png"))

    ico_entries = [
        (16, of(16)),
        (20, of(20)),
        (24, of(24)),
        (32, of(32)),
        (40, of(40)),
        (48, of(48)),
        (64, sf(64)),
        (128, sf(128)),
        (256, sf(256)),
    ]
    ico = build_ico(ico_entries, os.path.join(PACK, "app.ico"))
    with open(ico, "rb") as f:
        blob = f.read()
    with open(os.path.join(SRC_ASSETS, "app.ico"), "wb") as f:
        f.write(blob)
    with open(os.path.join(OUT, "app.ico"), "wb") as f:
        f.write(blob)

    # A clean full-bleed 1024 for press / Store hero use.
    std_full.resize((1024, 1024), Image.LANCZOS).save(os.path.join(OUT, "icon-1024-large.png"))

    print("done")
    for name in sorted(os.listdir(OUT)):
        print("  outputs/.../icon/" + name)


if __name__ == "__main__":
    main()

