﻿using System.IO;
using LibVLCSharp.Shared;
using VividDesk.Wallpaper;

namespace VividDesk.Core;

/// <summary>
/// Управляет окнами обоев на всех мониторах: применяет/меняет/останавливает обои,
/// маршрутизирует паузы, ввод (мышь), аудио-данные и время суток.
/// </summary>
public class WallpaperManager : IDisposable
{
    private readonly ConfigService _config;
    private readonly LibraryService _library;
    private readonly Dictionary<string, WallpaperWindow> _windows = new(); // deviceId -> окно
    private LibVLC _libVlc;

    public IEnumerable<WallpaperWindow> Windows => _windows.Values;
    public LibVLC SharedVlc => _libVlc;

    public WallpaperManager(ConfigService config, LibraryService library)
    {
        _config = config;
        _library = library;
        _libVlc = new LibVLC("--no-osd", "--no-video-title-show", "--quiet", "--avcodec-hw=any");
    }

    /// <summary>Восстановление обоев из конфига при старте.</summary>
    public void RestoreFromConfig()
    {
        foreach (var screen in WpfScreen.AllScreens)
        {
            var mc = _config.Config.Monitors.FirstOrDefault(m => m.DeviceId == screen.DeviceName)
                     ?? (_config.Config.Monitors.Count == 1 && WpfScreen.AllScreens.Count > 1 && screen.Primary
                         ? _config.Config.Monitors[0]
                         : null);
            if (mc == null || string.IsNullOrEmpty(mc.WallpaperPath)) continue;
            var proj = _library.FindByPath(mc.WallpaperPath);
            if (proj == null)
            {
                // обои могли быть добавлены в обход библиотеки — пробуем загрузить напрямую
                if (Directory.Exists(mc.WallpaperPath)) proj = _library.LoadProject(mc.WallpaperPath);
            }
            if (proj != null) Apply(screen, proj, mc.Settings);
        }
    }

    /// <summary>Применяет обои на конкретный монитор (создаёт/переиспользует окно-хост).</summary>
    public void Apply(WpfScreen screen, WallpaperProject project, WallpaperSettings settings)
    {
        string id = screen.DeviceName;

        if (_windows.TryGetValue(id, out var existing))
        {
            if (string.Equals(existing.Project?.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase))
            {
                existing.ApplySettings(settings); // тот же обои — только обновляем настройки на лету
                SaveMonitorConfig(screen, project, settings);
                return;
            }
            existing.CloseWallpaper();
            _windows.Remove(id);
        }

        var win = new WallpaperWindow(id, screen.Bounds, _libVlc, _config.Config);
        _windows[id] = win;
        win.Load(project, settings);
        SaveMonitorConfig(screen, project, settings);
        _config.Save();
        _ = ApplyLockScreenAsync();
    }

    /// <summary>Снимает обои с монитора (возврат к статичным).</summary>
    public void Clear(WpfScreen screen)
    {
        if (_windows.Remove(screen.DeviceName, out var win))
        {
            win.CloseWallpaper();
            var mc = _config.Config.Monitors.FirstOrDefault(m => m.DeviceId == screen.DeviceName);
            if (mc != null) { mc.WallpaperPath = ""; _config.Save(); }
        }
    }

    // экран блокировки: один кадр на систему, берём основной монитор
    private readonly System.Threading.SemaphoreSlim _lockScreenGate = new(1, 1);
    private DateTime _lockScreenAt = DateTime.MinValue;

    /// <summary>Ставит кадр обоев основного монитора на экран блокировки (если включено в настройках).</summary>
    public async Task ApplyLockScreenAsync(bool force = false)
    {
        if (!_config.Config.LockScreen) return;
        if (!await _lockScreenGate.WaitAsync(0)) return;      // предыдущий кадр ещё снимается
        try
        {
            if (!force && (DateTime.UtcNow - _lockScreenAt).TotalSeconds < 5) return;

            var screen = WpfScreen.AllScreens.FirstOrDefault(s => s.Primary) ?? WpfScreen.AllScreens.FirstOrDefault();
            if (screen == null) return;
            if (!_windows.TryGetValue(screen.DeviceName, out var win) || win?.Project == null) return;

            string png = LockScreenService.ImagePath;
            // окно только что создано: WebView2/libvlc готовы не сразу — пробуем несколько раз
            bool captured = false;
            for (int attempt = 0; attempt < 6 && !captured; attempt++)
            {
                if (attempt > 0) await Task.Delay(1200);
                captured = await win.CaptureFrameAsync(png, screen.Bounds.Width, screen.Bounds.Height);
            }
            if (!captured) return;

            if (await LockScreenService.ApplyAsync(png)) _lockScreenAt = DateTime.UtcNow;
        }
        catch { }
        finally { _lockScreenGate.Release(); }
    }

    private void SaveMonitorConfig(WpfScreen screen, WallpaperProject project, WallpaperSettings settings)
    {
        var mc = _config.Config.Monitors.FirstOrDefault(m => m.DeviceId == screen.DeviceName);
        if (mc == null)
        {
            mc = new MonitorConfig { DeviceId = screen.DeviceName };
            _config.Config.Monitors.Add(mc);
        }
        mc.WallpaperPath = project.FolderPath;
        mc.Settings = settings;
    }

    public MonitorConfig GetMonitorConfig(WpfScreen screen) =>
        _config.Config.Monitors.FirstOrDefault(m => m.DeviceId == screen.DeviceName);

    public WallpaperWindow FindWindowFor(WallpaperProject project) =>
        _windows.Values.FirstOrDefault(w => string.Equals(w.Project?.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase));

    // ======================= ПАУЗА =======================

    private bool _manualPause;
    private bool _fullscreenPause;

    public event Action PauseStateChanged;

    public bool IsPaused => _manualPause || _fullscreenPause;

    public void SetManualPause(bool paused)
    {
        _manualPause = paused;
        ApplyPause();
    }

    public void SetFullscreenPause(bool fullscreen)
    {
        _fullscreenPause = fullscreen;
        ApplyPause();
    }

    private void ApplyPause()
    {
        foreach (var w in _windows.Values.ToList())
        {
            if (IsPaused) w.PauseRendering();
            else w.ResumeRendering();
        }
        PauseStateChanged?.Invoke();
    }

    public void TogglePause() => SetManualPause(!_manualPause);

    // ======================= РАССЫЛКА ДАННЫХ =======================

    public void BroadcastMouseMove(int x, int y)
    {
        foreach (var w in _windows.Values) w.SendMouseMove(x, y);
    }

    public void BroadcastTime()
    {
        foreach (var w in _windows.Values) w.SendTime();
    }

    public void BroadcastAudio(float[] bands)
    {
        foreach (var w in _windows.Values) w.SendAudio(bands);
    }

    public void ApplyWallpaperScaleEverywhere()
    {
        foreach (var w in _windows.Values) w.ApplyWallpaperScale();
    }

    public void ApplySoundStateEverywhere()
    {
        foreach (var w in _windows.Values) w.ApplySoundState();
    }

    /// <summary>Проект удалён из библиотеки — закрываем окна с ним.</summary>
    public void OnProjectDeleted(WallpaperProject project)
    {
        foreach (var kv in _windows.ToList())
        {
            if (string.Equals(kv.Value.Project?.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value.CloseWallpaper();
                _windows.Remove(kv.Key);
                var mc = _config.Config.Monitors.FirstOrDefault(m => m.WallpaperPath == project.FolderPath);
                if (mc != null) { mc.WallpaperPath = ""; }
            }
        }
        _config.Save();
    }

    public void Dispose()
    {
        foreach (var w in _windows.Values.ToList()) { try { w.CloseWallpaper(); } catch { } }
        _windows.Clear();
        try { _libVlc?.Dispose(); } catch { }
    }
}

/// <summary>Обёртка над WinForms Screen для WPF-кода.</summary>
public class WpfScreen
{
    public string DeviceName { get; init; }
    public string FriendlyName { get; init; }
    public System.Drawing.Rectangle Bounds { get; init; }
    public bool Primary { get; init; }

    public static IReadOnlyList<WpfScreen> AllScreens =>
        System.Windows.Forms.Screen.AllScreens
            .Select(s => new WpfScreen
            {
                DeviceName = s.DeviceName,
                FriendlyName = s.DeviceName,
                Bounds = s.Bounds,
                Primary = s.Primary
            })
            .ToList();
}
