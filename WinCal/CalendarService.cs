using System.IO;
using System.Net.Http;
using System.Text.Json;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;

namespace WinCal;

public sealed record EventItem(string Title, bool AllDay, string? TimeText, int CalendarIndex);

/// <summary>
/// Downloads and parses one or more iCalendar (.ics) feeds and expands
/// occurrences (including recurrence rules) for display in the month grid.
/// </summary>
internal sealed class CalendarService
{
    private static readonly string CachePath =
        Path.Combine(SettingsStore.DataDir, "cache.json");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly List<Calendar> _calendars = new();
    private readonly List<string?> _colors = new();

    // GetEvents cache: recurrence expansion is the expensive part of rendering.
    private int _version;
    private int _cacheVersion = -1;
    private DateOnly _cacheFrom, _cacheTo;
    private Dictionary<DateOnly, List<EventItem>>? _cache;

    /// <summary>Per-calendar hex color from COLOR/X-WR-CALCOLOR if present, else null.</summary>
    public IReadOnlyList<string?> Colors => _colors;

    public string? LastError { get; private set; }
    public DateTimeOffset? LastFetched { get; private set; }
    public bool IsSyncing { get; private set; }

    public async Task<bool> RefreshAsync(IReadOnlyList<string> icsUrls, CancellationToken ct = default)
    {
        var urls = icsUrls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0)
        {
            LastError = Loc.T("no_ics");
            return false;
        }

        IsSyncing = true;
        var parsed = new List<Calendar>();
        var rawTexts = new List<string>();
        var errors = new List<string>();
        try
        {
            foreach (var url in urls)
            {
                try
                {
                    var text = await Http.GetStringAsync(url.Trim(), ct);
                    if (Calendar.Load(text) is { } c)
                        parsed.Add(c);
                    rawTexts.Add(text);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            _calendars.Clear();
            _calendars.AddRange(parsed);
            _colors.Clear();
            _colors.AddRange(parsed.Select(ExtractColor));
            _version++;
            LastError = errors.Count > 0 ? string.Join(" | ", errors) : null;
            if (parsed.Count > 0)
            {
                LastFetched = DateTimeOffset.Now;
                try
                {
                    Directory.CreateDirectory(SettingsStore.DataDir);
                    await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(rawTexts), ct);
                }
                catch { }
            }
            return errors.Count == 0;
        }
        finally { IsSyncing = false; }
    }

    /// <summary>Loads the last downloaded feeds from disk (offline / pre-settings).</summary>
    public void LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var texts = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CachePath));
            if (texts == null) return;
            _calendars.Clear();
            _colors.Clear();
            foreach (var t in texts)
            {
                try
                {
                    if (Calendar.Load(t) is { } c)
                    {
                        _calendars.Add(c);
                        _colors.Add(ExtractColor(c));
                    }
                }
                catch { }
            }
            _version++;
        }
        catch { }
    }

    /// <summary>Google export can carry X-WR-CALCOLOR; RFC 7986 uses COLOR.</summary>
    private static string? ExtractColor(Calendar cal) =>
        cal.Properties["X-WR-CALCOLOR"]?.Value?.ToString()
        ?? cal.Properties["COLOR"]?.Value?.ToString();

    /// <summary>Returns events grouped by local calendar date for [from, to].
    /// Cached by (range, data version) — recurrence expansion only runs when
    /// the month or the feeds actually change.</summary>
    public Dictionary<DateOnly, List<EventItem>> GetEvents(DateOnly from, DateOnly to)
    {
        if (_cache != null && _cacheVersion == _version && _cacheFrom == from && _cacheTo == to)
            return _cache;

        var result = new Dictionary<DateOnly, List<EventItem>>();
        var rangeStart = new CalDateTime(from.ToDateTime(TimeOnly.MinValue), hasTime: true);
        var rangeEnd = new CalDateTime(to.ToDateTime(TimeOnly.MinValue).AddDays(1), hasTime: true);

        for (int ci = 0; ci < _calendars.Count; ci++)
        {
            foreach (var ev in _calendars[ci].Events.OfType<CalendarEvent>())
            {
                if (ev.DtStart == null) continue;
                var duration = ev.DtEnd != null
                    ? ev.DtEnd.Value - ev.DtStart.Value
                    : (ev.IsAllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));

                List<Occurrence> occs;
                try
                {
                    occs = ev.GetOccurrences(rangeStart, new EvaluationOptions())
                             .TakeWhileBefore(rangeEnd)
                             .ToList();
                }
                catch { continue; }

                foreach (var occ in occs)
                {
                    var st = occ.Period.StartTime;
                    if (st == null) continue;

                    DateTime localStart = st.HasTime ? st.AsUtc.ToLocalTime() : st.Value;
                    var occEnd = occ.Period.EndTime;
                    DateTime localEnd = occEnd != null
                        ? (occEnd.HasTime ? occEnd.AsUtc.ToLocalTime() : occEnd.Value)
                        : localStart + duration;

                    var item = new EventItem(
                        ev.Summary ?? Loc.T("no_title"),
                        ev.IsAllDay || !st.HasTime,
                        st.HasTime ? localStart.ToString("HH:mm") : null,
                        ci);

                    // All-day DTEND is exclusive; expand multi-day spans per date.
                    var firstDay = DateOnly.FromDateTime(localStart);
                    var lastDay = localEnd <= localStart
                        ? firstDay
                        : DateOnly.FromDateTime(localEnd.AddTicks(-1));

                    for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
                    {
                        if (d < from || d > to) continue;
                        if (!result.TryGetValue(d, out var list))
                            result[d] = list = new List<EventItem>();
                        list.Add(item);
                    }
                }
            }
        }

        foreach (var list in result.Values)
            list.Sort((a, b) => a.AllDay != b.AllDay
                ? (a.AllDay ? -1 : 1)
                : string.Compare(a.TimeText, b.TimeText, StringComparison.Ordinal));

        _cache = result;
        _cacheVersion = _version;
        _cacheFrom = from;
        _cacheTo = to;
        return result;
    }
}
