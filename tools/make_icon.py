"""生成 QuickBoard 图标：3×3 键帽网格，中心键用强调色。

纯标准库（zlib/struct/math），不依赖 Pillow 或任何图像库，
按超采样逐像素算有符号距离，输入输出都可复现。

用法：
    python tools/make_icon.py

产物：
    src/QuickBoard/Assets/QuickBoard.ico   16/24/32/48/64/128/256 多尺寸（小尺寸 BMP 帧，大尺寸 PNG 帧）
    assets/icon-256.png                    README 展示
    assets/icon-1024.png                   GitHub 仓库头像
"""

import math
import os
import struct
import zlib

# 与 Theme.xaml 保持一致：TileNameTextBrush / AccentBrush / 深色面板
BG = (0x20, 0x24, 0x2A)        # #20242A
KEY = (0xF1, 0xF3, 0xF6)       # #F1F3F6
ACCENT = (0x5B, 0x8D, 0xEF)    # #5B8DEF

DESIGN = 256.0                 # 设计画布边长
BASE_INSET = 6.0               # 底块四周留白
BASE_RADIUS = 58.0             # 底块圆角
GRID_TOTAL = 176.0             # 九宫格总边长
HOT = (1, 1)                   # 高亮的键（列, 行）

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def sdf_round_rect(px, py, cx, cy, hw, hh, r):
    """圆角矩形的有符号距离，< 0 表示在内部。"""
    qx = abs(px - cx) - (hw - r)
    qy = abs(py - cy) - (hh - r)
    ax = qx if qx > 0.0 else 0.0
    ay = qy if qy > 0.0 else 0.0
    m = qx if qx > qy else qy
    return math.sqrt(ax * ax + ay * ay) + (m if m < 0.0 else 0.0) - r


def geometry(size):
    """小尺寸放大间隙，避免 16px 下九宫格糊成一团。"""
    gap = 16.0 if size > 24 else 26.0
    cell = (GRID_TOTAL - 2.0 * gap) / 3.0
    origin = (DESIGN - GRID_TOTAL) / 2.0
    return gap, cell, origin


def render(size):
    """渲染为 RGBA 字节串（非预乘 alpha）。"""
    ss = 8 if size <= 128 else (4 if size <= 512 else 2)
    gap, cell, origin = geometry(size)
    pitch = cell + gap
    scale = DESIGN / size
    base_c = DESIGN / 2.0
    base_h = DESIGN / 2.0 - BASE_INSET
    cell_r = 12.0 * (cell / 48.0)
    cell_h = cell / 2.0
    samples = float(ss * ss)
    out = bytearray(size * size * 4)

    for py in range(size):
        for px in range(size):
            r = g = b = a = 0.0
            for sy in range(ss):
                y = (py + (sy + 0.5) / ss) * scale
                for sx in range(ss):
                    x = (px + (sx + 0.5) / ss) * scale
                    if sdf_round_rect(x, y, base_c, base_c, base_h, base_h, BASE_RADIUS) >= 0.0:
                        continue
                    cr, cg, cb = BG
                    lx = x - origin
                    ly = y - origin
                    if 0.0 <= lx < GRID_TOTAL and 0.0 <= ly < GRID_TOTAL:
                        col = int(lx / pitch)
                        row = int(ly / pitch)
                        ox = lx - col * pitch
                        oy = ly - row * pitch
                        if ox < cell and oy < cell:
                            if sdf_round_rect(ox, oy, cell_h, cell_h, cell_h, cell_h, cell_r) < 0.0:
                                cr, cg, cb = ACCENT if (col, row) == HOT else KEY
                    r += cr
                    g += cg
                    b += cb
                    a += 255.0
            if a > 0.0:
                i = (py * size + px) * 4
                k = 255.0 / a          # a 是「命中子样本数 × 255」，除以它才是颜色均值
                out[i] = min(255, int(r * k + 0.5))
                out[i + 1] = min(255, int(g * k + 0.5))
                out[i + 2] = min(255, int(b * k + 0.5))
                out[i + 3] = min(255, int(a / samples + 0.5))
    return bytes(out)


def encode_png(rgba, size):
    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    stride = size * 4
    raw = b''.join(b'\x00' + rgba[y * stride:(y + 1) * stride] for y in range(size))
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def encode_bmp_frame(rgba, size):
    """ICO 里的 DIB 帧：BITMAPINFOHEADER + 自下而上的 BGRA + 1bpp AND 掩码。"""
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = []
    for y in range(size - 1, -1, -1):
        base = y * size * 4
        row = bytearray(size * 4)
        for x in range(size):
            i = base + x * 4
            row[x * 4] = rgba[i + 2]
            row[x * 4 + 1] = rgba[i + 1]
            row[x * 4 + 2] = rgba[i]
            row[x * 4 + 3] = rgba[i + 3]
        rows.append(bytes(row))
    mask_row = ((size + 31) // 32) * 4
    return header + b''.join(rows) + b'\x00' * (mask_row * size)


def encode_ico(frames):
    count = len(frames)
    header = struct.pack('<HHH', 0, 1, count)
    offset = 6 + 16 * count
    directory = bytearray()
    body = bytearray()
    for size, data in frames:
        dim = 0 if size >= 256 else size
        directory += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(data), offset)
        body += data
        offset += len(data)
    return header + bytes(directory) + bytes(body)


def main():
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = []
    for size in ico_sizes:
        rgba = render(size)
        frames.append((size, encode_png(rgba, size) if size >= 128 else encode_bmp_frame(rgba, size)))
        print(f'  渲染 {size}x{size}')

    ico_dir = os.path.join(ROOT, 'src', 'QuickBoard', 'Assets')
    png_dir = os.path.join(ROOT, 'assets')
    os.makedirs(ico_dir, exist_ok=True)
    os.makedirs(png_dir, exist_ok=True)

    ico_path = os.path.join(ico_dir, 'QuickBoard.ico')
    with open(ico_path, 'wb') as f:
        f.write(encode_ico(frames))
    print(f'写出 {ico_path} ({os.path.getsize(ico_path)} 字节, {len(frames)} 帧)')

    for size in (256, 1024):
        rgba = render(size)
        path = os.path.join(png_dir, f'icon-{size}.png')
        with open(path, 'wb') as f:
            f.write(encode_png(rgba, size))
        print(f'写出 {path} ({os.path.getsize(path)} 字节)')


if __name__ == '__main__':
    main()
