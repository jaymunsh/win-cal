using System.Runtime.InteropServices;
using WinCal.Native;

namespace WinCal;

/// <summary>
/// Global low-level mouse hook. Our overlay lives behind the desktop icon
/// layer, so it never receives input directly; this hook lets us see clicks
/// that land on the desktop and decide whether to claim them.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly Win32.LowLevelMouseProc _proc; // keep a reference alive for the GC
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>
    /// Called for every mouse message. Args: (message, screen point).
    /// Return true to swallow the message so the desktop never sees it.
    /// </summary>
    public Func<int, Win32.POINT, bool>? Handler;

    public MouseHook() => _proc = HookProc;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler != null)
        {
            var s = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            try
            {
                if (Handler((int)wParam, s.pt))
                    return new IntPtr(1); // swallow
            }
            catch { /* never break the hook chain */ }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
