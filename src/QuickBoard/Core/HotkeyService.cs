using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickBoard.Core;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5131;

    private HwndSource? _source;
    private IntPtr _handle;
    private bool _registered;
    private bool _hooked;

    /// <summary>最近一次成功生效的热键设置（用于新设置注册失败时回滚）。</summary>
    private HotkeySetting? _lastGood;

    /// <summary>当前实际生效的热键；从未注册成功则为 null。</summary>
    public HotkeySetting? Active { get; private set; }

    public event Action? Pressed;

    public string? Register(Window window, HotkeySetting setting)
    {
        var helper = new WindowInteropHelper(window);
        _handle = helper.EnsureHandle();
        if (_handle == IntPtr.Zero) return "窗口句柄尚未创建，热键注册失败";

        EnsureHook();
        UnregisterHotkey();

        // 无法识别的按键名：同样回滚，避免把上一次可用的热键弄丢
        if (!Enum.TryParse<Key>(setting.Key, true, out var key) || key == Key.None)
            return $"无法识别的按键：{setting.Key}" + TryRollback();

        if (!TryRegisterHotkey(setting, key))
            return $"热键 {setting} 注册失败，可能已被其他程序占用" + TryRollback();

        Remember(setting);
        return null;
    }

    /// <summary>回滚到上一次成功的热键，返回给用户看的中文后缀。</summary>
    private string TryRollback()
    {
        if (_lastGood is null) return "";
        if (!Enum.TryParse<Key>(_lastGood.Key, true, out var key)) return "（原热键也无法恢复，请换一个组合）";
        return TryRegisterHotkey(_lastGood, key)
            ? $"（已保留原热键 {_lastGood}）"
            : "（原热键也无法恢复，请换一个组合）";
    }

    private void Remember(HotkeySetting setting)
    {
        _lastGood = Clone(setting);
        Active = _lastGood;
    }

    private static HotkeySetting Clone(HotkeySetting s) => new()
    {
        Ctrl = s.Ctrl,
        Alt = s.Alt,
        Shift = s.Shift,
        Win = s.Win,
        Key = s.Key
    };

    private bool TryRegisterHotkey(HotkeySetting setting, Key key)
    {
        uint modifiers = Native.MOD_NOREPEAT;
        if (setting.Ctrl) modifiers |= Native.MOD_CONTROL;
        if (setting.Alt) modifiers |= Native.MOD_ALT;
        if (setting.Shift) modifiers |= Native.MOD_SHIFT;
        if (setting.Win) modifiers |= Native.MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!Native.RegisterHotKey(_handle, HotkeyId, modifiers, vk)) return false;

        _registered = true;
        return true;
    }

    private void EnsureHook()
    {
        if (_hooked) return;
        _source ??= HwndSource.FromHwnd(_handle);
        if (_source is null) return;
        _source.AddHook(WndProc);
        _hooked = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    private void UnregisterHotkey()
    {
        if (_registered && _handle != IntPtr.Zero)
        {
            Native.UnregisterHotKey(_handle, HotkeyId);
            _registered = false;
        }
    }

    /// <summary>注销当前热键（保留窗口钩子，重新注册时仍然有效）。</summary>
    public void Unregister()
    {
        UnregisterHotkey();

        if (_hooked)
        {
            _source?.RemoveHook(WndProc);
            _hooked = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source = null;
    }
}
