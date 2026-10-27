using System.IO;
using System.Windows.Threading;

namespace VividDesk.Core;

/// <summary>
/// Автоматическая смена обоев: по таймеру (каждые N минут) и/или по расписанию
/// (активен только в заданном интервале часов). Применяет следующий обои на все мониторы.
/// </summary>
public class PlaylistManager : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly ConfigService _config;
    private readonly LibraryService _library;
    private readonly WallpaperManager _manager;
    private DateTime _lastChange = DateTime.Now;
    private readonly Random _rnd = new();
    private string _lastItem;

    public event Action<WallpaperProject> WallpaperApplied;

    public PlaylistManager(ConfigService config, LibraryService library, WallpaperManager manager)
    {
        _config = config;
        _library = library;
        _manager = manager;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (s, e) => Tick();
    }

    public void Start() { _lastChange = DateTime.Now; _timer.Start(); }
    public void Stop() => _timer.Stop();

    /// <summary>Событие «следующие обои» — вручную из трея или автоматически.</summary>
    public void Next(bool manual = false)
    {
        var pl = _config.Config.Playlist;
        var items = pl.Items
            .Where(p => Directory.Exists(p))
            .Select(p => _library.FindByPath(p) ?? _library.LoadProject(p))
            .Where(p => p != null)
            .ToList();
        if (items.Count == 0) return;

        WallpaperProject next = null;
        if (items.Count == 1)
        {
            next = items[0];
        }
        else if (pl.Order == "random")
        {
            var pool = items.Where(i => i.FolderPath != _lastItem).ToList();
            next = pool.Count > 0 ? pool[_rnd.Next(pool.Count)] : items[0];
        }
        else
        {
            int idx = items.FindIndex(i => i.FolderPath == _lastItem);
            next = items[(idx + 1 + items.Count) % items.Count];
        }

        _lastItem = next.FolderPath;
        _lastChange = DateTime.Now;
        ApplyToAllMonitors(next);
        WallpaperApplied?.Invoke(next);
    }

    private void ApplyToAllMonitors(WallpaperProject project)
    {
        foreach (var screen in WpfScreen.AllScreens)
        {
            // настройки сохраняем по монитору: если там уже были настройки этого обои — сохраняем их
            var existing = _manager.GetMonitorConfig(screen);
            WallpaperSettings settings = null;
            if (existing != null && existing.WallpaperPath == project.FolderPath) settings = existing.Settings;
            settings ??= DefaultSettingsFor(project);
            _manager.Apply(screen, project, settings);
        }
    }

    public static WallpaperSettings DefaultSettingsFor(WallpaperProject project)
    {
        var s = new WallpaperSettings();
        foreach (var p in project.Properties)
            s.UserProperties[p.Key] = p.Default.ToSettingString();
        return s;
    }

    private void Tick()
    {
        var pl = _config.Config.Playlist;
        if (!pl.Enabled) return;

        if (pl.UseSchedule && !IsWithinSchedule(DateTime.Now)) return;

        if ((DateTime.Now - _lastChange).TotalMinutes >= Math.Max(0.2, pl.IntervalMinutes))
            Next();
    }

    private bool IsWithinSchedule(DateTime now)
    {
        try
        {
            var from = TimeSpan.Parse(_config.Config.Playlist.From);
            var to = TimeSpan.Parse(_config.Config.Playlist.To);
            var cur = now.TimeOfDay;
            return from <= to ? (cur >= from && cur < to) : (cur >= from || cur < to); // через полночь
        }
        catch { return true; }
    }

    public void Dispose() => _timer.Stop();
}
