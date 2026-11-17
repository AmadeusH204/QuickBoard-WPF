import ctypes
import ctypes.wintypes as wt
import struct
import sys
import time

u = ctypes.WinDLL("user32")
g = ctypes.WinDLL("gdi32")
EnumProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)


def windows_of(pid_filter=None, min_w=200):
    out = []

    def cb(h, l):
        pid = wt.DWORD()
        u.GetWindowThreadProcessId(h, ctypes.byref(pid))
        if pid_filter and pid.value != pid_filter:
            return True
        if not u.IsWindowVisible(h):
            return True
        r = wt.RECT()
        u.GetWindowRect(h, ctypes.byref(r))
        w, hh = r.right - r.left, r.bottom - r.top
        if w >= min_w:
            n = ctypes.create_unicode_buffer(260)
            u.GetWindowTextW(h, n, 260)
            out.append((h, n.value, r.left, r.top, w, hh))
        return True

    u.EnumWindows(EnumProc(cb), 0)
    return out


class BIH(ctypes.Structure):
    _fields_ = [("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
                ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
                ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
                ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD)]


def grab(hwnd, path):
    r = wt.RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    sdc = u.GetDC(None)
    mdc = g.CreateCompatibleDC(sdc)
    bmp = g.CreateCompatibleBitmap(sdc, w, h)
    g.SelectObject(mdc, bmp)
    g.BitBlt(mdc, 0, 0, w, h, sdc, r.left, r.top, 0x00CC0020)
    bi = BIH()
    bi.biSize = ctypes.sizeof(BIH)
    bi.biWidth = w
    bi.biHeight = -h
    bi.biPlanes = 1
    bi.biBitCount = 32
    buf = ctypes.create_string_buffer(w * h * 4)
    g.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0)
    open(path, "wb").write(struct.pack("<2sIHHI", b"BM", 54 + len(buf.raw), 0, 0, 54)
                           + ctypes.string_at(ctypes.byref(bi), ctypes.sizeof(bi)) + buf.raw)
    g.DeleteObject(bmp)
    g.DeleteDC(mdc)
    u.ReleaseDC(None, sdc)
    print(f"saved {path} {w}x{h}")


if __name__ == "__main__":
    title, out = sys.argv[1], sys.argv[2]
    cands = [w for w in windows_of(min_w=300) if w[1].strip() == title]
    cands.sort(key=lambda t: t[4] * t[5], reverse=True)
    if not cands:
        print("no window", title, windows_of(min_w=300))
        sys.exit(2)
    u.ShowWindow(cands[0][0], 5)
    u.SetForegroundWindow(cands[0][0])
    time.sleep(0.8)
    u.SetCursorPos(20, 20)
    time.sleep(0.8)
    grab(cands[0][0], out)
