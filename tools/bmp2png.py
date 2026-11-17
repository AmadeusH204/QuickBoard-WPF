# -*- coding: utf-8 -*-
"""24bpp BMP -> PNG（纯标准库）。用法: python bmp2png.py in.bmp out.png"""
import struct
import sys
import zlib


def bmp_to_png(src, dst):
    data = open(src, "rb").read()
    pixel_offset = struct.unpack_from("<I", data, 10)[0]
    width = struct.unpack_from("<i", data, 18)[0]
    height = struct.unpack_from("<i", data, 22)[0]
    bpp = struct.unpack_from("<H", data, 28)[0]
    if bpp != 24:
        raise SystemExit(f"unsupported bpp {bpp}")

    top_down = height < 0
    height = abs(height)
    stride = (len(data) - pixel_offset) // height if height else 0
    if stride < width * 3:
        stride = (width * 3 + 3) // 4 * 4

    rows = []
    for y in range(height):
        src_y = y if top_down else height - 1 - y
        start = pixel_offset + src_y * stride
        row = data[start:start + width * 3]
        out = bytearray(b"\x00")  # filter: none
        for x in range(width):
            b, g, r = row[x * 3:x * 3 + 3]
            out += bytes((r, g, b))
        rows.append(bytes(out))

    raw = b"".join(rows)

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        return c + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 6))
    png += chunk(b"IEND", b"")

    with open(dst, "wb") as f:
        f.write(png)
    return width, height


if __name__ == "__main__":
    print(bmp_to_png(sys.argv[1], sys.argv[2]))
