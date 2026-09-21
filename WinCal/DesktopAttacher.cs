using WinCal.Native;

namespace WinCal;

/// <summary>
/// Pins the overlay window to the desktop. Two strategies:
///
/// Classic desktop (SHELLDLL_DefView exists, pre/early-Win11): parent the
/// window into the wallpaper WorkerW so it renders behind the icons.
///
/// Modern desktop (Win11 XAML islands, no SHELLDLL_DefView): keep the window
/// top-level but fully click-through (WS_EX_TRANSPARENT) and pin its z-order
/// directly above Progman, so the icon layer (XamlExplorerHostIslandWindow)
/// stays on top and keeps all input. Interaction comes from the global
/// mouse hook either way.
/// </summary>
internal static class DesktopAttacher
{
    public enum AttachMode { None, Classic, Modern }
    public static AttachMode Mode { get; private set; } = AttachMode.None;

    /// <summary>
    /// Finds the WorkerW that sits behind the desktop icon layer.
    /// Sending 0x052C to Progman makes Explorer create it if missing.
    /// Returns zero on the modern XAML desktop (no SHELLDLL_DefView).
    /// </summary>
    public static IntPtr FindWorkerW()
    {
        var progman = Win32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        Win32.SendMessageTimeout(progman, Win32.WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero,
            0, 1000, out _);
        Win32.SendMessageTimeout(progman, Win32.WM_SPAWN_WORKERW, new IntPtr(0xD), new IntPtr(0x1),
            0, 1000, out _);

        IntPtr workerW = IntPtr.Zero;
        Win32.EnumWindows((hWnd, _) =>
        {
            var defView = Win32.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
                workerW = Win32.FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
            return workerW == IntPtr.Zero;
        }, IntPtr.Zero);
        return workerW;
    }

    /// <summary>Screen-space pixel rect the overlay should cover.</summary>
    public static Win32.RECT TargetBounds = new()
    {
        Left = 0, Top = 0,
        Right = Win32.GetSystemMetrics(Win32.SM_CXSCREEN),
        Bottom = Win32.GetSystemMetrics(Win32.SM_CYSCREEN)
    };

    public static bool Attach(IntPtr hwnd)
    {
        var progman = Win32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            DebugLog.Write("Attach: no Progman");
            return false;
        }

        // Classic desktop only: retry WorkerW discovery until we lock into modern mode.
        if (Mode != AttachMode.Modern)
        {
            var workerW = FindWorkerW();
            if (workerW != IntPtr.Zero)
                return AttachClassic(hwnd, workerW);
            Mode = AttachMode.Modern;
        }
        return AttachModern(hwnd, progman);
    }

    private static bool AttachClassic(IntPtr hwnd, IntPtr workerW)
    {
        Win32.SetParent(hwnd, workerW);

        long style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
        style = (style & ~Win32.WS_POPUP) | Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, new IntPtr(style));

        long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE) & ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(exStyle));

        Win32.GetClientRect(workerW, out var rc);
        Win32.MoveWindow(hwnd, 0, 0, rc.Width, rc.Height, true);

        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE |
            Win32.SWP_SHOWWINDOW | Win32.SWP_FRAMECHANGED);
        Mode = AttachMode.Classic;
        return true;
    }

    private static bool AttachModern(IntPtr hwnd, IntPtr progman)
    {
        // Recover if the window got minimized (e.g. Win+D).
        if (Win32.IsIconic(hwnd))
            Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);

        // Fully click-through: every pixel passes input to the desktop below,
        // so icons stay clickable. All interaction is via the mouse hook.
        long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT)
                  & ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(exStyle));

        // Cover the configured bounds (taskbar is a separate window above us).
        var b = TargetBounds;
        Win32.MoveWindow(hwnd, b.Left, b.Top, b.Width, b.Height, true);

        // Pin z-order directly above Progman (wallpaper) — below the icon layer.
        bool ok = Win32.SetWindowPos(hwnd, progman, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE |
            Win32.SWP_SHOWWINDOW | Win32.SWP_FRAMECHANGED);
        DebugLog.Write($"AttachModern ok={ok} bounds={b.Left},{b.Top}-{b.Right}x{b.Bottom}");
        return ok;
    }

    /// <summary>
    /// Classic mode: true while still parented to a live WorkerW.
    /// Modern mode: re-pin only when position or z-order actually drifted
    /// (window above us isn't Progman, bounds changed, or we got minimized).
    /// </summary>
    public static bool NeedsAttach(IntPtr hwnd)
    {
        if (Mode == AttachMode.Modern)
        {
            if (Win32.IsIconic(hwnd)) return true;
            var progman = Win32.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return true;
            if (Win32.GetWindow(hwnd, Win32.GW_HWNDPREV) != progman) return true;
            Win32.GetWindowRect(hwnd, out var rc);
            var b = TargetBounds;
            return rc.Left != b.Left || rc.Top != b.Top
                || rc.Width != b.Width || rc.Height != b.Height;
        }
        var parent = Win32.GetParent(hwnd);
        return !(parent != IntPtr.Zero && Win32.IsWindow(parent)
            && Win32.GetClassName(parent) == "WorkerW");
    }
}
