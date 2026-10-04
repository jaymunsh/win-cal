using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinCal.Native;

namespace WinCal;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CalendarService _calendar;

    private DateTime _viewMonth;
    private IntPtr _hwnd;

    // Screen-space hit regions (window covers the desktop, so client == screen).
    private readonly List<(Rect Rect, Action Action, Border Visual)> _buttonRegions = new();
    private (Rect Rect, Action Action)? _pendingButton;
    private int _lastCellUpTick;
    private object? _lastCellUpHit;
    private Win32.POINT _lastCellUpPt;
    private static readonly int _dblClickMs = System.Windows.Forms.SystemInformation.DoubleClickTime;

    private readonly DispatcherTimer _attachWatchdog;
    private readonly DispatcherTimer _refreshTimer;
    private DateOnly _lastDay = DateOnly.FromDateTime(DateTime.Today);

    private Theme _theme = Themes.Get(null);
    private Brush _text = System.Windows.Media.Brushes.White, _dim = System.Windows.Media.Brushes.White,
        _sun = System.Windows.Media.Brushes.White, _sat = System.Windows.Media.Brushes.White,
        _accent = System.Windows.Media.Brushes.White,
        _btnBg = System.Windows.Media.Brushes.White, _btnBgPressed = System.Windows.Media.Brushes.White,
        _cellBorder = System.Windows.Media.Brushes.White, _error = System.Windows.Media.Brushes.Red;

    /// <summary>Per-calendar bullet colors (cycles if more calendars).</summary>
    private static readonly Brush[] CalendarPalette =
    {
        new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)), // blue
        new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)), // amber
        new SolidColorBrush(Color.FromRgb(0x9C, 0xCC, 0x65)), // green
        new SolidColorBrush(Color.FromRgb(0xF0, 0x62, 0x92)), // pink
        new SolidColorBrush(Color.FromRgb(0xBA, 0x68, 0xC8)), // purple
        new SolidColorBrush(Color.FromRgb(0x4D, 0xD0, 0xE1)), // cyan
    };

    internal MainWindow(AppSettings settings, CalendarService calendar)
    {
        _settings = settings;
        _calendar = calendar;
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        InitializeComponent();
        ApplyFont();
        ApplyTheme();
        ApplyLoc();

        _attachWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _attachWatchdog.Tick += (_, _) => { EnsureAttached(); CheckDayRollover(); };
        _attachWatchdog.Start();

        UpdateTargetBounds();

        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += async (_, _) => await RefreshCalendarAsync();
        ApplyRefreshInterval();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => EnsureAttached(force: true);
        Deactivated += (_, _) => CommitInlineMemoEdit();

        Render();
    }

    // ---------- Desktop attachment ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        EnsureAttached(force: true);
    }

    /// <summary>Computes the pixel rect the overlay covers from MonitorIndex.</summary>
    private void UpdateTargetBounds()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        Win32.RECT b;
        if (_settings.MonitorIndex == -1 || screens.Length == 0)
        {
            int x = int.MaxValue, y = int.MaxValue, r = int.MinValue, bt = int.MinValue;
            foreach (var s in screens)
            {
                x = Math.Min(x, s.Bounds.Left); y = Math.Min(y, s.Bounds.Top);
                r = Math.Max(r, s.Bounds.Right); bt = Math.Max(bt, s.Bounds.Bottom);
            }
            b = new Win32.RECT { Left = x, Top = y, Right = r, Bottom = bt };
        }
        else
        {
            var scr = _settings.MonitorIndex < screens.Length
                ? screens[_settings.MonitorIndex] : System.Windows.Forms.Screen.PrimaryScreen!;
            b = new Win32.RECT
            {
                Left = scr.Bounds.Left, Top = scr.Bounds.Top,
                Right = scr.Bounds.Right, Bottom = scr.Bounds.Bottom
            };
        }

        if (!_settings.FullScreen)
        {
            const int margin = 8;
            int w = Math.Min(_settings.RegionWidth, b.Width - margin * 2);
            int h = Math.Min(_settings.RegionHeight, b.Height - margin * 2);
            int x = _settings.RegionLeft ?? b.Right - w - margin;
            int y = _settings.RegionTop ?? b.Top + margin;
            b = new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        }
        DesktopAttacher.TargetBounds = b;
    }

    private void EnsureAttached(bool force = false)
    {
        if (_hwnd == IntPtr.Zero || _editMode || _inlineMemoEdit) return;
        if (!force && !DesktopAttacher.NeedsAttach(_hwnd)) return;
        UpdateTargetBounds();
        bool ok = DesktopAttacher.Attach(_hwnd);
        DebugLog.Write($"Attach force={force} mode={DesktopAttacher.Mode} ok={ok}");
        if (ok)
            Dispatcher.BeginInvoke(RebuildHitRegions);
    }

    /// <summary>Day rolled over → re-render (today highlight) and re-sync.
    /// Checked on the 5s attach watchdog tick — a DateOnly compare is free.</summary>
    private void CheckDayRollover()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today == _lastDay) return;
        _lastDay = today;
        Render();
        _ = RefreshCalendarAsync();
    }

    // ---------- Rendering ----------

    private void ApplyFont()
    {
        FontFamily = _settings.FontFamily == "Pretendard"
            ? new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Pretendard")
            : new FontFamily(_settings.FontFamily);
    }

    private void ApplyLoc()
    {
        EditHint.Text = Loc.T("edit_hint");
        DoneButton.Content = Loc.T("done_lock");
    }

    private void ApplyTheme()
    {
        _theme = Themes.Get(_settings.Theme);
        _text = _theme.B(_theme.Text);
        _dim = _theme.B(_theme.Dim);
        _sun = _theme.B(_theme.Sunday);
        _sat = _theme.B(_theme.Saturday);
        _accent = _theme.B(_theme.Accent);
        _btnBg = _theme.B(_theme.ButtonBg);
        _btnBgPressed = _theme.B(_theme.ButtonBgPressed);
        _cellBorder = _theme.B(_theme.CellBorder);
        _error = _theme.B(_theme.Error);
        HeaderBar.Background = new SolidColorBrush(
            Color.FromArgb((byte)(_theme.HeaderAlpha * 255), _theme.HeaderColor.R, _theme.HeaderColor.G, _theme.HeaderColor.B));
    }

    public void ApplySettings()
    {
        ApplyFont();
        ApplyTheme();
        ApplyLoc();
        ApplyRefreshInterval();
        Root.Opacity = Math.Clamp(_settings.WindowOpacity, 0.3, 1.0);
        var s = Math.Clamp(_settings.UiScale, 0.5, 1.5);
        Root.LayoutTransform = new ScaleTransform(s, s);
        Render();
        EnsureAttached(force: true);
    }

    private void ApplyRefreshInterval() =>
        _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_settings.RefreshMinutes, 1, 720));

    public async Task RefreshCalendarAsync()
    {
        if (_calendar.IsSyncing) return;
        StatusText.Text = Loc.T("syncing");
        StatusText.Foreground = _dim;
        bool ok = await _calendar.RefreshAsync(_settings.IcsUrls);
        if (!ok && _calendar.LastError != null)
            App.Notify(Loc.T("sync_failed"), _calendar.LastError);
        Render();
    }

    private void RenderStatus()
    {
        if (_calendar.IsSyncing)
        {
            StatusText.Text = Loc.T("syncing");
            StatusText.Foreground = _dim;
        }
        else if (_calendar.LastError != null)
        {
            StatusText.Text = Loc.F("sync_failed_fmt", _calendar.LastError);
            StatusText.Foreground = _error;
        }
        else if (_calendar.LastFetched is { } t)
        {
            StatusText.Text = Loc.F("synced_fmt", t);
            StatusText.Foreground = _dim;
        }
        else StatusText.Text = "";
    }

    internal void Render()
    {
        MonthTitle.Text = _viewMonth.ToString(Loc.T("month_fmt"), Loc.Culture);
        RenderStatus();
        RenderHeaderButtons();
        RenderWeekdays();
        RenderCells();
        RenderSideMemo();
        Dispatcher.BeginInvoke(RebuildHitRegions, DispatcherPriority.Loaded);
    }

    private Border MakeButton(string text, Action action)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = _btnBg,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(4, 0, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                Foreground = _text,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            },
            Tag = action
        };
        return b;
    }

    /// <summary>Visual press feedback — the window is click-through, so real hover/press states don't exist.</summary>
    private async void FlashButton(Border b)
    {
        b.Background = _btnBgPressed;
        await Task.Delay(140);
        b.Background = _btnBg;
    }

    private void RenderHeaderButtons()
    {
        HeaderButtons.Children.Clear();
        _buttonRegions.Clear();

        void Add(string label, Action a) => HeaderButtons.Children.Add(MakeButton(label, a));

        Add("◀", () => { _viewMonth = _viewMonth.AddMonths(-1); Render(); });
        Add(Loc.T("btn_today"), () => { _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Render(); });
        Add("▶", () => { _viewMonth = _viewMonth.AddMonths(1); Render(); });
        Add(Loc.T("btn_refresh"), () => _ = RefreshCalendarAsync());
        Add(Loc.T("btn_gcal"), App.OpenGoogleCalendar);
        Add(Loc.T("btn_settings"), () => App.OpenSettings(this));
    }

    private void RenderWeekdays()
    {
        WeekdayGrid.Children.Clear();
        WeekdayGrid.ColumnDefinitions.Clear();
        // i = column index → DayOfWeek of that column
        for (int i = 0; i < 7; i++)
        {
            var dow = _settings.WeekStartsMonday ? (DayOfWeek)((i + 1) % 7) : (DayOfWeek)i;
            string[] names = Loc.Weekdays;
            WeekdayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = names[(int)dow],
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = dow == DayOfWeek.Sunday ? _sun
                           : dow == DayOfWeek.Saturday ? _sat : _dim
            };
            Grid.SetColumn(tb, i);
            WeekdayGrid.Children.Add(tb);
        }
    }

    private void RenderCells()
    {
        CellsGrid.Children.Clear();
        CellsGrid.RowDefinitions.Clear();
        CellsGrid.ColumnDefinitions.Clear();
        _calBrushCache.Clear();

        var firstOfMonth = _viewMonth;
        int dow = (int)firstOfMonth.DayOfWeek; // Sun=0..Sat=6
        int offset = _settings.WeekStartsMonday ? (dow + 6) % 7 : dow;
        var gridStart = firstOfMonth.AddDays(-offset);
        int weeks = (int)Math.Ceiling((offset + DateTime.DaysInMonth(_viewMonth.Year, _viewMonth.Month)) / 7.0);

        var events = _calendar.GetEvents(
            DateOnly.FromDateTime(gridStart),
            DateOnly.FromDateTime(gridStart.AddDays(weeks * 7 - 1)));

        for (int c = 0; c < 7; c++)
            CellsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < weeks; r++)
            CellsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        byte alpha = (byte)Math.Clamp(_settings.CellOpacity * _theme.CellAlphaFactor * 255, 20, 255);
        var cellBg = new SolidColorBrush(Color.FromArgb(alpha, _theme.CellColor.R, _theme.CellColor.G, _theme.CellColor.B));
        var todayBg = new SolidColorBrush(Color.FromArgb((byte)Math.Min(alpha + 40, 240),
            _theme.TodayColor.R, _theme.TodayColor.G, _theme.TodayColor.B));

        for (int i = 0; i < weeks * 7; i++)
        {
            var date = DateOnly.FromDateTime(gridStart.AddDays(i));
            bool inMonth = date.Month == _viewMonth.Month && date.Year == _viewMonth.Year;
            bool isToday = date == DateOnly.FromDateTime(DateTime.Today);

            var cell = new Border
            {
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(1.5),
                Background = isToday ? todayBg : cellBg,
                BorderBrush = isToday ? _accent : _cellBorder,
                BorderThickness = new Thickness(isToday ? 1.2 : 0.5),
                Tag = date
            };

            var panel = new StackPanel { Margin = new Thickness(6, 3, 6, 3) };

            var dayNum = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = date.DayOfWeek == DayOfWeek.Sunday ? _sun
                           : date.DayOfWeek == DayOfWeek.Saturday ? _sat
                           : _text,
                Opacity = inMonth ? 1.0 : 0.35
            };
            panel.Children.Add(dayNum);

            const int maxLines = 5;
            int shown = 0, hidden = 0;

            if (events.TryGetValue(date, out var evList))
                foreach (var ev in evList)
                {
                    if (shown >= maxLines) { hidden++; continue; }
                    var bullet = CalendarBrush(ev.CalendarIndex);
                    if (ev.AllDay)
                    {
                        // Google Calendar style: colored chip spanning the cell.
                        panel.Children.Add(new Border
                        {
                            Background = bullet,
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(4, 1, 4, 1),
                            Margin = new Thickness(0, 1, 0, 1),
                            Opacity = inMonth ? 0.9 : 0.3,
                            Child = new TextBlock
                            {
                                Text = ev.Title,
                                FontSize = 10.5,
                                Foreground = System.Windows.Media.Brushes.White,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        });
                    }
                    else
                    {
                        var tb = new TextBlock
                        {
                            FontSize = 11,
                            Opacity = inMonth ? 0.95 : 0.35,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        tb.Inlines.Add(new System.Windows.Documents.Run("● ")
                        {
                            Foreground = bullet,
                            FontSize = 8,
                            BaselineAlignment = BaselineAlignment.Center
                        });
                        tb.Inlines.Add(new System.Windows.Documents.Run(
                            ev.TimeText + " " + ev.Title)
                        {
                            Foreground = _text
                        });
                        panel.Children.Add(tb);
                    }
                    shown++;
                }

            if (hidden > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = Loc.F("more_fmt", hidden),
                    FontSize = 10,
                    Foreground = _dim,
                    Opacity = inMonth ? 0.8 : 0.3
                });

            cell.Child = panel;
            Grid.SetRow(cell, i / 7);
            Grid.SetColumn(cell, i % 7);
            CellsGrid.Children.Add(cell);
        }
    }

    // Per-render cache: many events share a calendar color.
    private readonly Dictionary<int, Brush> _calBrushCache = new();

    /// <summary>Feed color (COLOR / X-WR-CALCOLOR) if present, else palette by index.</summary>
    private Brush CalendarBrush(int index)
    {
        if (_calBrushCache.TryGetValue(index, out var cached)) return cached;
        Brush result;
        if (index < _calendar.Colors.Count && _calendar.Colors[index] is { } hex)
        {
            try
            {
                var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    hex.StartsWith('#') ? hex : "#" + hex);
                result = new SolidColorBrush(c);
            }
            catch { result = CalendarPalette[index % CalendarPalette.Length]; }
        }
        else result = CalendarPalette[index % CalendarPalette.Length];
        _calBrushCache[index] = result;
        return result;
    }

    // ---------- Side memo panel ----------

    private Rect? _memoRegion;
    private static readonly object MemoHit = new();
    private bool _inlineMemoEdit;
    private System.Windows.Controls.TextBox? _memoEditBox;
    private string _memoEditOriginal = "";

    private void RenderSideMemo()
    {
        SideMemoPanel.Visibility = _settings.SideMemoVisible ? Visibility.Visible : Visibility.Collapsed;
        SideMemoPanel.Width = _settings.SideMemoWidth;
        byte alpha = (byte)Math.Clamp(_settings.CellOpacity * _theme.CellAlphaFactor * 255, 20, 255);
        SideMemoPanel.Background = new SolidColorBrush(Color.FromArgb(alpha,
            _theme.CellColor.R, _theme.CellColor.G, _theme.CellColor.B));

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("memo_title"), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = _dim, Margin = new Thickness(0, 0, 0, 6)
        });
        if (_editMode || _inlineMemoEdit)
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = _settings.SideMemoText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = _text,
                CaretBrush = _text,
                FontSize = 12
            };
            box.LostFocus += (_, _) => { if (!_inlineMemoEdit) SettingsStore.Save(_settings); };
            if (_inlineMemoEdit)
            {
                _memoEditBox = box;
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape) CommitInlineMemoEdit(save: false);
                    else if (e.Key == Key.Enter
                        && (System.Windows.Input.Keyboard.Modifiers & ModifierKeys.Shift) == 0)
                    {
                        CommitInlineMemoEdit();
                        e.Handled = true;
                    }
                };
            }
            else box.TextChanged += (_, _) => _settings.SideMemoText = box.Text;
            stack.Children.Add(box);
        }
        else
        {
            bool empty = string.IsNullOrWhiteSpace(_settings.SideMemoText);
            stack.Children.Add(new TextBlock
            {
                Text = empty ? Loc.T("memo_hint") : _settings.SideMemoText,
                Foreground = empty ? _dim : _text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
        SideMemoPanel.Child = stack;
    }

    /// <summary>Double-click on the memo panel while locked: temporarily make the
    /// window interactive so the user can type directly, then lock back.</summary>
    private void BeginInlineMemoEdit()
    {
        if (_inlineMemoEdit) return;
        _inlineMemoEdit = true;
        _memoEditOriginal = _settings.SideMemoText;
        SetInteractive(true);
        RenderSideMemo();
        Activate();
        if (_memoEditBox != null)
        {
            _memoEditBox.Focus();
            _memoEditBox.CaretIndex = _memoEditBox.Text.Length;
        }
    }

    private void CommitInlineMemoEdit(bool save = true)
    {
        if (!_inlineMemoEdit) return;
        _inlineMemoEdit = false;
        _settings.SideMemoText = (save && _memoEditBox != null)
            ? _memoEditBox.Text.Trim()
            : _memoEditOriginal;
        _memoEditBox = null;
        SettingsStore.Save(_settings);
        SetInteractive(false);
        RenderSideMemo();
        RebuildHitRegions();
    }

    /// <summary>Toggle the window between click-through (desktop passthrough)
    /// and interactive (topmost, receives input) — used for inline memo editing.</summary>
    private void SetInteractive(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        long ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        if (on)
        {
            ex &= ~(Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE);
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
            Win32.SetWindowPos(_hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW | Win32.SWP_FRAMECHANGED);
        }
        else
        {
            ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
            Win32.SetWindowPos(_hwnd, new IntPtr(-2) /* HWND_NOTOPMOST */, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_FRAMECHANGED);
            EnsureAttached(force: true);
        }
    }

    // ---------- Hit testing (driven by the global mouse hook) ----------

    private void RebuildHitRegions()
    {
        double dpi = DpiScale();
        _buttonRegions.Clear();
        foreach (Border b in HeaderButtons.Children)
        {
            if (b.Tag is not Action a || !b.IsVisible) continue;
            var tl = b.PointToScreen(new Point(0, 0));
            _buttonRegions.Add((new Rect(tl.X, tl.Y, b.ActualWidth * dpi, b.ActualHeight * dpi), a, b));
        }

        _memoRegion = null;
        if (SideMemoPanel.Visibility == Visibility.Visible)
        {
            var tl = SideMemoPanel.PointToScreen(new Point(0, 0));
            _memoRegion = new Rect(tl.X, tl.Y,
                SideMemoPanel.ActualWidth * dpi, SideMemoPanel.ActualHeight * dpi);
        }
        DebugLog.Write($"HitRegions buttons={_buttonRegions.Count} dpi={dpi}");
    }

    private double DpiScale() => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private static bool IsDesktopHit(Win32.POINT pt)
    {
        var h = Win32.WindowFromPoint(pt);
        var direct = Win32.GetClassName(h);
        if (direct is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman"
            or "XamlExplorerHostIslandWindow")
            return true;
        // Modern desktop: icon children live inside the XAML island window.
        var root = Win32.GetAncestor(h, Win32.GA_ROOT);
        if (root != IntPtr.Zero && root != h)
        {
            var rootCls = Win32.GetClassName(root);
            return rootCls is "Progman" or "WorkerW" or "XamlExplorerHostIslandWindow";
        }
        return false;
    }

    private static string ClassAt(Win32.POINT pt)
    {
        var h = Win32.WindowFromPoint(pt);
        var root = Win32.GetAncestor(h, Win32.GA_ROOT);
        return Win32.GetClassName(h) + (root != h ? " <- " + Win32.GetClassName(root) : "");
    }

    /// <summary>Called by MouseHook. Return true to swallow the message.</summary>
    internal bool HandleMouseMessage(int msg, Win32.POINT pt)
    {
        if (_editMode || _inlineMemoEdit) return false; // real window input while editing
        bool result;
        switch (msg)
        {
            case Win32.WM_LBUTTONDOWN:
                result = false;
                // Cheap checks first — the icon UIA query is the expensive one.
                bool onButton = false;
                foreach (var (rect, action, visual) in _buttonRegions)
                    if (rect.Contains(pt.X, pt.Y))
                    {
                        _pendingButton = (rect, action);
                        FlashButton(visual);
                        onButton = true;
                    }
                if (!onButton) break;
                if (!IsDesktopHit(pt)) { _pendingButton = null; break; }
                if (DesktopIcons.IsOnIcon(pt)) { _pendingButton = null; DebugLog.Write($"DOWN on icon {pt.X},{pt.Y}"); break; }
                DebugLog.Write($"DOWN {pt.X},{pt.Y} buttonHit");
                result = true;
                break;

            case Win32.WM_LBUTTONUP:
                if (_pendingButton is { } pending)
                {
                    _pendingButton = null;
                    if (pending.Rect.Contains(pt.X, pt.Y))
                        Dispatcher.BeginInvoke(pending.Action);
                    result = true;
                }
                else
                {
                    result = false;
                    // Double-click detection on the side memo panel.
                    if (_memoRegion is not { } mr || !mr.Contains(pt.X, pt.Y))
                    {
                        _lastCellUpHit = null;
                        break;
                    }

                    int now = Environment.TickCount;
                    bool isDouble = _lastCellUpHit == MemoHit
                        && now - _lastCellUpTick <= _dblClickMs
                        && Math.Abs(pt.X - _lastCellUpPt.X) < 10
                        && Math.Abs(pt.Y - _lastCellUpPt.Y) < 10;
                    _lastCellUpTick = now;
                    _lastCellUpHit = MemoHit;
                    _lastCellUpPt = pt;
                    if (!isDouble) break;
                    if (!IsDesktopHit(pt)) { DebugLog.Write($"DBLCLK not desktop {pt.X},{pt.Y} {ClassAt(pt)}"); break; }
                    if (DesktopIcons.IsOnIcon(pt)) { DebugLog.Write($"DBLCLK on icon {pt.X},{pt.Y}"); break; }
                    _lastCellUpHit = null;
                    Dispatcher.BeginInvoke(BeginInlineMemoEdit);
                    DebugLog.Write($"DBLCLK {pt.X},{pt.Y} memo");
                    result = true;
                }
                break;

            case Win32.WM_LBUTTONDBLCLK:
                DebugLog.Write($"DBLCLK msg {pt.X},{pt.Y}");
                result = false;
                break;

            default:
                return false;
        }
        return result;
    }

    public void PrevMonth() { _viewMonth = _viewMonth.AddMonths(-1); Render(); }
    public void NextMonth() { _viewMonth = _viewMonth.AddMonths(1); Render(); }

    // ---------- Position edit mode ("위치 조정") ----------

    private bool _editMode;
    public bool IsEditMode => _editMode;

    /// <summary>Unlock: window rises above icons and receives real input.</summary>
    public void EnterEditMode()
    {
        if (_editMode) return;
        _editMode = true;
        EditOverlay.Visibility = Visibility.Visible;
        RenderSideMemo();

        long ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex &= ~(Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));

        // Raise above the icon layer so the user can grab it.
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW | Win32.SWP_FRAMECHANGED);
        Activate();
    }

    /// <summary>Lock: persist bounds, go click-through again, re-pin to desktop.</summary>
    public void ExitEditMode()
    {
        if (!_editMode) return;
        _editMode = false;
        EditOverlay.Visibility = Visibility.Collapsed;
        SettingsStore.Save(_settings); // side memo text edited inline
        RenderSideMemo();
        RebuildHitRegions();

        Win32.GetWindowRect(_hwnd, out var rc);
        _settings.FullScreen = false;
        _settings.RegionLeft = rc.Left;
        _settings.RegionTop = rc.Top;
        _settings.RegionWidth = rc.Width;
        _settings.RegionHeight = rc.Height;
        SettingsStore.Save(_settings);

        long ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));

        EnsureAttached(force: true);
    }

    private void EditOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Win32.GetWindowRect(_hwnd, out var rc);
        double scale = DpiScale();
        int newW = Math.Max(320, rc.Width + (int)(e.HorizontalChange * scale));
        int newH = Math.Max(220, rc.Height + (int)(e.VerticalChange * scale));
        Win32.MoveWindow(_hwnd, rc.Left, rc.Top, newW, newH, true);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        ExitEditMode();
        App.SetPositionMenuState(false);
    }
}
