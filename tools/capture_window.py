import ctypes
import ctypes.wintypes as wt
import struct
import sys

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)

title = sys.argv[1] if len(sys.argv) > 1 else "QuickBoard"
out_path = sys.argv[2] if len(sys.argv) > 2 else "shot.bmp"

hwnd = user32.FindWindowW(None, title)
if not hwnd:
    print("window not found:", title)
    sys.exit(2)

rect = wt.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))
w = rect.right - rect.left
h = rect.bottom - rect.top
print(f"hwnd={hwnd} rect={rect.left},{rect.top} size={w}x{h}")

if w <= 0 or h <= 0:
    print("invalid window size")
    sys.exit(3)

hdc_window = user32.GetWindowDC(hwnd)
hdc_mem = gdi32.CreateCompatibleDC(hdc_window)
hbmp = gdi32.CreateCompatibleBitmap(hdc_window, w, h)
gdi32.SelectObject(hdc_mem, hbmp)

ok = user32.PrintWindow(hwnd, hdc_mem, 2)
print("PrintWindow:", ok)

if not ok:
    gdi32.BitBlt(hdc_mem, 0, 0, w, h, hdc_window, 0, 0, 0x00CC0020)

buf = ctypes.create_string_buffer(w * h * 4)


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
        ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
        ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
        ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD),
    ]


bi = BITMAPINFOHEADER()
bi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
bi.biWidth = w
bi.biHeight = -h
bi.biPlanes = 1
bi.biBitCount = 32
bi.biCompression = 0

lines = gdi32.GetDIBits(hdc_mem, hbmp, 0, h, buf, ctypes.byref(bi), 0)
print("GetDIBits lines:", lines)

header = struct.pack("<2sIHHI", b"BM", 54 + len(buf.raw), 0, 0, 54)
with open(out_path, "wb") as f:
    f.write(header)
    f.write(ctypes.string_at(ctypes.byref(bi), ctypes.sizeof(bi)))
    f.write(buf.raw)

gdi32.DeleteObject(hbmp)
gdi32.DeleteDC(hdc_mem)
user32.ReleaseDC(hwnd, hdc_window)
print("saved:", out_path)
