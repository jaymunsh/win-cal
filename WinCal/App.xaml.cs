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
        var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        return new Icon(s!.Stream, 32, 32);
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
