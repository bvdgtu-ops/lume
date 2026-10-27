using System.Windows.Threading;

namespace VividDesk.Core;

/// <summary>
/// Рассылает в активные веб-обои позицию курсора (глобальный GetCursorPos) и время суток.
/// Мышь отправляется только при реальном перемещении, время — раз в 30 секунд.
/// </summary>
public class InputBroadcaster : IDisposable
{
    private readonly DispatcherTimer _mouseTimer;
    private readonly DispatcherTimer _timeTimer;
    private readonly WallpaperManager _manager;
    private WinApi.POINT _lastPoint;

    public InputBroadcaster(WallpaperManager manager)
    {
        _manager = manager;

        _mouseTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _mouseTimer.Tick += (s, e) =>
        {
            if (!WinApi.GetCursorPos(out var p)) return;
            if (p.X == _lastPoint.X && p.Y == _lastPoint.Y) return; // курсор неподвижен
            _lastPoint = p;
            _manager.BroadcastMouseMove(p.X, p.Y);
        };

        _timeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timeTimer.Tick += (s, e) => _manager.BroadcastTime();
    }

    public void Start()
    {
        _mouseTimer.Start();
        _timeTimer.Start();
        _manager.BroadcastTime();
    }

    public void Stop() { _mouseTimer.Stop(); _timeTimer.Stop(); }

    public void Dispose() => Stop();
}
