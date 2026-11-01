# -*- coding: utf-8 -*-
"""Generate a scalable SVG version of the Quiet icon."""

import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out", "icon.svg")


def squircle(cx, cy, hw, hh, n=5.0, steps=720):
    pts = []
    for i in range(steps):
        t = 2.0 * math.pi * i / steps
        c, s = math.cos(t), math.sin(t)
        x = cx + math.copysign(abs(c) ** (2.0 / n), c) * hw
        y = cy + math.copysign(abs(s) ** (2.0 / n), s) * hh
        pts.append((x, y))
    return "M " + " L ".join(f"{x:.2f} {y:.2f}" for x, y in pts) + " Z"


def rounded_polygon(pts, r):
    out = []
    for i, p in enumerate(pts):
        prev = pts[i - 1]
        nxt = pts[(i + 1) % len(pts)]
        v1 = (prev[0] - p[0], prev[1] - p[1])
        v2 = (nxt[0] - p[0], nxt[1] - p[1])
        l1 = math.hypot(*v1)
        l2 = math.hypot(*v2)
        a = (p[0] + v1[0] / l1 * r, p[1] + v1[1] / l1 * r)
        b = (p[0] + v2[0] / l2 * r, p[1] + v2[1] / l2 * r)
        if i == 0:
            out.append(f"M {a[0]:.2f} {a[1]:.2f}")
        else:
            out.append(f"L {a[0]:.2f} {a[1]:.2f}")
        out.append(f"A {r} {r} 0 0 1 {b[0]:.2f} {b[1]:.2f}")
    out.append("Z")
    return " ".join(out)


def main():
    body = 1024 * 0.855
    half = body / 2
    sq = squircle(512, 512, half, half, 5.0)
    base = rounded_polygon([(214, 664), (810, 664), (832, 700), (192, 700)], 14)

    svg = f'''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024"
     viewBox="0 0 1024 1024">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#2A3374"/>
      <stop offset="0.26" stop-color="#161B3E"/>
      <stop offset="0.60" stop-color="#080915"/>
      <stop offset="1" stop-color="#030408"/>
    </linearGradient>
    <radialGradient id="glow" cx="50%" cy="30%" r="58%">
      <stop offset="0" stop-color="#4C58DC" stop-opacity="0.62"/>
      <stop offset="1" stop-color="#4C58DC" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="sheen" cx="22%" cy="2%" r="85%">
      <stop offset="0" stop-color="#8C99FF" stop-opacity="0.10"/>
      <stop offset="1" stop-color="#8C99FF" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="vig" cx="50%" cy="50%" r="70%">
      <stop offset="0.55" stop-color="#000000" stop-opacity="0"/>
      <stop offset="1" stop-color="#000000" stop-opacity="0.16"/>
    </radialGradient>
    <linearGradient id="frame" x1="0" y1="274" x2="0" y2="668"
                    gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#FFFFFF"/>
      <stop offset="0.55" stop-color="#F2F4FD"/>
      <stop offset="1" stop-color="#DCE0F2"/>
    </linearGradient>
    <linearGradient id="screen" x1="0" y1="297" x2="0" y2="634"
                    gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#070913"/>
      <stop offset="0.55" stop-color="#080A17"/>
      <stop offset="1" stop-color="#0A0C1D"/>
    </linearGradient>
    <linearGradient id="base" x1="0" y1="664" x2="0" y2="700"
                    gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#EFF1FB"/>
      <stop offset="1" stop-color="#C9CEE2"/>
    </linearGradient>
    <linearGradient id="rim" x1="0" y1="80" x2="0" y2="950"
                    gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.34"/>
      <stop offset="0.55" stop-color="#FFFFFF" stop-opacity="0.12"/>
      <stop offset="1" stop-color="#FFFFFF" stop-opacity="0.02"/>
    </linearGradient>
    <clipPath id="clip">
      <path d="{sq}"/>
    </clipPath>
    <clipPath id="screenClip">
      <rect x="253" y="297" width="518" height="337" rx="27"/>
    </clipPath>
    <mask id="moonCut">
      <rect width="1024" height="1024" fill="black"/>
      <circle cx="678" cy="408" r="66" fill="white"/>
      <circle cx="664.8" cy="396.78" r="60.72" fill="black"/>
    </mask>
    <mask id="baseCut">
      <rect width="1024" height="1024" fill="black"/>
      <path d="{base}" fill="white"/>
      <circle cx="512" cy="700" r="15" fill="black"/>
    </mask>
    <filter id="drop" x="-25%" y="-25%" width="150%" height="155%">
      <feDropShadow dx="0" dy="22" stdDeviation="26"
                    flood-color="#000000" flood-opacity="0.38"/>
    </filter>
    <filter id="soft" x="-20%" y="-20%" width="140%" height="140%">
      <feGaussianBlur stdDeviation="7"/>
    </filter>
  </defs>

  <g clip-path="url(#clip)">
    <rect width="1024" height="1024" fill="url(#bg)"/>
    <rect width="1024" height="1024" fill="url(#glow)" style="mix-blend-mode:screen"/>
    <rect width="1024" height="1024" fill="url(#sheen)" style="mix-blend-mode:screen"/>
    <rect width="1024" height="1024" fill="url(#vig)"/>

    <g filter="url(#drop)">
      <rect x="228" y="274" width="568" height="394" rx="46" fill="url(#frame)"/>
      <path d="{base}" fill="url(#base)" mask="url(#baseCut)"/>
    </g>

    <rect x="253" y="297" width="518" height="337" rx="27" fill="url(#screen)"/>
    <rect x="253" y="297" width="518" height="337" rx="27" fill="none"
          stroke="#000000" stroke-opacity="0.20" stroke-width="14"
          filter="url(#soft)" clip-path="url(#screenClip)"/>
    <circle cx="512" cy="285.5" r="5.2" fill="#1A1C29"/>

    <circle cx="678" cy="408" r="66" fill="#FFE2A8" mask="url(#moonCut)"/>

    <rect x="200" y="659" width="624" height="7" rx="3.5"
          fill="#05060A" opacity="0.45"/>

    <path d="{sq}" fill="none" stroke="url(#rim)" stroke-width="2.2"/>
  </g>
</svg>
'''
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(svg)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
