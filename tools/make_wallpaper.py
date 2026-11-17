import struct
import sys
import zlib

W, H = 1600, 900


def chunk(tag, payload):
    return (struct.pack(">I", len(payload)) + tag + payload
            + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))


rows = []
for y in range(H):
    row = bytearray(b"\x00")
    ty = y / H
    for x in range(W):
        tx = x / W
        # 斜向渐变 + 两团柔和高光，便于肉眼确认背景图是否真的铺上了
        r = int(30 + 90 * tx * (0.3 + 0.7 * ty))
        g = int(20 + 60 * (1 - tx) * (0.2 + 0.8 * ty))
        b = int(60 + 120 * (1 - abs(tx - 0.72) * 1.4) * (1 - abs(ty - 0.3) * 1.2))
        d1 = ((x - 320) ** 2 + (y - 640) ** 2) ** 0.5
        d2 = ((x - 1250) ** 2 + (y - 220) ** 2) ** 0.5
        for d, boost in ((d1, (255, 90, 120)), (d2, (90, 190, 255))):
            k = max(0.0, 1 - d / 380.0) ** 2 * 0.55
            r = min(255, int(r + boost[0] * k))
            g = min(255, int(g + boost[1] * k))
            b = min(255, int(b + boost[2] * k))
        row += bytes((r, g, b))
    rows.append(bytes(row))

raw = b"".join(rows)
png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(raw, 6))
png += chunk(b"IEND", b"")

out = sys.argv[1] if len(sys.argv) > 1 else "wallpaper.png"
open(out, "wb").write(png)
print(f"{out} {W}x{H}")
