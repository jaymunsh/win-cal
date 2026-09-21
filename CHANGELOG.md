# Changelog

[한국어](CHANGELOG.ko.md)

## [0.1.0] - 2026-09-22

Initial release.

### Added

- Desktop-embedded monthly calendar (DesktopCal-style: above wallpaper, below icons)
  - Modern Win11 XAML-island desktop support (z-order pin above Progman + click-through)
  - Classic WorkerW attach fallback, auto-detected
- Read-only Google Calendar sync via secret iCal URLs — multiple feeds, per-calendar colors
- All-day events as colored chips; timed events with `HH:mm` labels
- Recurrence (RRULE) expansion, timezone handling, multi-day spans via Ical.Net
- Global low-level mouse hook for interaction over a fully click-through window
- Desktop icon hit-testing via UI Automation (icon clicks pass through)
- Free-floating sticker notes (double-click a cell to create, drag/resize in position mode)
- Right-side memo panel with inline editing (double-click to type, Enter saves)
- Tray-only presence: refresh, Google Calendar shortcut, add sticker, position lock, settings, exit
- Settings: ICS URLs, font, theme (Dark/Light/Minimal/Warm), language (한국어/English),
  monitor selection, refresh interval, week start day, full-screen/region mode,
  UI scale, cell/window opacity, run at startup, reset-to-defaults
- Bundled Pretendard font (SIL OFL)
- Debug logging via `settings.json` (`DebugLogEnabled`)
