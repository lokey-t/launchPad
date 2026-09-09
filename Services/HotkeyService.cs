using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LaunchPad.Services;

/// <summary>全局快捷键服务：基于 RegisterHotKey + 隐藏消息窗口。</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x0D00; // 自定义消息 id
    private HwndSource _source;
    private bool _registered;

    /// <summary>快捷键被按下时触发。</summary>
    public event Action Pressed;

    /// <summary>尝试注册全局快捷键。返回 false 表示注册失败（被占用等）。</summary>
    public bool Register(int modifiers, int key)
    {
        Unregister();
        try
        {
            var p = new HwndSourceParameters("LaunchPadHotkeyWindow")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x08000000, // WS_EX_TOOLWINDOW：不出现在任务栏
                PositionX = -32000,
                PositionY = -32000,
                ParentWindow = IntPtr.Zero
            };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            _registered = NativeMethods.RegisterHotKey(
                _source.Handle, HotkeyId, (uint)modifiers | NativeMethods.MOD_NOREPEAT, (uint)key);
            if (!_registered)
            {
                _source.Dispose();
                _source = null;
            }
            return _registered;
        }
        catch
        {
            return false;
        }
    }

    public void Unregister()
    {
        if (_registered && _source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
        if (_source != null)
        {
            _source.Dispose();
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && (int)wParam == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
