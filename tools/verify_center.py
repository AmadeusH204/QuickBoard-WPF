import ctypes, ctypes.wintypes as wt, subprocess, time, json, os, shutil

u = ctypes.WinDLL("user32", use_last_error=True)
EnumProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)

QB = r"C:\Users\16797\WorkBuddy\2026-11-17-02-10-40\QuickBoard"
EXE = os.path.join(QB, r"src\QuickBoard\bin\Release\net8.0-windows\QuickBoard.exe")
CFG = os.path.join(os.environ["APPDATA"], "QuickBoard", "board.json")
BAK = CFG + ".center-bak"

class RECT(ctypes.Structure):
    _fields_ = [("left",ctypes.c_long),("top",ctypes.c_long),("right",ctypes.c_long),("bottom",ctypes.c_long)]
class MI(ctypes.Structure):
    _fields_ = [("cbSize",wt.DWORD),("rcMonitor",RECT),("rcWork",RECT),("dwFlags",wt.DWORD)]

def get_pid():
    out = subprocess.check_output(['tasklist','/FI','IMAGENAME eq QuickBoard.exe','/FO','CSV']).decode('gbk',errors='ignore')
    for line in out.splitlines():
        if 'QuickBoard' in line:
            return int(line.split('","')[1])
    return None

def find_window(pid):
    found = []
    def cb(h, l):
        p = wt.DWORD()
        u.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u.IsWindowVisible(h):
            buf = ctypes.create_unicode_buffer(256)
            u.GetWindowTextW(h, buf, 256)
            r = RECT(); u.GetWindowRect(h, ctypes.byref(r))
            w, hgt = r.right-r.left, r.bottom-r.top
            if w > 300 and hgt > 200:  # 主窗口
                found.append((h, r.left, r.top, r.right, r.bottom))
        return True
    u.EnumWindows(EnumProc(cb), 0)
    return found

def work_area():
    # 主屏工作区，SPI_GETWORKAREA=0x0030，pvParam 指向 RECT
    r = RECT()
    ok = u.SystemParametersInfoW(0x0030, 0, ctypes.byref(r), 0)
    return r.left, r.top, r.right, r.bottom, ok

shutil.copy2(CFG, BAK)
d = json.load(open(CFG, encoding='utf-8'))
d['Options']['CenterOnScreen'] = True
d['Options']['HasWindowPosition'] = True
d['Options']['WindowLeft'] = 80
d['Options']['WindowTop'] = 60
json.dump(d, open(CFG,'w',encoding='utf-8'), ensure_ascii=False, indent=2)

report = []
try:
    subprocess.Popen([EXE], cwd=os.path.dirname(EXE), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(4)
    pid = get_pid()
    report.append(f"pid={pid}")
    wins = find_window(pid)
    report.append(f"main_windows={wins}")
    wa = work_area()
    report.append(f"workarea={wa[0:4]} ok={wa[4]}")
    if not wins:
        report.append("FAIL: 找不到主窗口")
    else:
        h, l, t, r, b = wins[0]
        cw, ch = r-l, b-t
        exp_x = wa[0] + (wa[2]-wa[0]-cw)//2
        exp_y = wa[1] + (wa[3]-wa[1]-ch)//2
        report.append(f"窗口=({l},{t}) 尺寸={cw}x{ch} 预期居中=({exp_x},{exp_y})")
        ok1 = abs(l-exp_x)<=3 and abs(t-exp_y)<=3
        report.append(f"启动居中: {'PASS' if ok1 else 'FAIL'}")

        SWP_NOSIZE=0x0001; SWP_NOZORDER=0x0004; SWP_NOACTIVATE=0x0010
        ok_set = u.SetWindowPos(h, 0, 200, 200, 0, 0, SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE)
        time.sleep(1.0)
        wins2 = find_window(pid)
        if wins2:
            _, l2, t2, r2, b2 = wins2[0]
            report.append(f"移动后=({l2},{t2}) SetWindowPos={ok_set}")
            ok2 = abs(l2-exp_x)<=3 and abs(t2-exp_y)<=3
            report.append(f"拖动后回中: {'PASS' if ok2 else 'FAIL'}")
        else:
            report.append("移动后找不到窗口")
finally:
    subprocess.run(['taskkill','/IM','QuickBoard.exe','/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(0.5)
    shutil.copy2(BAK, CFG)
    os.remove(BAK)

out = os.path.join(QB, "tools", "center_report.txt")
open(out,'w',encoding='utf-8').write("\n".join(report))
print("\n".join(report))
