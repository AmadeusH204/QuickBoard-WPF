import struct
import sys
import zlib


def bmp_to_png(src, dst):
    data = open(src, "rb").read()
    pixel_offset = struct.unpack_from("<I", data, 10)[0]
    width = struct.unpack_from("<i", data, 18)[0]
    height = struct.unpack_from("<i", data, 22)[0]
    bpp = struct.unpack_from("<H", data, 28)[0]
    if bpp != 32:
        raise SystemExit(f"unsupported bpp {bpp}")

    top_down = height < 0
    height = abs(height)
    stride = width * 4

    rows = []
    for y in range(height):
        src_y = y if top_down else height - 1 - y
        start = pixel_offset + src_y * stride
        row = data[start:start + stride]
        out = bytearray()
        out.append(0)
        for x in range(width):
            b, g, r, a = row[x * 4: x * 4 + 4]
            out += bytes((r, g, b, 255))
        rows.append(bytes(out))

    raw = b"".join(rows)

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 6))
    png += chunk(b"IEND", b"")

    open(dst, "wb").write(png)
    print(f"{src} -> {dst} ({width}x{height})")


if __name__ == "__main__":
    bmp_to_png(sys.argv[1], sys.argv[2])
