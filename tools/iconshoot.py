# -*- coding: utf-8 -*-
"""启动 QuickBoard，截默认尺寸与放大后两张图，验证 256 图标在 Viewbox 缩放下依然清晰。
启动/截图/关闭都在本脚本内，避免沙箱在工具调用间隙回收子进程。
"""
import ctypes
import ctypes.wintypes as wt
import os
import struct
import subprocess
import sys
import time
import zlib

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "src", "QuickBoard", "bin", "Release", "net8.0-windows", "QuickBoard.exe")
OUTDIR = os.path.join(ROOT, "screenshots")


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
        ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
        ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
        ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD),
    ]


def grab(hwnd, out_png):
    rect = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    w, h = rect.right - rect.left, rect.bottom - rect.top
    src = user32.GetDC(None)
    mem = gdi32.CreateCompatibleDC(src)
    bmp = gdi32.CreateCompatibleBitmap(src, w, h)
    gdi32.SelectObject(mem, bmp)
    gdi32.BitBlt(mem, 0, 0, w, h, src, rect.left, rect.top, 0x00CC0020)
    bi = BITMAPINFOHEADER()
    bi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bi.biWidth = w
    bi.biHeight = -h
    bi.biPlanes = 1
    bi.biBitCount = 32
    bi.biCompression = 0
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bi), 0)
    raw = buf.raw
    rows = []
    for y in range(h):
        row = bytearray(b"\x00")
        base = y * w * 4
        for x in range(w):
            i = base + x * 4
            b, g, r, a = raw[i], raw[i + 1], raw[i + 2], raw[i + 3]
            row += bytes((r, g, b, a))
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
    return w, h


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
    # 等图标异步加载完成 + 窗口位置稳定
    time.sleep(2.5)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.5)
    grab(hwnd, os.path.join(OUTDIR, "图标高清_默认尺寸.png"))

    # 放大到 1600x1000（强制居中开启时 WndProc 会自动回中，尺寸保留）
    user32.SetWindowPos(hwnd, 0, 0, 0, 1600, 1000, 0x0004 | 0x0010)
    time.sleep(1.5)
    grab(hwnd, os.path.join(OUTDIR, "图标高清_放大尺寸.png"))
finally:
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except Exception:
        proc.kill()
    print("process terminated")
