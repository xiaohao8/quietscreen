# -*- coding: utf-8 -*-
"""
Quiet 路 灞忔伅 鈥?Apple-style app icon renderer.

Design language: continuous-curvature squircle, midnight gradient, one light
source, single warm accent, no text. Rendered with numpy at 3x and downsampled,
so every edge and glow is clean at 1024 and at 16 px.

Usage:
  python make_icon.py --variant a --size 1024 --out out/icon-a-1024.png
  python make_icon.py --variant b --size 1024 --out out/icon-b-1024.png
"""

import argparse
import os
import numpy as np
from PIL import Image, ImageFilter

# ---------------------------------------------------------------- palette ----
INK_TOP = (0x2B, 0x31, 0x66)
INK_MID = (0x15, 0x18, 0x33)
INK_LOW = (0x08, 0x09, 0x15)
INK_BOT = (0x04, 0x05, 0x0A)
GLOW = (0x4C, 0x58, 0xDC)
FRAME_TOP = (0xFF, 0xFF, 0xFF)
FRAME_BOT = (0xDC, 0xE0, 0xF2)
SCREEN_TOP = (0x07, 0x09, 0x13)
SCREEN_BOT = (0x0A, 0x0C, 0x1D)
MOON = (0xFF, 0xE4, 0xA8)
MOON_HOT = (0xFF, 0xF2, 0xCF)


def rgb(c):
    return np.array(c, dtype=np.float32) / 255.0


def stops(t, pairs):
    pos = np.array([p for p, _ in pairs], dtype=np.float32)
    cols = np.array([c for _, c in pairs], dtype=np.float32) / 255.0
    out = np.empty(t.shape + (3,), dtype=np.float32)
    for ch in range(3):
        out[..., ch] = np.interp(t, pos, cols[:, ch])
    return out


def over(dst, color, alpha):
    a = alpha[..., None].astype(np.float32)
    c = color
    if isinstance(color, tuple):
        c = rgb(color)
    return dst * (1.0 - a) + c * a


def screen_over(dst, color, alpha):
    """Additive light, for glows."""
    a = alpha[..., None].astype(np.float32)
    c = color if not isinstance(color, tuple) else rgb(color)
    return np.clip(dst + c * a, 0.0, 1.0)


def blur(mask, radius):
    if radius <= 0:
        return mask
    im = Image.fromarray(np.clip(mask * 255.0, 0, 255).astype(np.uint8), "L")
    im = im.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(im, dtype=np.float32) / 255.0


def superellipse(size, cx, cy, hw, hh, n=5.0, aa=1.2):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    dx = np.abs(xx - cx) / hw
    dy = np.abs(yy - cy) / hh
    r = (dx ** n + dy ** n) ** (1.0 / n)
    # convert the 1.0 iso-line into a ~aa-pixel-wide antialiased edge
    edge = aa / max(hw, hh)
    return np.clip((1.0 - r) / edge + 0.5, 0.0, 1.0)


def rounded_rect(size, x0, y0, x1, y1, r, aa=1.2):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    cx, cy = (x0 + x1) * 0.5, (y0 + y1) * 0.5
    hx, hy = (x1 - x0) * 0.5, (y1 - y0) * 0.5
    qx = np.abs(xx - cx) - (hx - r)
    qy = np.abs(yy - cy) - (hy - r)
    ax = np.maximum(qx, 0.0)
    ay = np.maximum(qy, 0.0)
    d = np.sqrt(ax * ax + ay * ay) + np.minimum(np.maximum(qx, qy), 0.0) - r
    return np.clip(0.5 - d / aa, 0.0, 1.0)


def rounded_polygon(size, pts, r, aa=1.2):
    """Convex polygon with rounded corners (Minkowski sum with a disc)."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    P = np.array(pts, dtype=np.float32)
    n = len(P)
    cen = P.mean(axis=0)
    inward = []
    for i in range(n):
        p0 = P[i]
        p1 = P[(i + 1) % n]
        e = p1 - p0
        e = e / np.linalg.norm(e)
        nrm = np.array([-e[1], e[0]], dtype=np.float32)
        if np.dot(nrm, cen - p0) < 0:
            nrm = -nrm
        inward.append((p0, nrm))

    # inset polygon: intersect the edges moved inward by r
    inset = []
    for i in range(n):
        p_prev, n_prev = inward[(i - 1) % n]
        p_cur, n_cur = inward[i]
        c1 = float(np.dot(n_prev, p_prev)) + r
        c2 = float(np.dot(n_cur, p_cur)) + r
        det = n_prev[0] * n_cur[1] - n_cur[0] * n_prev[1]
        if abs(det) < 1e-6:
            continue
        vx = (c1 * n_cur[1] - c2 * n_prev[1]) / det
        vy = (n_prev[0] * c2 - n_cur[0] * c1) / det
        inset.append((vx, vy))
    inset = np.array(inset, dtype=np.float32)

    # inside the inset polygon?
    inside = np.ones((size, size), dtype=bool)
    for p0, nrm in inward:
        inside &= ((xx - p0[0]) * nrm[0] + (yy - p0[1]) * nrm[1] - r) >= 0.0

    # distance to the inset polygon boundary; offset outward by r
    d = np.full((size, size), 1e9, dtype=np.float32)
    for i in range(n):
        a = inset[i]
        b = inset[(i + 1) % n]
        ab = b - a
        t = ((xx - a[0]) * ab[0] + (yy - a[1]) * ab[1]) / max(float(np.dot(ab, ab)), 1e-6)
        t = np.clip(t, 0.0, 1.0)
        px = a[0] + t * ab[0]
        py = a[1] + t * ab[1]
        d = np.minimum(d, np.sqrt((xx - px) ** 2 + (yy - py) ** 2))
    edge = np.clip((r - d) / aa + 0.5, 0.0, 1.0)
    return np.where(inside, 1.0, edge).astype(np.float32)


def disc(size, cx, cy, r, aa=1.2):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) - r
    return np.clip(0.5 - d / aa, 0.0, 1.0)


def radial(size, cx, cy, radius, falloff=2.0):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / radius
    f = np.clip(1.0 - d, 0.0, 1.0) ** falloff
    return f


def render(variant="a", size=1024, body_ratio=None, mono_moon=False, optical=False):
    S = int(size)
    k = S / 1024.0
    if body_ratio is None:
        body_ratio = 0.94 if optical else 0.855
    body = 1024.0 * body_ratio
    m = (1024.0 - body) * 0.5

    # ---------------- background ----------------
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    u = xx / (S - 1)
    v = yy / (S - 1)
    bg = stops(v, [
        (0.00, (0x2A, 0x33, 0x74)),
        (0.26, (0x16, 0x1B, 0x3E)),
        (0.60, (0x08, 0x09, 0x15)),
        (1.00, (0x03, 0x04, 0x08)),
    ])
    # cool light behind the device, like moonlight on a desk
    g = radial(S, 0.50 * S, 0.30 * S, 0.58 * S, 2.1)
    bg = screen_over(bg, rgb(GLOW), g * 0.62)
    # top-left sheen
    sh = radial(S, 0.22 * S, 0.02 * S, 0.85 * S, 2.8)
    bg = screen_over(bg, np.array([0.55, 0.60, 1.0], dtype=np.float32), sh * 0.10)
    # gentle corner vignette
    vig = radial(S, 0.5 * S, 0.5 * S, 0.95 * S, 1.6)
    bg = bg * (0.90 + 0.10 * vig[..., None])

    # ---------------- squircle mask ----------------
    mask = superellipse(S, 512 * k, 512 * k, body * 0.5 * k, body * 0.5 * k,
                        n=5.0, aa=1.3 * k)
    out = bg * mask[..., None]

    # inner rim light (top brighter than bottom), drawn after the glyph
    rim = mask - blur((mask > 0.5).astype(np.float32), 1.6 * k)
    rim = np.clip(rim, 0, 1) * mask

    def draw_laptop(dark_screen=True):
        nonlocal out
        # geometry (1024 design units)
        if optical:
            sx0, sy0, sx1, sy1 = 208.0, 260.0, 816.0, 690.0
            fr, chin = 36.0, 48.0
            ix0, iy0, ix1, iy1 = sx0 + fr, sy0 + 24.0, sx1 - fr, sy1 - chin
            bx0, by0, bx1, by1 = 172.0, 684.0, 852.0, 726.0
            base_pts = [(196, 684), (824, 684), (852, 726), (172, 726)]
            br = 16.0
        else:
            sx0, sy0, sx1, sy1 = 228.0, 274.0, 796.0, 668.0
            fr, chin = 25.0, 34.0
            ix0, iy0, ix1, iy1 = sx0 + fr, sy0 + 23.0, sx1 - fr, sy1 - chin
            bx0, by0, bx1, by1 = 192.0, 664.0, 832.0, 700.0
            base_pts = [(214, 664), (810, 664), (832, 700), (192, 700)]
            br = 14.0

        # glyph alpha for the soft shadow
        frame = rounded_rect(S, sx0 * k, sy0 * k, sx1 * k, sy1 * k, 46 * k, 1.2 * k)
        base = rounded_polygon(S, [(px * k, py * k) for px, py in base_pts],
                               br * k, 1.2 * k)
        if not optical:
            # thumb scoop on the front lip: the small detail that says "laptop"
            notch = disc(S, 512 * k, by1 * k, 15 * k, 1.0 * k)
            base = np.clip(base - notch, 0, 1)
        glyph = np.clip(frame + base, 0, 1)

        shadow = blur(glyph, 30 * k)
        shadow = np.roll(shadow, int(22 * k), axis=0)
        out = out * (1.0 - 0.38 * shadow[..., None])

        # frame: vertical white gradient, warmer at the top
        fgrad = np.clip((yy / (S - 1) - (sy0 / 1024.0)) / ((sy1 - sy0) / 1024.0), 0, 1)
        fcol = stops(fgrad, [(0.0, FRAME_TOP), (0.55, (0xF2, 0xF4, 0xFD)), (1.0, FRAME_BOT)])
        out = over(out, fcol, frame)

        # screen: near-black with a faint blue floor
        sgrad = np.clip((yy / (S - 1) - (iy0 / 1024.0)) / ((iy1 - iy0) / 1024.0), 0, 1)
        scol = stops(sgrad, [(0.0, SCREEN_TOP), (0.55, (0x08, 0x0A, 0x17)), (1.0, SCREEN_BOT)])
        screen = rounded_rect(S, ix0 * k, iy0 * k, ix1 * k, iy1 * k, 27 * k, 1.2 * k)
        out = over(out, scol, screen)

        # subtle inner shadow just under the top bezel
        top_shadow = np.clip((yy - iy0 * k) / (20 * k), 0, 1)
        top_shadow = (1.0 - top_shadow) * screen
        out = out * (1.0 - 0.20 * top_shadow[..., None])

        # recessed glass: a soft inner vignette around the screen
        edge_d = np.minimum(np.minimum(xx - ix0 * k, ix1 * k - xx),
                            np.minimum(yy - iy0 * k, iy1 * k - yy))
        inner = np.clip(1.0 - edge_d / (14.0 * k), 0.0, 1.0) * screen
        out = out * (1.0 - 0.16 * inner[..., None])

        # a whisper of ambient reflection across the glass
        t = (xx / S) * 0.80 + (yy / S) * 0.30
        sheen = np.clip((0.62 - t) / 0.34, 0.0, 1.0) ** 2 * screen
        out = screen_over(out, np.ones(3, dtype=np.float32), sheen * 0.022)

        # camera dot in the top bezel: one small, honest laptop cue
        if not optical:
            cam = disc(S, 512 * k, (sy0 + 11.5) * k, 5.2 * k, 1.0 * k)
            out = over(out, np.array([0.10, 0.11, 0.16], dtype=np.float32), cam * 0.9)

        # moon: warm crescent, lit from the lower right
        if optical:
            mx, my, mr = 684.0, 402.0, 84.0
        elif variant == "a":
            mx, my, mr = 678.0, 408.0, 66.0
        else:
            mx, my, mr = 678.0, 402.0, 60.0
        if mono_moon:
            pass
        # no halo: the unlit side of the moon is just the dark screen
        moon = disc(S, mx * k, my * k, mr * k, 1.2 * k)
        bite = disc(S, (mx - 0.20 * mr) * k, (my - 0.17 * mr) * k, 0.92 * mr * k, 1.2 * k)
        crescent = np.clip(moon - bite, 0, 1)
        out = over(out, rgb(MOON), crescent)

        # base: same white material, with a dark seam where it meets the lid
        bgrad = np.clip((yy / (S - 1) - by0 / 1024.0) / ((by1 - by0) / 1024.0), 0, 1)
        bcol = stops(bgrad, [(0.0, (0xEF, 0xF1, 0xFB)), (1.0, (0xC9, 0xCE, 0xE2))])
        out = over(out, bcol, base)
        seam = rounded_rect(S, (bx0 + 8) * k, (by0 - 6) * k, (bx1 - 8) * k, (by0 + 1) * k,
                            3 * k, 1.0 * k)
        out = over(out, np.array([0.02, 0.02, 0.04], dtype=np.float32), seam * 0.45)

    def draw_lid():
        nonlocal out
        # closed laptop, moon rising behind the lid: fewer parts, more iconic
        lx0, ly0, lx1, ly1 = 246.0, 448.0, 778.0, 678.0
        bx0, by0, bx1, by1 = 212.0, 684.0, 812.0, 718.0
        lid = rounded_rect(S, lx0 * k, ly0 * k, lx1 * k, ly1 * k, 46 * k, 1.2 * k)
        base = rounded_rect(S, bx0 * k, by0 * k, bx1 * k, by1 * k, 17 * k, 1.2 * k)
        glyph = np.clip(lid + base, 0, 1)
        shadow = blur(glyph, 30 * k)
        shadow = np.roll(shadow, int(22 * k), axis=0)
        out = out * (1.0 - 0.55 * shadow[..., None])

        # moon (upper right), partly cropped by the lid
        mx, my, mr = 654.0, 400.0, 88.0
        glow = radial(S, mx * k, my * k, mr * 3.0 * k, 2.0)
        out = screen_over(out, rgb(MOON_HOT), glow * 0.22)
        moon = disc(S, mx * k, my * k, mr * k, 1.2 * k)
        bite = disc(S, (mx - 0.20 * mr) * k, (my - 0.17 * mr) * k, 0.92 * mr * k, 1.2 * k)
        crescent = np.clip(moon - bite, 0, 1)
        # clip the moon to above the lid so it looks like it rises behind it
        above = 1.0 - rounded_rect(S, lx0 * k, ly0 * k, lx1 * k, ly1 * k, 46 * k, 1.2 * k)
        out = over(out, rgb(MOON), crescent * above)

        lgrad = np.clip((yy / (S - 1) - ly0 / 1024.0) / ((ly1 - ly0) / 1024.0), 0, 1)
        lcol = stops(lgrad, [(0.0, (0xFF, 0xFF, 0xFF)), (1.0, (0xD6, 0xDB, 0xEE))])
        out = over(out, lcol, lid)
        # seam under the lid
        seam = rounded_rect(S, (lx0 + 14) * k, (ly1 - 3) * k, (lx1 - 14) * k, (ly1 + 7) * k,
                            5 * k, 1.0 * k)
        out = over(out, np.array([0.8, 0.84, 0.95], dtype=np.float32), seam * 0.45)
        bgrad = np.clip((yy / (S - 1) - by0 / 1024.0) / ((by1 - by0) / 1024.0), 0, 1)
        bcol = stops(bgrad, [(0.0, (0xEA, 0xED, 0xFA)), (1.0, (0xBF, 0xC5, 0xDC))])
        out = over(out, bcol, base)

    if variant == "lid":
        draw_lid()
    else:
        draw_laptop()

    # rim light on top of everything
    ry = np.clip(1.0 - yy / (S - 1) * 1.7, 0, 1)
    rim_alpha = rim * (0.14 + 0.16 * ry)
    out = screen_over(out, np.ones(3, dtype=np.float32), rim_alpha * 0.9)

    # final: antialiased alpha, transparent outside the squircle
    rgba = np.concatenate([out, mask[..., None]], axis=2)
    img = Image.fromarray(np.clip(rgba * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA")
    if S > 1024:
        pass
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--variant", default="a")
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--ss", type=int, default=3)
    ap.add_argument("--out", required=True)
    ap.add_argument("--mono", action="store_true")
    ap.add_argument("--optical", action="store_true")
    args = ap.parse_args()

    optical = args.optical or args.size <= 48
    big = render(args.variant, args.size * args.ss, mono_moon=args.mono, optical=optical)
    small = big.resize((args.size, args.size), Image.LANCZOS)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    small.save(args.out, "PNG")
    print("wrote", args.out, small.size)


if __name__ == "__main__":
    main()


