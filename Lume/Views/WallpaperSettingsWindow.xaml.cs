using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VividDesk.Core;

namespace VividDesk.Views;

/// <summary>
/// Отдельное окно настройки выбранного обоев: превью, выбор монитора, применение,
/// пользовательские свойства и встроенные регулировки (на лету).
/// </summary>
public partial class WallpaperSettingsWindow : Window
{
    private readonly App _app;
    private readonly WallpaperProject _project;
    private readonly List<WpfScreen> _screens;
    private bool _loading;

    public WallpaperSettingsWindow(App app, WallpaperProject project, WpfScreen preferredScreen)
    {
        InitializeComponent();
        _app = app;
        _project = project;
        _screens = WpfScreen.AllScreens.ToList();

        Title = string.Format(Strings.T("ws.titleFmt"), project.Title);
        TitleText.Text = project.Title;
        TypeText.Text = project.Kind switch
        {
            WallpaperType.Video => "видео · " + project.File,
            WallpaperType.Url => "веб · " + project.Url,
            _ => "веб · " + project.File
        };

        try
        {
            if (File.Exists(project.PreviewPath))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 900;
                bi.UriSource = new Uri(project.PreviewPath);
                bi.EndInit();
                bi.Freeze();
                PreviewImg.Source = bi;
            }
        }
        catch { }

        _loading = true;
        for (int i = 0; i < _screens.Count; i++)
        {
            var s = _screens[i];
            MonitorCombo.Items.Add(string.Format(Strings.T("ws.monitorItem"), i + 1,
                s.Bounds.Width, s.Bounds.Height, s.Primary ? Strings.T("monitor.primary") : ""));
        }
        int idx = preferredScreen != null
            ? _screens.FindIndex(s => s.DeviceName == preferredScreen.DeviceName)
            : -1;
        if (idx < 0) idx = _screens.FindIndex(s => s.Primary);
        if (idx < 0) idx = 0;
        MonitorCombo.SelectedIndex = idx;
        _loading = false;

        // настройки монитора, выбранного по умолчанию
        Panel.Load(_app, _project, _screens[idx]);

        ApplyLanguage();
        Strings.LanguageChanged += ApplyLanguage;
        ApplyUiScale(UiScale.Current);
        UiScale.Changed += ApplyUiScale;
        StateChanged += (s, e) => UpdateMaximizeMargin();
        Closed += (s, e) => { Strings.LanguageChanged -= ApplyLanguage; UiScale.Changed -= ApplyUiScale; };

        SourceInitialized += (s, e) => WinApi.EnableRoundedCorners(this);
    }

    // ======================= МАСШТАБ ИНТЕРФЕЙСА =======================

    // базовые (до масштабирования) размеры окна — 520×760, минимум 460×560
    private void ApplyUiScale(double s)
    {
        WndScale.ScaleX = WndScale.ScaleY = s;
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null)
        {
            chrome.CaptionHeight = Math.Round(250 * s);
            chrome.ResizeBorderThickness = new Thickness(Math.Max(4, Math.Round(7 * s)));
        }
        Width = Math.Round(520 * s);
        Height = Math.Round(760 * s);
        MinWidth = Math.Round(460 * s);
        MinHeight = Math.Round(560 * s);
        UpdateMaximizeMargin();
    }

    private void UpdateMaximizeMargin()
    {
        var border = System.Windows.Shell.WindowChrome.GetWindowChrome(this)?.ResizeBorderThickness.Left ?? 5;
        RootPanel.Margin = WindowState == WindowState.Maximized ? new Thickness(border) : new Thickness(0);
    }

    // ======================= ЯЗЫК =======================

    private void ApplyLanguage()
    {
        Title = string.Format(Strings.T("ws.titleFmt"), _project.Title);
        BtnOpenProjectFolder.Content = Strings.T("ws.openFolder");
        BtnApply.Content = Strings.T("ws.apply");

        // пункты комбобокса мониторов
        _loading = true;
        int sel = MonitorCombo.SelectedIndex;
        for (int i = 0; i < _screens.Count && i < MonitorCombo.Items.Count; i++)
        {
            var s = _screens[i];
            MonitorCombo.Items[i] = string.Format(Strings.T("ws.monitorItem"), i + 1,
                s.Bounds.Width, s.Bounds.Height, s.Primary ? Strings.T("monitor.primary") : "");
        }
        MonitorCombo.SelectedIndex = sel;
        _loading = false;
    }

    // ======================= КНОПКИ ОКНА (безшовный заголовок) =======================

    private void BtnMinWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnCloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MonitorCombo.SelectedIndex < 0 ||
            MonitorCombo.SelectedIndex >= _screens.Count) return;
        // при смене монитора показываем его настройки этого обои
        Panel.Load(_app, _project, _screens[MonitorCombo.SelectedIndex]);
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        var screen = Panel.CurrentScreen ?? _screens[MonitorCombo.SelectedIndex];
        _app.Manager.Apply(screen, _project, Panel.CurrentSettings);
        // после применения настройки «закрепляются» за монитором — перезагружаем панель
        Panel.LoadExisting(_app, _project, screen, Panel.CurrentSettings);
    }

    private void BtnOpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_project.FolderPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }
}
