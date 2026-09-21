using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace WinCal;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    private MainWindow? _main;
    private MouseHook? _hook;
    private NotifyIcon? _tray;
    private ToolStripMenuItem? _positionItem;
    private AppSettings? _settings;
    private StickerStore? _stickers;

    public static AppSettings Settings => ((App)Current)._settings!;
    public static MainWindow? Overlay => ((App)Current)._main;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "WinCal.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = SettingsStore.Load();
        DebugLog.Enabled = _settings.DebugLogEnabled;
        _stickers = StickerStore.Load();
        var calendar = new CalendarService();
        calendar.LoadCached();

        _main = new MainWindow(_settings, calendar, _stickers);
        _main.Show();

        _hook = new MouseHook { Handler = _main.HandleMouseMessage };
        _hook.Start();

        SetupTray();

        if (_settings.IcsUrls.Count > 0)
            _ = _main.RefreshCalendarAsync();
        else
            Dispatcher.BeginInvoke(() => OpenSettings(_main));
    }

    private void SetupTray()
    {
        _tray = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true
        };
        RebuildTrayMenu();
        _tray.DoubleClick += (_, _) => OpenGoogleCalendar();
    }

    /// <summary>(Re)builds tray menu — also used to apply a language change.</summary>
    public void RebuildTrayMenu()
    {
        if (_tray == null) return;
        _tray.Text = Loc.T("tray_tip");
        var menu = new ContextMenuStrip();
        menu.Items.Add(Loc.T("tray_refresh"), null, (_, _) => { if (_main != null) _ = _main.RefreshCalendarAsync(); });
        menu.Items.Add(Loc.T("tray_gcal"), null, (_, _) => OpenGoogleCalendar());
        menu.Items.Add(Loc.T("tray_sticker"), null, (_, _) => _main?.AddStickerCenter());
        menu.Items.Add(new ToolStripSeparator());
        _positionItem = new ToolStripMenuItem(
            _main?.IsEditMode == true ? Loc.T("tray_lock") : Loc.T("tray_position"),
            null, (_, _) => TogglePositionEdit());
        menu.Items.Add(_positionItem);
        menu.Items.Add(Loc.T("tray_settings"), null, (_, _) => { if (_main != null) OpenSettings(_main); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("tray_exit"), null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    private void TogglePositionEdit()
    {
        if (_main == null) return;
        if (_main.IsEditMode) _main.ExitEditMode();
        else _main.EnterEditMode();
        SetPositionMenuState(_main.IsEditMode);
    }

    public static void SetPositionMenuState(bool editing)
    {
        var app = (App)Current;
        if (app._positionItem != null)
            app._positionItem.Text = editing ? Loc.T("tray_lock") : Loc.T("tray_position");
    }

    public static void Notify(string title, string text) =>
        ((App)Current)._tray?.ShowBalloonTip(4000, title, text, ToolTipIcon.Warning);

    public static void OpenGoogleCalendar() =>
        Process.Start(new ProcessStartInfo("https://calendar.google.com") { UseShellExecute = true });

    public static void OpenSettings(MainWindow owner)
    {
        var app = (App)Current;
        var win = new SettingsWindow(app._settings!, () =>
        {
            owner.ApplySettings();
            app.RebuildTrayMenu();
            _ = owner.RefreshCalendarAsync();
        });
        win.ShowDialog();
    }

    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(0x1E, 0x1E, 0x24));
            using var header = new SolidBrush(Color.FromArgb(0x4C, 0xC2, 0xFF));
            using var line = new Pen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), 1.5f);
            g.FillRoundedRectangle(body, 3, 4, 26, 25, 5);
            g.FillRoundedRectangle(header, 3, 4, 26, 8, 5);
            g.FillRectangle(header, 3, 9, 26, 3);
            g.DrawLine(line, 12, 14, 12, 27);
            g.DrawLine(line, 21, 14, 21, 27);
            g.DrawLine(line, 5, 20, 27, 20);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) _tray.Visible = false;
        _tray?.Dispose();
        _hook?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush,
        int x, int y, int w, int h, int r)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
