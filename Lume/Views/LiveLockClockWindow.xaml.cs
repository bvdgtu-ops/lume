using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VividDesk.Core;

namespace VividDesk.Views;

/// <summary>
/// Часы живой заставки — отдельное прозрачное окно.
/// Нужно отдельное окно, потому что WebView2 и WindowsFormsHost всегда рисуются
/// поверх WPF-элементов (airspace), и часы внутри основного окна были бы не видны.
/// </summary>
public partial class LiveLockClockWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _shownAt = DateTime.UtcNow;

    public event Action Dismissed;

    public LiveLockClockWindow(System.Drawing.Rectangle bounds)
    {
        InitializeComponent();
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        HintText.Text = Strings.T("livelock.hint");

        UpdateClock();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateClock();
        _timer.Start();

        MouseDown += (s, e) => Dismiss();
        MouseMove += (s, e) => Dismiss();
        MouseWheel += (s, e) => Dismiss();
        PreviewKeyDown += (s, e) => Dismiss();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        var culture = System.Globalization.CultureInfo.GetCultureInfo(Strings.Lang == "en" ? "en-US" : "ru-RU");
        DateText.Text = now.ToString("dddd, d MMMM", culture);
    }

    private bool _dismissed;

    private void Dismiss()
    {
        if (_dismissed) return;
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 900) return;
        _dismissed = true;
        try { _timer.Stop(); } catch { }
        Dismissed?.Invoke();
    }
}
