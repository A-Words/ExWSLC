using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ExWSLC.Services;

/// <summary>
/// Raises <see cref="ThemeChanged"/> when the Windows light/dark personalization changes,
/// by hooking the <c>WM_SETTINGCHANGE</c> broadcast (carrying <c>ImmersiveColorSet</c>) on a
/// window's message pump. The callback fires on the UI thread.
/// </summary>
public sealed class SystemThemeWatcher : IDisposable
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const string ImmersiveColorSet = "ImmersiveColorSet";

    private HwndSource? _source;
    private HwndSourceHook? _hook;

    /// <summary>Raised on the UI thread when the system light/dark preference changes.</summary>
    public event EventHandler? ThemeChanged;

    public void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        if (_source is null)
        {
            return;
        }

        _hook = WndProc;
        _source.AddHook(_hook);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero &&
            Marshal.PtrToStringUni(lParam) == ImmersiveColorSet)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null && _hook is not null)
        {
            _source.RemoveHook(_hook);
            _source = null;
            _hook = null;
        }
    }
}
