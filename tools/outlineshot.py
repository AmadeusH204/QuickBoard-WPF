# -*- coding: utf-8 -*-
"""启动 QuickBoard，截取主界面，验证磁贴名称描边效果，然后关闭。
启动/截图/关闭都在本脚本内完成，避免沙箱回收子进程。"""
import os
import struct
import subprocess
import sys
import time
import zlib

import ctypes
import ctypes.wintypes as wt

user32 = ctypes.WinDLL("user32")
gdi32 = ctypes.WinDLL("gdi32")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "src", "QuickBoard", "bin", "Release", "net8.0-windows", "QuickBoard.exe")
OUTDIR = os.path.join(ROOT, "screenshots")


class BIH(ctypes.Structure):
    _fields_ = [("biSize", wt.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
                ("biPlanes", ctypes.c_short), ("biBitCount", ctypes.c_short), ("biCompression", wt.DWORD),
                ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", ctypes.c_long), ("biYPelsPerMeter", ctypes.c_long),
                ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD)]


def grab(hwnd, out_png):
    r = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    src = user32.GetDC(None)
    mem = gdi32.CreateCompatibleDC(src)
    bmp = gdi32.CreateCompatibleBitmap(src, w, h)
    gdi32.SelectObject(mem, bmp)
    gdi32.BitBlt(mem, 0, 0, w, h, src, r.left, r.top, 0x00CC0020)
    bi = BIH(ctypes.sizeof(BIH), w, -h, 1, 32, 0, 0, 0, 0, 0, 0)
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bi), 0)
    raw = buf.raw
    rows = []
    for y in range(h):
        base = y * w * 4
        row = bytearray()
        for x in range(w):
            i = base + x * 4
            b, g_, rr = raw[i], raw[i + 1], raw[i + 2]
            row += bytes((rr, g_, b, 255))  # BitBlt 后第 4 字节未定义，强制不透明
        rows.append(bytes(row))
    idat = zlib.compress(b"".join(rows), 6)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", idat)
           + chunk(b"IEND", b""))
    with open(out_png, "wb") as f:
        f.write(png)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(None, src)
    print(f"saved {out_png} {w}x{h}")


proc = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
try:
    hwnd = 0
    for _ in range(40):
        hwnd = user32.FindWindowW(None, "QuickBoard")
        if hwnd:
            break
        time.sleep(0.25)
    if not hwnd:
        print("window not found")
        sys.exit(2)
    time.sleep(2.5)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.5)
    grab(hwnd, os.path.join(OUTDIR, "磁贴名称黑字_描边.png"))
finally:
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except Exception:
        proc.kill()
    print("process terminated")
