using System.IO;
using System.Threading;
using System.Windows;
using VividDesk.Core;
using VividDesk.Views;

namespace VividDesk;

public partial class App : Application
{
    private static Mutex _mutex;
    private static bool _mutexOwned;

    public ConfigService Config { get; private set; }
    public LibraryService Library { get; private set; }
    public WallpaperManager Manager { get; private set; }
    public ForegroundWatcher Foreground { get; private set; }
    public AudioCaptureService AudioCapture { get; private set; }
    public InputBroadcaster Input { get; private set; }
    public PlaylistManager Playlist { get; private set; }
    public PresetService Presets { get; private set; }
    public TrayController Tray { get; private set; }
    public LiveLockService LiveLock { get; private set; }
    public MainWindow MainWnd { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // язык как можно раньше — он нужен даже сообщению о повторном запуске
        Strings.Set(ConfigService.PeekLanguage());

        // одиночный экземпляр
        _mutex = new Mutex(true, "Lume_SingleInstance", out _mutexOwned);
        if (!_mutexOwned)
        {
            MessageBox.Show(Strings.T("app.alreadyRunning"), "Lume", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // импорт архива без UI: Lume.exe --import "C:\path\wallpaper.zip"
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (!string.Equals(e.Args[i], "--import", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var cfgs = new ConfigService();
                var libs = new LibraryService(cfgs);
                var res = libs.ImportZip(e.Args[i + 1]);
                File.AppendAllText(Path.Combine(ConfigService.AppDataDir, "import.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " +
                    (res.Ok ? "OK " : "FAIL " + res.Problem + " ") + (res.Project?.FolderPath ?? "") + Environment.NewLine);
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(ConfigService.AppDataDir, "import.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ERR " + ex.Message + Environment.NewLine);
            }
            Shutdown();
            return;
        }

        // журнал необработанных ошибок — %APPDATA%\VividDesk\error.log
        DispatcherUnhandledException += (s, e2) =>
        {
            LogError(e2.Exception);
            e2.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e2) =>
            LogError(e2.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e2) =>
        {
            LogError(e2.Exception);
            e2.SetObserved();
        };

        bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        Config = new ConfigService();
        Strings.Set(Config.Config.Language);
        UiScale.Init(Config.Config.UiScale);

        // ярлык на рабочем столе (если ещё не создан)
        Core.DesktopShortcut.EnsureCreated();

        // первый запуск: создаём папку библиотеки и встроенные обои
        Directory.CreateDirectory(Config.Config.WallpaperDir);
        if (!Directory.EnumerateDirectories(Config.Config.WallpaperDir).Any())
            BuiltinWallpapers.EnsureCreated(Config.Config.WallpaperDir);

        Library = new LibraryService(Config);
        Library.Rescan();

        InitVlcCore();
        Manager = new WallpaperManager(Config, Library);
        Foreground = new ForegroundWatcher(Manager, Config);
        AudioCapture = new AudioCaptureService();
        Input = new InputBroadcaster(Manager);
        Playlist = new PlaylistManager(Config, Library, Manager);
        Presets = new PresetService(Config, Manager);

        // аудио-визуализатор: спектр уходит в окна обоев в UI-потоке
        AudioCapture.BandsReady += bands =>
            Dispatcher.BeginInvoke(() => Manager.BroadcastAudio(bands));

        Manager.RestoreFromConfig();
        _ = Manager.ApplyLockScreenAsync(true);
        LiveLock = new LiveLockService(this);
        LiveLock.Start();

        // если после восстановления ни одного обои — ставим первый попавшийся на основной монитор
        if (!Manager.Windows.Any() && Library.Wallpapers.Count > 0)
        {
            var primary = WpfScreen.AllScreens.First(s => s.Primary) ?? WpfScreen.AllScreens[0];
            var proj = Library.Wallpapers.FirstOrDefault(p => p.Kind != WallpaperType.Url) ?? Library.Wallpapers[0];
            Manager.Apply(primary, proj, PlaylistManager.DefaultSettingsFor(proj));
        }

        if (Config.Config.AudioCapture) AudioCapture.Start();
        Foreground.Start();
        Input.Start();
        Playlist.Start();

        Tray = new TrayController();
        Tray.ShowRequested += () => ShowMainWindow();
        Tray.ExitRequested += ExitApp;
        Tray.NextWallpaperRequested += () => Playlist.Next(manual: true);
        Tray.SettingsRequested += OpenSettingsForActiveWallpaper;
        Manager.PauseStateChanged += () => Tray.SetPausedVisual(Manager.IsPaused);

        MainWnd = new MainWindow(this);
        if (!(startMinimized || Config.Config.StartMinimized))
            ShowMainWindow();

        Library.WallpapersChanged += () =>
        {
            Dispatcher.BeginInvoke(() => MainWnd?.RefreshLibrary());
        };
    }

    /// <summary>
    /// libvlc sits in resourses\libvlc\win-x64 (kept outside the single-file exe),
    /// so point LibVLCSharp there explicitly - its default probe looks next to Lume.exe.
    /// </summary>
    private static void InitVlcCore()
    {
        try
        {
            string res = Path.Combine(AppContext.BaseDirectory, "resourses", "libvlc", "win-x64");
            if (Directory.Exists(res))
                LibVLCSharp.Shared.Core.Initialize(res);
        }
        catch (Exception ex) { LogError(ex); }
    }

    private static void LogError(Exception ex)
    {
        try
        {
            if (ex == null) return;
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(ConfigService.AppDataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }

    public void ShowMainWindow()
    {
        MainWnd.Show();
        MainWnd.WindowState = WindowState.Normal;
        MainWnd.Activate();
    }

    /// <summary>Открывает отдельное окно настройки обоев (из библиотеки, трей, двойного клика).</summary>
    public void OpenWallpaperSettings(Core.WallpaperProject project, Core.WpfScreen screen)
    {
        var w = new Views.WallpaperSettingsWindow(this, project, screen);
        if (MainWnd != null && MainWnd.IsVisible) w.Owner = MainWnd;
        // явно нормальное состояние и видимое положение — не зависим от состояния владельца
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.WindowState = WindowState.Normal;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Focus();
    }

    /// <summary>Из трея: настроить текущие обои (первый активный монитор, иначе — любые доступные).</summary>
    private void OpenSettingsForActiveWallpaper()
    {
        var win = Manager.Windows.FirstOrDefault();
        if (win?.Project != null)
        {
            var screen = Core.WpfScreen.AllScreens.FirstOrDefault(s => s.DeviceName == win.DeviceId)
                         ?? Core.WpfScreen.AllScreens[0];
            OpenWallpaperSettings(win.Project, screen);
            return;
        }
        var proj = Library.Wallpapers.FirstOrDefault();
        if (proj == null)
        {
            MainWnd ??= new MainWindow(this);
            ShowMainWindow();
            return;
        }
        OpenWallpaperSettings(proj, Core.WpfScreen.AllScreens[0]);
    }

    public void ExitApp()
    {
        Tray?.Dispose();
        Input?.Stop();
        Playlist?.Stop();
        Foreground?.Stop();
        AudioCapture?.Dispose();
        Manager?.Dispose();
        Library?.Dispose();
        Config?.Save();
        if (_mutexOwned) { try { _mutex.ReleaseMutex(); } catch { } }
        Shutdown();
    }
}
