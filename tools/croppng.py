# -*- coding: utf-8 -*-
"""裁剪 PNG（无 PIL 依赖）：读取 -> 解过滤 -> 裁剪 -> 重存。"""
import struct
import sys
import zlib


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos = 8
    w = h = None
    idat = b""
    while pos < len(data):
        ln = struct.unpack_from(">I", data, pos)[0]
        typ = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + ln]
        if typ == b"IHDR":
            w, h, bit, color, comp, filt, inter = struct.unpack(">IIBBBBB", body)
        elif typ == b"IDAT":
            idat += body
        pos += 12 + ln
    raw = zlib.decompress(idat)
    stride = w * 4
    out = bytearray()
    prev = bytearray(stride)
    p = 0
    for _ in range(h):
        f = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        if f == 1:
            for i in range(4, stride):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif f == 3:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif f == 4:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                b = prev[i]
                c = prev[i - 4] if i >= 4 else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out += line
        prev = line
    return w, h, bytes(out)


def write_png(path, w, h, pixels):
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    stride = w * 4
    raw = b"".join(b"\x00" + pixels[y * stride:(y + 1) * stride] for y in range(h))
    idat = zlib.compress(raw, 6)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", idat) + chunk(b"IEND", b""))


def crop(w, h, px, x0, y0, cw, ch):
    rows = []
    stride = w * 4
    for y in range(y0, min(y0 + ch, h)):
        rows.append(px[y * stride + x0 * 4:(y) * stride + (x0 + cw) * 4])
    return cw, min(ch, h - y0), b"".join(rows)


def scale(w, h, px, factor):
    """最近邻放大 factor 倍，便于目视检查小字号。"""
    if factor <= 1:
        return w, h, px
    stride = w * 4
    out = bytearray()
    for y in range(h):
        row = px[y * stride:(y + 1) * stride]
        wide = bytearray()
        for x in range(w):
            wide += row[x * 4:x * 4 + 4] * factor
        out += wide * factor
    return w * factor, h * factor, bytes(out)


if __name__ == "__main__":
    src, dst, x0, y0, cw, ch = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
    factor = int(sys.argv[7]) if len(sys.argv) > 7 else 1
    w, h, px = read_png(src)
    cw, ch, cpx = crop(w, h, px, x0, y0, cw, ch)
    cw, ch, cpx = scale(cw, ch, cpx, factor)
    write_png(dst, cw, ch, cpx)
    print(f"saved {dst} {cw}x{ch}")
