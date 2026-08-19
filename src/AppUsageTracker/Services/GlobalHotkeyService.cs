using System.Windows;
using System.Windows.Interop;
using AppUsageTracker.Models;

namespace AppUsageTracker.Services;

/// <summary>
/// 全局快捷键注册。把 RegisterHotKey 注册到主窗口句柄上，并通过其 HwndSource 拦截
/// WM_HOTKEY；主窗口最小化到托盘时句柄仍有效，因此隐藏后也能响应快捷键。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    /// <summary>注册用的唯一 ID，同一进程内只需一个实例。</summary>
    private const int HotkeyId = 0x5452;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _registered;

    public GlobalHotkeyService(Window window)
    {
        _window = window;
    }

    /// <summary>快捷键被按下时触发。</summary>
    public event Action? Pressed;

    /// <summary>注册或更新快捷键；定义无效或注册失败时返回 false。</summary>
    public bool Register(HotkeyDefinition hotkey)
    {
        Unregister();
        if (!hotkey.IsValid)
        {
            return false;
        }

        var source = EnsureSource();
        _registered = NativeMethods.RegisterHotKey(
            source.Handle,
            HotkeyId,
            ToNativeModifiers(hotkey.Modifiers),
            (uint)hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        }

        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    /// <summary>确保主窗口句柄已创建并挂上消息钩子，返回其 HwndSource。</summary>
    private HwndSource EnsureSource()
    {
        if (_source is not null)
        {
            return _source;
        }

        var helper = new WindowInteropHelper(_window);
        var handle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);
        return _source;
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        var result = NativeMethods.ModNoRepeat;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= NativeMethods.ModShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            result |= NativeMethods.ModWin;
        }

        return result;
    }
}
