import struct
import sys
import zlib


def load_bmp(path):
    data = open(path, "rb").read()
    offset = struct.unpack_from("<I", data, 10)[0]
    width = struct.unpack_from("<i", data, 18)[0]
    height = struct.unpack_from("<i", data, 22)[0]
    top_down = height < 0
    height = abs(height)
    stride = width * 4

    pixels = []
    for y in range(height):
        src_y = y if top_down else height - 1 - y
        start = offset + src_y * stride
        row = data[start:start + stride]
        pixels.append([tuple(row[x * 4:x * 4 + 3]) for x in range(width)])
    return width, height, pixels


def png(width, height, rows):
    raw = b"".join(b"\x00" + bytes(v for px in row for v in (px[2], px[1], px[0], 255)) for row in rows)

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    out = b"\x89PNG\r\n\x1a\n"
    out += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    out += chunk(b"IDAT", zlib.compress(raw, 6))
    out += chunk(b"IEND", b"")
    return out


if __name__ == "__main__":
    src, dst = sys.argv[1], sys.argv[2]
    x, y, w, h = (int(v) for v in sys.argv[3:7])
    scale = int(sys.argv[7]) if len(sys.argv) > 7 else 2

    sw, sh, pixels = load_bmp(src)
    x = max(0, min(x, sw - 1))
    y = max(0, min(y, sh - 1))
    w = min(w, sw - x)
    h = min(h, sh - y)

    rows = []
    for j in range(h * scale):
        sy = y + j // scale
        rows.append([pixels[sy][x + i // scale] for i in range(w * scale)])

    open(dst, "wb").write(png(w * scale, h * scale, rows))
    print(f"{src} [{x},{y} {w}x{h}] x{scale} -> {dst}")
