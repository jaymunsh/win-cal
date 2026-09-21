using System.IO;

namespace WinCal;

internal static class DebugLog
{
    private static readonly string Path_ =
        Path.Combine(SettingsStore.DataDir, "debug.log");
    private static readonly object _lock = new();

    /// <summary>Off by default; enabled when settings.json has "DebugLog": true.</summary>
    public static bool Enabled { get; set; }

    public static void Write(string msg)
    {
        if (!Enabled) return;
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(SettingsStore.DataDir);
                File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
        }
        catch { }
    }
}
