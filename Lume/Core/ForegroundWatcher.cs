using System.Diagnostics;
using System.Windows.Threading;

namespace VividDesk.Core;

/// <summary>
/// Детекция полноэкранного приложения на переднем плане (игры, видео).
/// Если найдено — менеджер обоев ставится на паузу (экономия GPU/CPU).
/// </summary>
public class ForegroundWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly WallpaperManager _manager;
    private readonly ConfigService _config;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private bool _lastFullscreen;

    public ForegroundWatcher(WallpaperManager manager, ConfigService config)
    {
        _manager = manager;
        _config = config;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => Tick();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Tick()
    {
        bool fullscreen = _config.Config.AutoPauseOnFullscreen && IsForegroundFullscreen();
        if (fullscreen != _lastFullscreen)
        {
            _lastFullscreen = fullscreen;
            _manager.SetFullscreenPause(fullscreen);
        }
    }

    private bool IsForegroundFullscreen()
    {
        IntPtr fg = WinApi.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        // свои окна не считаем (окно библиотеки/настроек — не «игра»)
        WinApi.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == _ownPid) return false;

        if (!WinApi.GetWindowRect(fg, out var rect)) return false;

        IntPtr mon = WinApi.MonitorFromWindow(fg, 1 /*MONITOR_DEFAULTTONEAREST*/);
        var info = new WinApi.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<WinApi.MONITORINFO>() };
        if (!WinApi.GetMonitorInfo(mon, ref info)) return false;

        // окно занимает весь монитор целиком и не является нашим приложением
        return rect.Left <= info.rcMonitor.Left && rect.Top <= info.rcMonitor.Top
            && rect.Right >= info.rcMonitor.Right && rect.Bottom >= info.rcMonitor.Bottom;
    }

    public void Dispose() => _timer.Stop();
}
