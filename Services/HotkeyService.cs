using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LaunchPad.Services;

/// <summary>全局快捷键服务：基于 RegisterHotKey + 隐藏消息窗口，支持多个热键 ID。</summary>
public sealed class HotkeyService : IDisposable
{
    /// <summary>主呼出热键的固定 ID。</summary>
    public const int MainHotkeyId = 0x0D00;

    private HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();

    /// <summary>主呼出热键被按下时触发（兼容旧接口）。</summary>
    public event Action Pressed;

    /// <summary>注册主呼出热键（兼容旧接口）。返回 false 表示注册失败（被占用等）。</summary>
    public bool Register(int modifiers, int key)
        => Register(MainHotkeyId, modifiers, key, () => Pressed?.Invoke());

    /// <summary>注册一个带指定 ID 的全局热键。返回 false 表示注册失败（被占用等）。</summary>
    public bool Register(int id, int modifiers, int key, Action handler)
    {
        if (handler == null) return false;
        try
        {
            EnsureSource();
            // 同 ID 先注销旧的
            if (_handlers.ContainsKey(id))
            {
                NativeMethods.UnregisterHotKey(_source.Handle, id);
                _handlers.Remove(id);
            }
            bool ok = NativeMethods.RegisterHotKey(
                _source.Handle, id, (uint)modifiers | NativeMethods.MOD_NOREPEAT, (uint)key);
            if (ok) _handlers[id] = handler;
            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>注销指定 ID 的热键。</summary>
    public void Unregister(int id)
    {
        if (_source != null && _handlers.ContainsKey(id))
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
            _handlers.Remove(id);
        }
        if (_handlers.Count == 0) DisposeSource();
    }

    /// <summary>注销所有热键并释放消息窗口。</summary>
    public void UnregisterAll()
    {
        if (_source != null)
        {
            foreach (var id in _handlers.Keys.ToList())
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            _handlers.Clear();
        }
        DisposeSource();
    }

    /// <summary>兼容旧接口：注销所有热键。</summary>
    public void Unregister() => UnregisterAll();

    private void EnsureSource()
    {
        if (_source != null) return;
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
    }

    private void DisposeSource()
    {
        if (_source != null)
        {
            _source.Dispose();
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var handler))
        {
            handler?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => UnregisterAll();
}
