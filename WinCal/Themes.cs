using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WinCal;

internal sealed class Theme
{
    public required string Name { get; init; }
    /// <summary>English display name (Name stays the stored key).</summary>
    public string NameEn { get; init; } = "";
    /// <summary>셀 배경 기본색 (알파는 설정의 CellOpacity × CellAlphaFactor)</summary>
    public required Color CellColor { get; init; }
    public double CellAlphaFactor { get; init; } = 1.0;
    public required Color TodayColor { get; init; }
    public required Color HeaderColor { get; init; }
    public double HeaderAlpha { get; init; } = 0.35;
    public required Color Text { get; init; }
    public required Color Dim { get; init; }
    public required Color Memo { get; init; }
    public required Color Sunday { get; init; }
    public required Color Saturday { get; init; }
    public required Color Accent { get; init; }
    public required Color CellBorder { get; init; }
    public required Color ButtonBg { get; init; }
    public required Color ButtonBgPressed { get; init; }
    public Color Error { get; init; } = Color.FromRgb(0xFF, 0xB0, 0xB0);

    public SolidColorBrush B(Color c) => new(c);
}

internal static class Themes
{
    public static readonly Theme[] All =
    {
        new Theme
        {
            Name = "다크", NameEn = "Dark",
            CellColor = Color.FromRgb(0x10, 0x10, 0x14),
            TodayColor = Color.FromRgb(0x1A, 0x3A, 0x4F),
            HeaderColor = Colors.Black, HeaderAlpha = 0.35,
            Text = Color.FromRgb(0xF5, 0xF5, 0xF5),
            Dim = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF),
            Memo = Color.FromRgb(0x8E, 0xE6, 0xB0),
            Sunday = Color.FromRgb(0xFF, 0x8A, 0x80),
            Saturday = Color.FromRgb(0x8A, 0xB8, 0xFF),
            Accent = Color.FromRgb(0x4C, 0xC2, 0xFF),
            CellBorder = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
            ButtonBg = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
            ButtonBgPressed = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
        },
        new Theme
        {
            Name = "라이트", NameEn = "Light",
            CellColor = Color.FromRgb(0xFF, 0xFF, 0xFF),
            TodayColor = Color.FromRgb(0xE8, 0xF0, 0xFE),
            HeaderColor = Colors.White, HeaderAlpha = 0.55,
            Text = Color.FromRgb(0x20, 0x21, 0x24),
            Dim = Color.FromArgb(0xB0, 0x40, 0x40, 0x40),
            Memo = Color.FromRgb(0x18, 0x80, 0x38),
            Sunday = Color.FromRgb(0xD9, 0x30, 0x25),
            Saturday = Color.FromRgb(0x1A, 0x73, 0xE8),
            Accent = Color.FromRgb(0x1A, 0x73, 0xE8),
            CellBorder = Color.FromArgb(0x30, 0x00, 0x00, 0x00),
            ButtonBg = Color.FromArgb(0x30, 0x00, 0x00, 0x00),
            ButtonBgPressed = Color.FromArgb(0x60, 0x00, 0x00, 0x00),
            Error = Color.FromRgb(0xD9, 0x30, 0x25),
        },
        new Theme
        {
            Name = "미니멀", NameEn = "Minimal",
            CellColor = Color.FromRgb(0x00, 0x00, 0x00), CellAlphaFactor = 0.3,
            TodayColor = Color.FromRgb(0x26, 0x32, 0x38),
            HeaderColor = Colors.Black, HeaderAlpha = 0.18,
            Text = Color.FromRgb(0xFF, 0xFF, 0xFF),
            Dim = Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
            Memo = Color.FromRgb(0xB9, 0xF6, 0xCA),
            Sunday = Color.FromRgb(0xFF, 0xAB, 0x91),
            Saturday = Color.FromRgb(0xB3, 0xE5, 0xFC),
            Accent = Color.FromRgb(0xFF, 0xFF, 0xFF),
            CellBorder = Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF),
            ButtonBg = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
            ButtonBgPressed = Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
        },
        new Theme
        {
            Name = "웜", NameEn = "Warm",
            CellColor = Color.FromRgb(0x2B, 0x21, 0x18),
            TodayColor = Color.FromRgb(0x4E, 0x34, 0x20),
            HeaderColor = Color.FromRgb(0x24, 0x1A, 0x10), HeaderAlpha = 0.45,
            Text = Color.FromRgb(0xF5, 0xED, 0xE0),
            Dim = Color.FromArgb(0x99, 0xF5, 0xED, 0xE0),
            Memo = Color.FromRgb(0xAE, 0xD5, 0x81),
            Sunday = Color.FromRgb(0xFF, 0x8A, 0x65),
            Saturday = Color.FromRgb(0x90, 0xCA, 0xF9),
            Accent = Color.FromRgb(0xFF, 0xB7, 0x4D),
            CellBorder = Color.FromArgb(0x26, 0xFF, 0xB7, 0x4D),
            ButtonBg = Color.FromArgb(0x30, 0xFF, 0xB7, 0x4D),
            ButtonBgPressed = Color.FromArgb(0x70, 0xFF, 0xB7, 0x4D),
        },
    };

    public static Theme Get(string? name) =>
        All.FirstOrDefault(t => t.Name == name) ?? All[0];
}
