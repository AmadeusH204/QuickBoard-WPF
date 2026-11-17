"""诊断：对比 ExtractIconEx(0) 与 PrivateExtractIcons(0,256) 的图标尺寸与身份。
纯读取，不写配置、不动原程序。用于确定高分辨率图标提取方案是否可行。
"""
import ctypes
import os
import struct
import zlib
from ctypes import wintypes

user32 = ctypes.WinDLL("user32", use_last_error=True)
shell32 = ctypes.WinDLL("shell32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)


class ICONINFO(ctypes.Structure):
    _fields_ = [
        ("fIcon", wintypes.BOOL),
        ("xHotspot", wintypes.DWORD),
        ("yHotspot", wintypes.DWORD),
        ("hbmMask", wintypes.HBITMAP),
        ("hbmColor", wintypes.HBITMAP),
    ]


class BITMAP(ctypes.Structure):
    _fields_ = [
        ("bmType", wintypes.LONG),
        ("bmWidth", wintypes.LONG),
        ("bmHeight", wintypes.LONG),
        ("bmWidthBytes", wintypes.LONG),
        ("bmPlanes", wintypes.WORD),
        ("bmBitsPixel", wintypes.WORD),
        ("bmBits", ctypes.c_void_p),
    ]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD),
        ("biWidth", wintypes.LONG),
        ("biHeight", wintypes.LONG),
        ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG),
        ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


def icon_size(hicon):
    info = ICONINFO()
    if not user32.GetIconInfo(wintypes.HICON(hicon), ctypes.byref(info)):
        return None
    try:
        hbm = info.hbmColor if info.hbmColor else info.hbmMask
        bmp = BITMAP()
        if not gdi32.GetObjectW(wintypes.HGDIOBJ(hbm), ctypes.sizeof(BITMAP), ctypes.byref(bmp)):
            return None
        h = bmp.bmHeight if info.hbmColor else bmp.bmHeight // 2
        return (bmp.bmWidth, h, bool(info.hbmColor))
    finally:
        if info.hbmColor:
            gdi32.DeleteObject(wintypes.HGDIOBJ(info.hbmColor))
        if info.hbmMask:
            gdi32.DeleteObject(wintypes.HGDIOBJ(info.hbmMask))


def icon_to_png(hicon, path):
    """用 GetDIBits 取 32bpp BGRA，手搓 PNG（自底向上翻转 + BGRA->RGBA）。"""
    info = ICONINFO()
    if not user32.GetIconInfo(wintypes.HICON(hicon), ctypes.byref(info)):
        return False
    try:
        if not info.hbmColor:
            return False
        bmp = BITMAP()
        gdi32.GetObjectW(wintypes.HGDIOBJ(info.hbmColor), ctypes.sizeof(BITMAP), ctypes.byref(bmp))
        w, h = bmp.bmWidth, bmp.bmHeight
        bi = BITMAPINFO()
        bi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bi.bmiHeader.biWidth = w
        bi.bmiHeader.biHeight = -h  # 自顶向下
        bi.bmiHeader.biPlanes = 1
        bi.bmiHeader.biBitCount = 32
        bi.bmiHeader.biCompression = 0  # BI_RGB
        buf = ctypes.create_string_buffer(w * h * 4)
        gdi32.GetDIBits(ctypes.c_void_p(0), wintypes.HBITMAP(info.hbmColor), 0, h,
                        buf, ctypes.byref(bi), 0)
        raw = buf.raw
        rows = []
        for y in range(h):
            row = bytearray(b"\x00")  # PNG filter type 0
            src = raw[y * w * 4:(y + 1) * w * 4]
            for x in range(w):
                b, g, r, a = src[x * 4:x * 4 + 4]
                row += bytes((r, g, b, a))
            rows.append(bytes(row))
        idat = zlib.compress(b"".join(rows), 9)

        def chunk(typ, data):
            c = struct.pack(">I", len(data)) + typ + data
            return c + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF)

        png = (b"\x89PNG\r\n\x1a\n"
               + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
               + chunk(b"IDAT", idat)
               + chunk(b"IEND", b""))
        with open(path, "wb") as f:
            f.write(png)
        return True
    finally:
        if info.hbmColor:
            gdi32.DeleteObject(wintypes.HGDIOBJ(info.hbmColor))
        if info.hbmMask:
            gdi32.DeleteObject(wintypes.HGDIOBJ(info.hbmMask))


def extract_icon_ex(path):
    large = wintypes.HICON()
    small = wintypes.HICON()
    n = shell32.ExtractIconExW(path, 0, ctypes.byref(large), ctypes.byref(small), 1)
    out = []
    if n > 0 and large.value:
        out.append(("large", icon_size(large.value)))
        user32.DestroyIcon(large)
    if small.value:
        out.append(("small", icon_size(small.value)))
        user32.DestroyIcon(small)
    return n, out


def private_extract(path, size):
    icons = (wintypes.HICON * 1)()
    ids = (wintypes.UINT * 1)()
    n = user32.PrivateExtractIconsW(path, 0, size, size, icons, ids, 1, 0)
    out = []
    if n > 0 and icons[0]:
        out.append((icon_size(icons[0]), ids[0]))
        user32.DestroyIcon(icons[0])
    return n, out


EXE_TARGETS = [
    r"D:\App\IntelliJ IDEA 2024.1\bin\idea64.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"D:\App\Microsoft VS Code\Code.exe",
    r"E:\App\PyCharm 2024.1.7\bin\pycharm64.exe",
    r"D:\QQ\QQ.exe",
    r"D:\App\FeiShu\Feishu.exe",
    r"C:\Windows\explorer.exe",
]
SPECIAL_TARGETS = [
    r"C:\Users\Public\Desktop\Microsoft Edge.lnk",
    r"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Excel.lnk",
    r"D:\App\FeiShu\Feishu.exe",  # 对照
    r"E:\App\FeiShu\icon.ico",
    r"C:\Users\16797\AppData\Roaming\KRLauncher\C10003\ico\Aki.ico",
    r"C:\Users\16797",  # 文件夹
]

print("################ 可执行文件 ################")
for t in EXE_TARGETS:
    if not os.path.exists(t):
        print(f"[MISSING] {t}")
        continue
    n1, r1 = extract_icon_ex(t)
    n2, r2 = private_extract(t, 256)
    print(f"\n=== {os.path.basename(t)} ===")
    print(f" ExtractIconEx(0):      count={n1} -> {r1}")
    print(f" PrivateExtractIcons:   count={n2} -> {r2}")

print("\n\n################ 特殊路径 ################")
for t in SPECIAL_TARGETS:
    if not os.path.exists(t):
        print(f"[MISSING] {t}")
        continue
    n2, r2 = private_extract(t, 256)
    print(f"\n=== {os.path.basename(t) or t} (dir={os.path.isdir(t)}) ===")
    print(f" PrivateExtractIcons:   count={n2} -> {r2}")

# 导出 IDEA 的 32 与 256 对照图
outdir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon_probe_out")
os.makedirs(outdir, exist_ok=True)
idea = r"D:\App\IntelliJ IDEA 2024.1\bin\idea64.exe"
if os.path.exists(idea):
    large = wintypes.HICON()
    shell32.ExtractIconExW(idea, 0, ctypes.byref(large), ctypes.byref(wintypes.HICON()), 1)
    if large.value:
        icon_to_png(large.value, os.path.join(outdir, "IDEA_extract32.png"))
        user32.DestroyIcon(large)
    icons = (wintypes.HICON * 1)()
    user32.PrivateExtractIconsW(idea, 0, 256, 256, icons, (wintypes.UINT * 1)(), 1, 0)
    if icons[0]:
        icon_to_png(icons[0], os.path.join(outdir, "IDEA_private256.png"))
        user32.DestroyIcon(icons[0])
    print(f"\n导出对照图到: {outdir}")
