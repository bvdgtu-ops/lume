using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VividDesk.Core;

namespace VividDesk.Views;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private AppConfig Cfg => _app.Config.Config;
    private bool _loading;
    private System.Windows.Threading.DispatcherTimer _scaleTimer;

    public SettingsWindow(App app)
    {
        _loading = true;
        InitializeComponent();
        _app = app;

        ChkSound.IsChecked = Cfg.SoundEnabled;
        ChkAutoPause.IsChecked = Cfg.AutoPauseOnFullscreen;
        ChkAudioCapture.IsChecked = Cfg.AudioCapture;
        ChkAutoStart.IsChecked = Cfg.StartWithSystem;
        ChkStartMinimized.IsChecked = Cfg.StartMinimized;
        ChkLockScreen.IsChecked = Cfg.LockScreen;
        ChkLiveLock.IsChecked = Cfg.LiveLock;
        TxtLiveLockMin.Text = Cfg.LiveLockMinutes.ToString();
        TxtDir.Text = Cfg.WallpaperDir;

        CmbPauseAction.SelectedIndex = Cfg.PauseAction == "stop" ? 1 : 0;

        ChkPlaylist.IsChecked = Cfg.Playlist.Enabled;
        TxtInterval.Text = Cfg.Playlist.IntervalMinutes.ToString();
        CmbOrder.SelectedIndex = Cfg.Playlist.Order == "random" ? 1 : 0;
        ChkSchedule.IsChecked = Cfg.Playlist.UseSchedule;
        TxtFrom.Text = Cfg.Playlist.From;
        TxtTo.Text = Cfg.Playlist.To;

        _loading = true;
        CmbLanguage.SelectedIndex = Cfg.Language == "en" ? 1 : 0;
        SldUiScale.Value = UiScale.Current * 100;
        SldWallScale.Value = Math.Clamp(Cfg.WallpaperScale, 0.5, 1.5) * 100;
        LblWallScaleValue.Text = Math.Round(SldWallScale.Value) + "%";
        LblScaleValue.Text = Math.Round(UiScale.Current * 100) + "%";
        // применение масштаба с задержкой: слайдер шлёт много значений при перетаскивании
        _scaleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _scaleTimer.Tick += (s, a) =>
        {
            _scaleTimer.Stop();
            var v = UiScale.Clamp(SldUiScale.Value / 100.0);
            if (Math.Abs(v - UiScale.Current) < 0.001) return;
            Cfg.UiScale = v;
            _app.Config.Save();
            UiScale.Set(v);
        };
        _loading = false;

        SourceInitialized += (s, e) => WinApi.EnableRoundedCorners(this);

        BuildPlaylistChecklist();

        ApplyLanguage();
        Strings.LanguageChanged += ApplyLanguage;
        ApplyUiScale(UiScale.Current);
        UiScale.Changed += ApplyUiScale;
        Closed += (s, e) => { Strings.LanguageChanged -= ApplyLanguage; UiScale.Changed -= ApplyUiScale; };
    }

    // ======================= ЯЗЫК =======================

    private void ApplyLanguage()
    {
        Title = Strings.T("settings.title");
        HeaderText.Text = Strings.T("settings.title");
        LblHeader.Text = Strings.T("settings.header");
        LblSound.Text = Strings.T("section.sound");
        ChkSound.Content = Strings.T("chk.sound");
        LblBehavior.Text = Strings.T("section.behavior");
        ChkAutoPause.Content = Strings.T("chk.autoPause");
        LblWhenPaused.Text = Strings.T("pause.label");
        ItmPauseFreeze.Content = Strings.T("pause.freeze");
        ItmPauseStop.Content = Strings.T("pause.stop");
        ChkAudioCapture.Content = Strings.T("chk.audioCapture");
        LblSystem.Text = Strings.T("section.system");
        ChkAutoStart.Content = Strings.T("chk.autoStart");
        ChkStartMinimized.Content = Strings.T("chk.startMinimized");
        ChkLockScreen.Content = Strings.T("chk.lockScreen");
        ChkLiveLock.Content = Strings.T("chk.liveLock");
        LblLiveLockMin.Text = Strings.T("livelock.minutes");
        LblLanguage.Text = Strings.T("language.label") + " / Language:";
        LblUiScale.Text = Strings.T("uiscale.label");
        LblWallScale.Text = Strings.T("wscale.label");
        LblDir.Text = Strings.T("section.dir");
        BtnChangeDir.Content = Strings.T("dir.change");
        BtnOpenDir.Content = Strings.T("dir.open");
        LblPlaylist.Text = Strings.T("playlist.header");
        ChkPlaylist.Content = Strings.T("chk.playlist");
        LblEvery.Text = Strings.T("playlist.every");
        LblMin.Text = Strings.T("playlist.min");
        LblOrder.Text = Strings.T("playlist.order");
        ItmOrderSeq.Content = Strings.T("order.sequential");
        ItmOrderRnd.Content = Strings.T("order.random");
        ChkSchedule.Content = Strings.T("chk.schedule");
        LblFrom.Text = Strings.T("schedule.from");
        LblTo.Text = Strings.T("schedule.to");
        LblHint.Text = Strings.T("schedule.hint");
        LblPlaylistItems.Text = Strings.T("playlist.items");
        BtnSave.Content = Strings.T("btn.save");
        BtnCancel.Content = Strings.T("btn.cancel");
        BuildPlaylistChecklist();
    }

    /// <summary>Смена языка применяется на лету и сохраняется сразу (не зависит от «Сохранить»).</summary>
    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var lang = (CmbLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? "ru";
        Strings.Set(lang);
        Cfg.Language = lang;
        _app.Config.Save();
    }

    // ======================= МАСШТАБ ИНТЕРФЕЙСА =======================

    private System.Windows.Threading.DispatcherTimer _wallTimer;

    private void SldWallScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblWallScaleValue != null) LblWallScaleValue.Text = Math.Round(e.NewValue) + "%";
        if (_loading) return;
        if (_wallTimer == null)
        {
            _wallTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _wallTimer.Tick += (s, a) =>
            {
                _wallTimer.Stop();
                Cfg.WallpaperScale = Math.Clamp(SldWallScale.Value / 100.0, 0.5, 1.5);
                _app.Config.Save();
                _app.Manager.ApplyWallpaperScaleEverywhere();
            };
        }
        _wallTimer.Stop();
        _wallTimer.Start();
    }

    private void SldUiScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblScaleValue != null) LblScaleValue.Text = Math.Round(e.NewValue) + "%";
        if (_loading || _scaleTimer == null) return;
        _scaleTimer.Stop();
        _scaleTimer.Start();
    }

    /// <summary>Базовый (до масштабирования) размер окна настроек — 560×640.</summary>
    private void ApplyUiScale(double s)
    {
        WndScale.ScaleX = WndScale.ScaleY = s;
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CaptionHeight = Math.Round(60 * s);
        Width = Math.Round(560 * s);
        Height = Math.Round(640 * s);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BuildPlaylistChecklist()
    {
        PlaylistPanel.Children.Clear();
        foreach (var w in _app.Library.Wallpapers)
        {
            bool check = Cfg.Playlist.Items.Contains(w.FolderPath);
            var cb = new CheckBox
            {
                Content = w.Title,
                IsChecked = check,
                Tag = w.FolderPath,
                Margin = new Thickness(2, 3, 2, 3)
            };
            PlaylistPanel.Children.Add(cb);
        }
        if (PlaylistPanel.Children.Count == 0)
            PlaylistPanel.Children.Add(new TextBlock { Text = Strings.T("playlist.empty"), Opacity = 0.5 });
    }

    private void BtnChangeDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Strings.T("dir.pick"),
            SelectedPath = Cfg.WallpaperDir,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog(this.GetWin32Window()) == System.Windows.Forms.DialogResult.OK)
            TxtDir.Text = dlg.SelectedPath;
    }

    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Directory.Exists(TxtDir.Text) ? TxtDir.Text : Cfg.WallpaperDir;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string oldDir = Cfg.WallpaperDir;

        Cfg.SoundEnabled = ChkSound.IsChecked == true;
        Cfg.AutoPauseOnFullscreen = ChkAutoPause.IsChecked == true;
        Cfg.AudioCapture = ChkAudioCapture.IsChecked == true;
        Cfg.StartWithSystem = ChkAutoStart.IsChecked == true;
        Cfg.StartMinimized = ChkStartMinimized.IsChecked == true;
        Cfg.WallpaperScale = Math.Clamp(SldWallScale.Value / 100.0, 0.5, 1.5);
        Cfg.LockScreen = ChkLockScreen.IsChecked == true;
        Cfg.LiveLock = ChkLiveLock.IsChecked == true;
        if (int.TryParse(TxtLiveLockMin.Text.Trim(), out int lm) && lm > 0) Cfg.LiveLockMinutes = lm;
        Cfg.PauseAction = (CmbPauseAction.SelectedItem as ComboBoxItem)?.Tag as string ?? "pause";

        Cfg.Playlist.Enabled = ChkPlaylist.IsChecked == true;
        Cfg.Playlist.Order = (CmbOrder.SelectedItem as ComboBoxItem)?.Tag as string ?? "sequential";
        Cfg.Playlist.UseSchedule = ChkSchedule.IsChecked == true;
        Cfg.Playlist.From = TxtFrom.Text.Trim();
        Cfg.Playlist.To = TxtTo.Text.Trim();
        if (int.TryParse(TxtInterval.Text.Trim(), out int interval) && interval > 0)
            Cfg.Playlist.IntervalMinutes = interval;

        Cfg.Playlist.Items = PlaylistPanel.Children
            .OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true)
            .Select(cb => (string)cb.Tag)
            .ToList();

        // смена папки библиотеки
        if (!string.Equals(TxtDir.Text.Trim(), oldDir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(TxtDir.Text.Trim()))
        {
            Cfg.WallpaperDir = TxtDir.Text.Trim();
            _app.Library.RescanAfterDirChange();
        }

        _app.Config.Save();

        // сайд-эффекты
        try { AutoStartHelper.Set(Cfg.StartWithSystem); } catch { }
        _app.Manager.ApplySoundStateEverywhere();
        _ = _app.Manager.ApplyLockScreenAsync(true);
        if (Cfg.AudioCapture) _app.AudioCapture.Start();
        else _app.AudioCapture.Stop();

        Close();
    }
}

internal static class WindowInteropExtensions
{
    private class Win32Window : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Window(IntPtr handle) => Handle = handle;
    }

    public static System.Windows.Forms.IWin32Window GetWin32Window(this Window w) =>
        new Win32Window(new System.Windows.Interop.WindowInteropHelper(w).Handle);
}
