using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WinCal;

public sealed class AppSettings
{
    /// <summary>ICS 주소 목록 (캘린더 여러 개 가능)</summary>
    public List<string> IcsUrls { get; set; } = new();

    /// <summary>레거시 단일 URL — 로드 시 IcsUrls로 이전됨</summary>
    public string IcsUrl { get; set; } = "";
    public string FontFamily { get; set; } = "Pretendard";
    public int RefreshMinutes { get; set; } = 15;
    public bool RunAtStartup { get; set; }
    public double CellOpacity { get; set; } = 0.45;

    /// <summary>0=주 모니터, 1..n=보조 모니터, -1=전체 모니터에 걸치기</summary>
    public int MonitorIndex { get; set; } = 0;

    /// <summary>UI 크기 배율 (0.5 ~ 1.5)</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>창 전체 불투명도 (0.3 ~ 1.0)</summary>
    public double WindowOpacity { get; set; } = 1.0;

    public bool WeekStartsMonday { get; set; }

    /// <summary>테마 이름 (Themes.All)</summary>
    public string Theme { get; set; } = "다크";

    /// <summary>UI 언어: "ko" (기본) 또는 "en"</summary>
    public string Language { get; set; } = "ko";

    /// <summary>true=모니터 전체, false=RegionWidth/Height/Anchor로 지정한 영역</summary>
    public bool FullScreen { get; set; } = true;

    public int RegionWidth { get; set; } = 1200;
    public int RegionHeight { get; set; } = 720;

    /// <summary>명시적 위치(px). null이면 우상단 기본 위치.</summary>
    public int? RegionLeft { get; set; }
    public int? RegionTop { get; set; }

    /// <summary>우측 고정 메모 패널 표시</summary>
    public bool SideMemoVisible { get; set; } = true;

    /// <summary>우측 메모 패널 내용</summary>
    public string SideMemoText { get; set; } = "";

    /// <summary>우측 메모 패널 너비(px)</summary>
    public int SideMemoWidth { get; set; } = 190;

    /// <summary>디버그 로그 기록 (debug.log)</summary>
    public bool DebugLogEnabled { get; set; }
}

internal static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCal");
    private static readonly string Path_ = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        AppSettings s;
        try
        {
            s = File.Exists(Path_)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path_)) ?? new AppSettings()
                : new AppSettings();
        }
        catch { s = new AppSettings(); }

        // Legacy single-URL → list migration
        if (s.IcsUrls.Count == 0 && !string.IsNullOrWhiteSpace(s.IcsUrl))
            s.IcsUrls.Add(s.IcsUrl);
        return s;
    }

    public static void Save(AppSettings settings)
    {
        settings.IcsUrl = settings.IcsUrls.FirstOrDefault() ?? "";
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }

        // Registry write only when the flag actually changed (Save is called
        // often — inline memo edits, layout locks, etc.)
        if (settings.RunAtStartup != _lastStartupApplied)
        {
            _lastStartupApplied = settings.RunAtStartup;
            ApplyStartup(settings.RunAtStartup);
        }
    }

    private static bool? _lastStartupApplied;

    private static void ApplyStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enable && Environment.ProcessPath is { } exe)
                key.SetValue("WinCal", $"\"{exe}\"");
            else
                key.DeleteValue("WinCal", throwOnMissingValue: false);
        }
        catch { }
    }

    public static string DataDir => Dir;
}
