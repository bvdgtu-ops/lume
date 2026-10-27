using System;
using System.Linq;
using System.Windows.Threading;
using VividDesk.Core;
using VividDesk.Views;

namespace VividDesk.Core;

/// <summary>
/// Живая заставка: по бездействию показывает анимированные обои на всех мониторах.
/// Любой ввод закрывает окно и блокирует сеанс (LockWorkStation) — пароль спрашивает
/// сама Windows, поэтому безопасность не страдает.
/// </summary>
public class LiveLockService
{
    private readonly App _app;
    private readonly DispatcherTimer _timer;
    private LiveLockWindow _window;   // контент обоев
    private LiveLockClockWindow _clock;   // часы поверх (airspace)

    public LiveLockService(App app)
    {
        _app = app;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (s, e) => Tick();
    }

    public bool IsShown => _window != null || _clock != null;

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        Hide();
    }

    private void Tick()
    {
        var cfg = _app.Config.Config;
        if (!cfg.LiveLock || cfg.LiveLockMinutes <= 0) { if (IsShown) Hide(); return; }
        if (IsShown) return;

        if (IdleWatcher.IdleSeconds() >= cfg.LiveLockMinutes * 60) Show();
    }

    public void Show()
    {
        if (IsShown) return;

        var screen = WpfScreen.AllScreens.FirstOrDefault(s => s.Primary) ?? WpfScreen.AllScreens.FirstOrDefault();
        if (screen == null) return;

        var source = _app.Manager.Windows.FirstOrDefault(w => w.DeviceId == screen.DeviceName)
                     ?? _app.Manager.Windows.FirstOrDefault();
        var project = source?.Project;
        var settings = source?.Settings;

        LogLive("show: screen=" + screen.DeviceName + " project=" + (project?.Title ?? "<none>"));
        _window = new LiveLockWindow(_app.Manager.SharedVlc, project, settings, screen.Bounds);
        _window.Dismissed += OnDismissed;
        _window.Show();

        _clock = new LiveLockClockWindow(screen.Bounds);
        _clock.Dismissed += OnDismissed;
        _clock.Show();

        _window.Activate();
    }

    public void Hide()
    {
        if (_window == null) return;
        var w = _window;
        var c = _clock;
        _window = null;
        _clock = null;
        try { if (c != null) { c.Dismissed -= OnDismissed; c.Close(); } } catch { }
        try { if (w != null) { w.Dismissed -= OnDismissed; w.Close(); } } catch { }
    }

    private void OnDismissed()
    {
        LogLive("dismissed");
        Hide();
        try
        {
            if (_app.Config.Config.LiveLockLockOnExit) LockWorkStation();
        }
        catch { }
    }

    private static void LogLive(string message)
    {
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(ConfigService.AppDataDir, "livelock.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool LockWorkStation();
}
