using System.IO;
using System.Text.Json;

namespace WinCal;

/// <summary>A free-floating sticky note on the widget surface (DIP coordinates).</summary>
public sealed class Sticker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 150;
    public double H { get; set; } = 90;
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#994CC2FF"; // ARGB
}

internal sealed class StickerStore
{
    private static readonly string Path_ =
        Path.Combine(SettingsStore.DataDir, "stickers.json");

    /// <summary>Sticker fill colors, cycled on creation.</summary>
    private static readonly string[] Palette =
        { "#994CC2FF", "#99FFB300", "#999CCC65", "#99F06292", "#99BA68C8", "#994DD0E1" };

    private int _nextColor;

    public List<Sticker> Items { get; private set; } = new();

    public static StickerStore Load()
    {
        var store = new StickerStore();
        try
        {
            if (File.Exists(Path_))
                store.Items = JsonSerializer.Deserialize<List<Sticker>>(
                    File.ReadAllText(Path_)) ?? new();
        }
        catch { }
        return store;
    }

    public Sticker Add(double x, double y)
    {
        var s = new Sticker { X = x, Y = y, Color = Palette[_nextColor++ % Palette.Length] };
        Items.Add(s);
        Save();
        return s;
    }

    public void Remove(Sticker s)
    {
        Items.Remove(s);
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(Items,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
