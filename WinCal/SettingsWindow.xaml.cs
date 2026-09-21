using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;

namespace WinCal;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _onSaved;

    internal SettingsWindow(AppSettings settings, Action onSaved)
    {
        _settings = settings;
        _onSaved = onSaved;
        InitializeComponent();
        ApplyLoc();

        IcsBox.Text = string.Join(Environment.NewLine, settings.IcsUrls);
        RefreshBox.Text = settings.RefreshMinutes.ToString();
        StartupCheck.IsChecked = settings.RunAtStartup;
        OpacitySlider.Value = settings.CellOpacity;
        WindowOpacitySlider.Value = settings.WindowOpacity;
        ScaleSlider.Value = settings.UiScale;
        WeekStartCombo.SelectedIndex = settings.WeekStartsMonday ? 1 : 0;
        FullScreenCheck.IsChecked = settings.FullScreen;
        SideMemoCheck.IsChecked = settings.SideMemoVisible;
        RegionW.Text = settings.RegionWidth.ToString();
        RegionH.Text = settings.RegionHeight.ToString();
        UpdateLabels();
        RegionToggle(null, null!);

        FontCombo.Items.Add("Pretendard");
        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s))
            FontCombo.Items.Add(f);
        FontCombo.SelectedItem = FontCombo.Items.Contains(settings.FontFamily)
            ? settings.FontFamily : "Pretendard";

        foreach (var t in Themes.All) ThemeCombo.Items.Add(Loc.Korean ? t.Name : t.NameEn);
        ThemeCombo.SelectedIndex = Math.Max(0,
            Array.FindIndex(Themes.All, t => t.Name == settings.Theme));

        LangCombo.SelectedIndex = settings.Language == "en" ? 1 : 0;

        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            MonitorCombo.Items.Add(Loc.F("monitor_fmt", i + 1, s.Bounds.Width, s.Bounds.Height)
                + (s.Primary ? Loc.T("primary_suffix") : ""));
        }
        if (screens.Length > 1)
            MonitorCombo.Items.Add(Loc.T("all_monitors"));
        MonitorCombo.SelectedIndex = settings.MonitorIndex == -1
            ? MonitorCombo.Items.Count - 1
            : Math.Min(settings.MonitorIndex, screens.Length - 1);
    }

    private void ApplyLoc()
    {
        Title = Loc.T("set_title");
        IcsLabel.Text = Loc.T("set_ics_label");
        IcsHint.Text = Loc.T("set_ics_hint");
        FontLabel.Text = Loc.T("set_font");
        ThemeLabel.Text = Loc.T("set_theme");
        LangLabel.Text = Loc.T("set_language");
        MonitorLabel.Text = Loc.T("set_monitor");
        RefreshLabel.Text = Loc.T("set_refresh");
        WeekStartLabel.Text = Loc.T("set_weekstart");
        WeekSun.Content = Loc.T("sunday");
        WeekMon.Content = Loc.T("monday");
        FullScreenCheck.Content = Loc.T("fullscreen");
        SideMemoCheck.Content = Loc.T("sidememo");
        SizeLabel.Text = Loc.T("size");
        RegionHint.Text = Loc.T("region_hint");
        ScaleRun.Text = Loc.T("scale");
        CellOpRun.Text = Loc.T("cell_opacity");
        WinOpRun.Text = Loc.T("win_opacity");
        StartupCheck.Content = Loc.T("startup");
        DefaultsButton.Content = Loc.T("defaults");
        DefaultsButton.ToolTip = Loc.T("defaults_tip");
        SaveButton.Content = Loc.T("save");
        CancelButton.Content = Loc.T("cancel");
    }

    private void UpdateLabels()
    {
        // Sliders fire ValueChanged while XAML is still loading — labels may not exist yet.
        if (!IsInitialized || ScaleLabel == null) return;
        ScaleLabel.Text = $"  {ScaleSlider.Value:P0}";
        CellOpacityLabel.Text = $"  {OpacitySlider.Value:P0}";
        WindowOpacityLabel.Text = $"  {WindowOpacitySlider.Value:P0}";
    }

    private void ScaleSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();
    private void OpacitySlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();
    private void WindowOpacitySlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();

    private void RegionToggle(object? sender, RoutedEventArgs e) =>
        RegionPanel.IsEnabled = FullScreenCheck.IsChecked != true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.IcsUrls = IcsBox.Text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        _settings.FontFamily = FontCombo.SelectedItem as string ?? "Pretendard";
        _settings.Theme = ThemeCombo.SelectedIndex >= 0
            ? Themes.All[ThemeCombo.SelectedIndex].Name : "다크";
        _settings.Language = LangCombo.SelectedIndex == 1 ? "en" : "ko";
        if (int.TryParse(RefreshBox.Text, out var m)) _settings.RefreshMinutes = m;
        _settings.RunAtStartup = StartupCheck.IsChecked == true;
        _settings.CellOpacity = OpacitySlider.Value;
        _settings.WindowOpacity = WindowOpacitySlider.Value;
        _settings.UiScale = ScaleSlider.Value;
        _settings.WeekStartsMonday = WeekStartCombo.SelectedIndex == 1;
        _settings.MonitorIndex = MonitorCombo.SelectedIndex == MonitorCombo.Items.Count - 1
            && Screen.AllScreens.Length > 1 ? -1 : MonitorCombo.SelectedIndex;
        _settings.FullScreen = FullScreenCheck.IsChecked == true;
        _settings.SideMemoVisible = SideMemoCheck.IsChecked == true;
        if (int.TryParse(RegionW.Text, out var rw)) _settings.RegionWidth = rw;
        if (int.TryParse(RegionH.Text, out var rh)) _settings.RegionHeight = rh;

        SettingsStore.Save(_settings);
        _onSaved();
        DialogResult = true;
        Close();
    }

    /// <summary>Reset appearance options to AppSettings defaults; keeps URLs/startup/monitor/layout.</summary>
    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        var d = new AppSettings();
        FontCombo.SelectedItem = d.FontFamily;
        ThemeCombo.SelectedIndex = 0;
        RefreshBox.Text = d.RefreshMinutes.ToString();
        OpacitySlider.Value = d.CellOpacity;
        WindowOpacitySlider.Value = d.WindowOpacity;
        ScaleSlider.Value = d.UiScale;
        WeekStartCombo.SelectedIndex = d.WeekStartsMonday ? 1 : 0;
        // FullScreen/Region/모니터/시작프로그램은 건드리지 않음 — 위치가 튀는 걸 방지
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
