# -*- coding: utf-8 -*-
"""启动 QuickBoard 并直接打开设置页截图，验证主题/尺寸下拉框在真实窗口里的渲染。"""
import ctypes
import ctypes.wintypes as wt
import os
import struct
import subprocess
import time
import zlib

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "src", "QuickBoard", "bin", "Release", "net8.0-windows", "QuickBoard.exe")
OUT = os.path.join(ROOT, "screenshots", "设置页_主题与诊断.png")


class BIH(ctypes.Structure):
    _fields_ = [
        ("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
        ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
        ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
        ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD),
    ]


def grab(hwnd, out_png):
    r = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    src = user32.GetDC(None)
    mem = gdi32.CreateCompatibleDC(src)
    bmp = gdi32.CreateCompatibleBitmap(src, w, h)
    gdi32.SelectObject(mem, bmp)
    gdi32.BitBlt(mem, 0, 0, w, h, src, r.left, r.top, 0x00CC0020)
    bi = BIH()
    bi.biSize = ctypes.sizeof(BIH); bi.biWidth = w; bi.biHeight = -h
    bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bi), 0)
    raw = buf.raw
    rows = []
    for y in range(h):
        row = bytearray(b"\x00")
        base = y * w * 4
        for x in range(w):
            i = base + x * 4
            row += bytes((raw[i + 2], raw[i + 1], raw[i], raw[i + 3]))
        rows.append(bytes(row))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(b"".join(rows), 6))
           + chunk(b"IEND", b""))
    open(out_png, "wb").write(png)
    gdi32.DeleteObject(bmp); gdi32.DeleteDC(mem); user32.ReleaseDC(None, src)
    print(f"saved {out_png} {w}x{h}")


WNDENUMPROC = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)


def find_settings():
    found = []

    def cb(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        if n == 0:
            return True
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        title = buf.value
        r = wt.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        w, h = r.right - r.left, r.bottom - r.top
        print(f"  window: {title!r} {w}x{h} hwnd={hwnd}")
        if title == "设置" and 400 < w < 800 and 500 < h < 1200:
            found.append(hwnd)
        return True

    user32.EnumWindows(WNDENUMPROC(cb), 0)
    return found[0] if found else 0


proc = subprocess.Popen([EXE, "--opensettings"], cwd=os.path.dirname(EXE))
try:
    target = 0
    for _ in range(60):
        target = find_settings()
        if target:
            break
        time.sleep(0.25)
    if not target:
        print("settings window not found"); raise SystemExit(2)
    time.sleep(1.0)
    user32.SetForegroundWindow(target)
    time.sleep(0.4)
    grab(target, OUT)
finally:
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except Exception:
        proc.kill()
    print("process terminated")
