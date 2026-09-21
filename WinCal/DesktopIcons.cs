using System.Windows;
using System.Windows.Automation;
using Condition = System.Windows.Automation.Condition;
using WinCal.Native;

namespace WinCal;

/// <summary>
/// Locates desktop icons via UI Automation so double-clicks on icons
/// don't get mistaken for calendar interaction.
/// </summary>
internal static class DesktopIcons
{
    private static List<Rect> _cache = new();
    private static DateTime _cacheTime = DateTime.MinValue;

    public static bool IsOnIcon(Win32.POINT pt)
    {
        foreach (var r in GetRects())
            if (pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom)
                return true;
        return false;
    }

    private static void AddRect(AutomationElement item, List<Rect> rects)
    {
        try
        {
            var r = item.Current.BoundingRectangle;
            if (!r.IsEmpty) rects.Add(r);
        }
        catch { }
    }

    private static List<Rect> GetRects()
    {
        if ((DateTime.Now - _cacheTime).TotalMilliseconds < 500)
            return _cache;

        _cacheTime = DateTime.Now;
        var rects = new List<Rect>();
        try
        {
            var desktop = AutomationElement.RootElement;

            // Classic desktop: icons are children of SysListView32.
            var listView = desktop.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "SysListView32"));
            if (listView != null)
            {
                foreach (AutomationElement item in listView.FindAll(TreeScope.Children, Condition.TrueCondition))
                    AddRect(item, rects);
            }
            else
            {
                // Modern Win11 desktop: icons are XAML list items inside the island window.
                var island = desktop.FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "XamlExplorerHostIslandWindow"));
                if (island != null)
                {
                    var items = island.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                    foreach (AutomationElement item in items)
                        AddRect(item, rects);
                }
            }
        }
        catch { }
        _cache = rects;
        return rects;
    }
}
