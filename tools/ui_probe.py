import ctypes
import ctypes.wintypes as wt
import sys
import time

user32 = ctypes.WinDLL("user32", use_last_error=True)

WM_MOUSEWHEEL = 0x020A
WM_KEYDOWN = 0x0100
WM_KEYUP = 0x0101


def hwnd_of(title):
    hwnd = user32.FindWindowW(None, title)
    if not hwnd:
        raise SystemExit("window not found: " + title)
    return hwnd


def wheel(hwnd, delta, times=1):
    rect = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    x = (rect.left + rect.right) // 2
    y = (rect.top + rect.bottom) // 2
    for _ in range(times):
        wparam = (delta & 0xFFFF) << 16
        lparam = (y << 16) | (x & 0xFFFF)
        user32.PostMessageW(hwnd, WM_MOUSEWHEEL, wparam, lparam)
        time.sleep(0.25)
    print(f"wheel delta={delta} times={times} at {x},{y}")
    time.sleep(0.4)


def key(hwnd, vk):
    user32.PostMessageW(hwnd, WM_KEYDOWN, vk, 0)
    time.sleep(0.05)
    user32.PostMessageW(hwnd, WM_KEYUP, vk, 0)
    time.sleep(0.3)


def resize(hwnd, w, h):
    user32.SetWindowPos(hwnd, 0, 0, 0, w, h, 0x0002 | 0x0004 | 0x0010)
    time.sleep(0.6)
    rect = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    print(f"resize -> {rect.right - rect.left}x{rect.bottom - rect.top}")


if __name__ == "__main__":
    hwnd = hwnd_of(sys.argv[1] if len(sys.argv) > 1 else "QuickBoard")
    action = sys.argv[2] if len(sys.argv) > 2 else "info"
    if action == "wheel":
        wheel(hwnd, int(sys.argv[3]) if len(sys.argv) > 3 else -120,
              int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif action == "key":
        key(hwnd, int(sys.argv[3], 0))
    elif action == "resize":
        resize(hwnd, int(sys.argv[3]), int(sys.argv[4]))
    else:
        rect = wt.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        print(f"hwnd={hwnd} rect={rect.left},{rect.top} {rect.right - rect.left}x{rect.bottom - rect.top}")
