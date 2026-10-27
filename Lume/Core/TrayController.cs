using SD = System.Drawing;

namespace VividDesk.Core;

/// <summary>Иконка в системном трее (WinForms NotifyIcon) с меню: окно, пауза, следующий обои, выход.</summary>
public class TrayController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _openItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _settingsItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _nextItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _exitItem;
    private bool _paused;

    public event Action ShowRequested;
    public event Action ExitRequested;
    public event Action NextWallpaperRequested;
    public event Action SettingsRequested;

    public TrayController()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        _openItem = new System.Windows.Forms.ToolStripMenuItem(Strings.T("tray.open"), null,
            (s, e) => ShowRequested?.Invoke());
        _settingsItem = new System.Windows.Forms.ToolStripMenuItem(Strings.T("tray.settings"), null,
            (s, e) => SettingsRequested?.Invoke());
        _nextItem = new System.Windows.Forms.ToolStripMenuItem(Strings.T("tray.next"), null,
            (s, e) => NextWallpaperRequested?.Invoke());
        _exitItem = new System.Windows.Forms.ToolStripMenuItem(Strings.T("tray.exit"), null, (s, e) => ExitRequested?.Invoke());

        menu.Items.Add(_openItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_nextItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = Strings.T("tray.tooltip"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (s, e) => ShowRequested?.Invoke();

        // живое переключение языка
        Strings.LanguageChanged += ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        _openItem.Text = Strings.T("tray.open");
        _settingsItem.Text = Strings.T("tray.settings");
        _nextItem.Text = Strings.T("tray.next");
        _exitItem.Text = Strings.T("tray.exit");
        _notifyIcon.Text = _paused ? Strings.T("tray.paused") : Strings.T("tray.tooltip");
    }

    /// <summary>Обновляет подсказку у иконки (например, «на паузе» при полноэкранной игре).</summary>
    public void SetPausedVisual(bool paused)
    {
        _paused = paused;
        _notifyIcon.Text = paused ? Strings.T("tray.paused") : Strings.T("tray.tooltip");
    }

    /// <summary>
    /// Иконка трея = иконка приложения (Lume.ico, вырезана из логотипа).
    /// Раньше здесь рисовался программный градиентный круг — из-за этого
    /// иконка в трее не совпадала с иконкой exe.
    /// </summary>
    private static SD.Icon LoadAppIcon()
    {
        try
        {
            using var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/LumeTray.ico")).Stream;
            return new SD.Icon(stream);
        }
        catch
        {
            // запасной вариант: программная иконка, если ресурс недоступен
            return CreateIcon();
        }
    }

    /// <summary>Резервная иконка кодом: градиентный круг с «живой» волной.</summary>
    private static SD.Icon CreateIcon()
    {
        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SD.Drawing2D.LinearGradientBrush(new SD.Rectangle(0, 0, 32, 32),
                SD.Color.FromArgb(255, 124, 92, 255), SD.Color.FromArgb(255, 0, 200, 220), 45f);
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var wave = new SD.Pen(SD.Color.White, 2.4f);
            g.DrawBezier(wave, 6, 19, 10, 9, 14, 9, 18, 19);
            g.DrawBezier(wave, 17, 19, 20, 26, 23, 26, 27, 13);
        }
        IntPtr hIcon = bmp.GetHicon();
        return SD.Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
