using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VividDesk.Core;

namespace VividDesk.Wallpaper;

/// <summary>
/// Безрамочное окно, прикрепляемое к WorkerW за иконками рабочего стола.
/// Внутри — либо LibVLC VideoView (видео-обои), либо WebView2 (веб/URL/интерактивные обои).
/// </summary>
public partial class WallpaperWindow : Window
{
    private readonly string _deviceId;
    private readonly System.Drawing.Rectangle _bounds; // физические пиксели монитора

    private LibVLC _libVlc;
    private MediaPlayer _player;
    private System.Windows.Forms.Panel _videoPanel;
    private WallpaperProject _project;
    private WallpaperSettings _settings;
    private AppConfig _app;

    private WebView2 _webView;
    private bool _webReady;
    private bool _paused;
    private DispatcherTimer _fpsTimer;   // кадровое окно для лимита FPS у видео
    private DateTime _fpsPlayUntil;
    private IntPtr _vlcShadowWnd;        // скрытое окно «VLC video main» (уничтожаем в TearDown)

    public string DeviceId => _deviceId;
    public WallpaperProject Project => _project;
    public WallpaperSettings Settings => _settings;
    public bool IsWeb => _project != null && _project.Kind != WallpaperType.Video;

    public event Action<string, WallpaperProject> WallpaperChanged;

    public WallpaperWindow(string deviceId, System.Drawing.Rectangle bounds, LibVLC sharedVlc, AppConfig app)
    {
        InitializeComponent();
        _deviceId = deviceId;
        _bounds = bounds;
        _libVlc = sharedVlc;
        _app = app;

        // Показываем окно и сразу уводим его в слой рабочего стола за иконками
        Show();
        AttachToDesktop();
    }

    private void AttachToDesktop()
    {
        var layer = DesktopLayer.Get();
        if (layer == IntPtr.Zero) return;

        var hwnd = (HwndSource)PresentationSource.FromVisual(this);
        int style = WinApi.GetWindowLong(hwnd.Handle, WinApi.GWL_STYLE);
        style = (int)((style & ~(WinApi.WS_CAPTION | WinApi.WS_THICKFRAME)) | WinApi.WS_CHILD | WinApi.WS_VISIBLE);
        WinApi.SetWindowLong(hwnd.Handle, WinApi.GWL_STYLE, style);
        WinApi.SetParent(hwnd.Handle, layer);
        WinApi.SetWindowPos(hwnd.Handle, IntPtr.Zero,
            _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height,
            WinApi.SWP_NOZORDER | WinApi.SWP_FRAMECHANGED | WinApi.SWP_SHOWWINDOW);
    }

    // ======================= ЗАГРУЗКА ОБОЕВ =======================

    public void Load(WallpaperProject project, WallpaperSettings settings)
    {
        TearDownContent();
        _project = project;
        _settings = settings;
        _paused = false;

        if (!IsPlayable(project)) { ShowUnsupported(); return; }
        if (project.Kind == WallpaperType.Video) LoadVideo(project);
        else if (project.Kind == WallpaperType.Image) LoadImage(project);
        else LoadWeb(project);

        WallpaperChanged?.Invoke(_deviceId, project);
    }

    private System.Windows.Controls.Image _image;

    /// <summary>Статичная картинка как обои (jpg/png/webp/bmp).</summary>
    private void LoadImage(WallpaperProject project)
    {
        try
        {
            string path = Path.Combine(project.FolderPath, project.File);
            if (!File.Exists(path)) { ShowUnsupported(); return; }

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();

            _image = new System.Windows.Controls.Image
            {
                Source = bmp,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (Content is System.Windows.Controls.Grid root) root.Children.Add(_image);
            ApplyImageScale();
        }
        catch (Exception ex)
        {
            LogWallpaper("картинка не открылась: " + ex.Message);
            ShowUnsupported();
        }
    }

    /// <summary>Масштаб картинки-обоев (50..150%).</summary>
    private void ApplyImageScale()
    {
        try
        {
            if (_image == null) return;
            double s = WallpaperScaleValue;
            _image.RenderTransformOrigin = new Point(0.5, 0.5);
            _image.RenderTransform = new System.Windows.Media.ScaleTransform(s, s);
        }
        catch { }
    }

    private void LoadVideo(WallpaperProject project)
    {
        VideoHost.Visibility = Visibility.Visible;
        _videoPanel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        VideoHost.Child = _videoPanel;

        _player = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = true, // аппаратное ускорение (D3D11)
            Scale = 0f,
        };
        // явный HWND панели: без него libvlc создаёт собственное окно «VLC (Direct3D11
        // output)» поверх рабочего стола, когда окно обоев прикреплено к WorkerW
        _player.Hwnd = _videoPanel.Handle;
        _player.Scale = (float)WallpaperScaleValue;

        string path = Path.Combine(project.FolderPath, project.File);
        var media = new Media(_libVlc, path, FromType.FromPath);
        media.AddOption(":input-repeat=65535"); // бесконечный цикл
        _player.Play(media);

        // Служебное «event-окно» libvlc «VLC video main» существует, пока играет
        // видео, и является настоящим top-level (parent=0). Манипуляции с ним на
        // живом рендере (SetParent, стиль, мгновенный WM_CLOSE) роняют процесс
        // нативно — vout держит в нём message-pump. Окно существует скрытым в
        // единственном экземпляре; TearDown после остановки плеера закрывает его
        // отложенным WM_CLOSE (отложить важно: сразу после Dispose окно ещё
        // разбирается потоком VLC, и Close в этот момент роняет процесс).
        // Таск привязан к поколению плеера: при смене обоев в цикле старый таск
        // не должен захватить окно нового поколения.
        System.Threading.Tasks.Task.Run(async () =>
        {
            var player = _player;
            for (int attempt = 0; attempt < 5 && _vlcShadowWnd == IntPtr.Zero; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(attempt == 0 ? 400 : 600);
                if (_player == null || !ReferenceEquals(_player, player)) return; // поколение сменилось
                IntPtr found = IntPtr.Zero;
                WinApi.EnumWindows((h, _) =>
                {
                    var cls = new System.Text.StringBuilder(256);
                    WinApi.GetClassName(h, cls, 256);
                    if (!cls.ToString().Equals("VLC video main", StringComparison.Ordinal)) return true;
                    WinApi.GetWindowThreadProcessId(h, out uint pid);
                    if (pid == Environment.ProcessId) { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero && ReferenceEquals(_player, player))
                    _vlcShadowWnd = found;
            }
        });

        ApplyVideoSettings();
    }

    private async void LoadWeb(WallpaperProject project)
    {
        WebHost.Visibility = Visibility.Visible;
        _webView = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Black,
        };
        WebHost.Child = _webView;

        // среда создаётся явно, с запасными папками: если папка данных заблокирована
        // осиротевшими процессами браузера, инициализация раньше молча давала чёрное окно
        string baseFolder = Path.Combine(ConfigService.AppDataDir, "WebView2", SafeDeviceId(_deviceId));
        CoreWebView2Environment env = null;
        for (int attempt = 1; attempt <= 3 && env == null; attempt++)
        {
            string folder = attempt == 1 ? baseFolder : baseFolder + "_" + attempt;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, folder,
                    new CoreWebView2EnvironmentOptions { Language = "ru-RU" });
            }
            catch (Exception ex) { LogWallpaper("среда не создана (" + folder + "): " + ex.Message); }
        }
        if (env == null)
        {
            LogWallpaper("веб-обои: не удалось создать среду ни с одной папкой данных");
            return;
        }

        // EnsureCoreWebView2Async падает COMException 0x8007139F при быстром
        // переключении обоев (контроллер ещё доигрывает закрытие) — ретраи с паузой
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async(env);
                break;
            }
            catch (Exception ex)
            {
                LogWallpaper($"инициализация веб-обоев (попытка {attempt}/5): {ex.Message}");
                if (attempt == 5)
                {
                    LogWallpaper("веб-обои: инициализация не удалась после 5 попыток");
                    return;
                }
                // пересоздание контрола: повторный вызов на том же после сбоя бесполезен
                _webView.Dispose();
                _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
                WebHost.Child = _webView;
                await Task.Delay(attempt * 300);
            }
        }

        var cwv = _webView.CoreWebView2;
        cwv.Settings.AreDefaultContextMenusEnabled = false;
        cwv.Settings.AreDevToolsEnabled = true;
        cwv.Settings.IsZoomControlEnabled = false;
        cwv.Settings.AreBrowserAcceleratorKeysEnabled = false;
        cwv.NewWindowRequested += (s2, e2) => { e2.Handled = true; e2.NewWindow = cwv; };

        await cwv.AddScriptToExecuteOnDocumentCreatedAsync(WebApiScripts.ApiScript);
        cwv.WebMessageReceived += OnWebMessage;
        _webReady = true;

        _webView.ZoomFactor = WallpaperScaleValue;
        _webView.NavigationCompleted += (s, e) => PushAllStateToPage();
        if (project.Kind == WallpaperType.Url)
            cwv.Navigate(project.Url);
        else
            cwv.Navigate(new Uri(Path.Combine(project.FolderPath, project.File)).AbsoluteUri);
    }

    /// <summary>Можно ли проиграть эти обои: поддерживаемый тип и реально существующий файл.</summary>
    private static bool IsPlayable(WallpaperProject project)
    {
        if (project == null) return false;
        if (project.Kind == WallpaperType.Url) return !string.IsNullOrWhiteSpace(project.Url);
        if (string.IsNullOrWhiteSpace(project.File)) return false;
        try { return File.Exists(Path.Combine(project.FolderPath ?? "", project.File)); }
        catch { return false; }
    }

    /// <summary>Вместо чёрного экрана показываем понятное объяснение.</summary>
    private void ShowUnsupported()
    {
        try
        {
            WebHost.Visibility = Visibility.Collapsed;
            VideoHost.Visibility = Visibility.Collapsed;
            MessageText.Text = Strings.T("wall.unsupported");
            MessageText.Visibility = Visibility.Visible;
            LogWallpaper("обои не поддерживаются: " + (_project?.FolderPath ?? "<null>") + " / type=" + (_project?.Type ?? "?"));
        }
        catch { }
    }

    /// <summary>Масштаб содержимого обоев из настроек (0.5..1.5).</summary>
    private double WallpaperScaleValue => Math.Clamp(_app?.WallpaperScale ?? 1.0, 0.5, 1.5);

    /// <summary>Применяет новый масштаб к уже открытому окну (слайдер в настройках).</summary>
    public void ApplyWallpaperScale()
    {
        try
        {
            if (_player != null) _player.Scale = (float)WallpaperScaleValue;
            if (_webView != null) _webView.ZoomFactor = WallpaperScaleValue;
            ApplyImageScale();
        }
        catch (Exception ex) { LogWallpaper("масштаб не применён: " + ex.Message); }
    }

    /// <summary>Журнал проблем рендера обоев — %APPDATA%\VividDesk\error.log.</summary>
    private void LogWallpaper(string message)
    {
        try
        {
            System.IO.File.AppendAllText(
                Path.Combine(ConfigService.AppDataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [wallpaper:{_deviceId}] {message}\r\n");
        }
        catch { }
    }

    /// <summary>Ответ на запросы страницы (например, повторный getProperties после перезагрузки).</summary>
    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var t) && t.GetString() == "requestProperties")
                PushPropertiesToPage();
        }
        catch { }
    }

    // ======================= ПРИМЕНЕНИЕ НАСТРОЕК =======================

    /// <summary>Применяет настройки на лету (без перезапуска).</summary>
    public void ApplySettings(WallpaperSettings settings)
    {
        _settings = settings;
        if (_project == null) return;

        if (_project.Kind == WallpaperType.Video) ApplyVideoSettings();
        else PushAllStateToPage();
    }

    private void ApplyVideoSettings()
    {
        if (_player == null) return;
        var s = _settings;

        try
        {
            _player.SetAdjustFloat(VideoAdjustOption.Enable, 1f);
            _player.SetAdjustFloat(VideoAdjustOption.Brightness, Clamp((float)s.Brightness, 0f, 2f));
            _player.SetAdjustFloat(VideoAdjustOption.Contrast, Clamp((float)s.Contrast, 0f, 2f));
            _player.SetAdjustFloat(VideoAdjustOption.Saturation, Clamp((float)s.Saturation, 0f, 3f));

            _player.SetRate((float)s.Rate);

            bool sound = _app.SoundEnabled && !s.Muted;
            _player.Mute = !sound;
            if (sound) _player.Volume = (int)Math.Clamp(s.Volume * 100, 0, 100);

            SetupVideoFpsLimit(s.FpsLimit);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("video settings: " + ex.Message); }
    }

    /// <summary>
    /// Лимит FPS для видео: LibVLC не умеет ограничивать частоту вывода, поэтому используем
    /// «кадровые окна»: короткое воспроизведение раз в 1000/limit мс, между ними — пауза.
    /// Для limit >= 24 или 0 окна отключены (не дают экономии и только портят плавность).
    /// </summary>
    private void SetupVideoFpsLimit(int limit)
    {
        _fpsTimer?.Stop();
        _fpsTimer = null;
        if (limit <= 0 || limit >= 24 || _player == null) return;

        double period = 1000.0 / limit;
        double burst = Math.Min(40.0, period * 0.45); // окно воспроизведения
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(period) };
        timer.Tick += (s, e) =>
        {
            if (_player == null) return;
            if (_paused) return;
            try
            {
                _player.SetPause(false);
                _fpsPlayUntil = DateTime.UtcNow.AddMilliseconds(burst);
                // одноразовый таймер окончания окна
                var burstTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(burst) };
                burstTimer.Tick += (s2, e2) =>
                {
                    burstTimer.Stop();
                    if (_player != null && !_paused) _player.SetPause(true);
                };
                burstTimer.Start();
            }
            catch { }
        };
        _fpsTimer = timer;
        timer.Start();
    }

    private void PushAllStateToPage()
    {
        if (!_webReady || _webView?.CoreWebView2 == null) return;
        PushPropertiesToPage();
        var s = _settings;
        PostJson(new
        {
            type = "visual",
            brightness = s.Brightness,
            contrast = s.Contrast,
            saturation = s.Saturation
        });
        PostJson(new { type = "rate", value = s.Rate });
        PostJson(new { type = "fps", value = s.FpsLimit });
        PostJson(new { type = "pause", value = _paused });
        PostJson(new { type = "mute", value = !(_app.SoundEnabled && !s.Muted) });
    }

    /// <summary>Собирает пользовательские свойства с типами по объявлению и доставляет на страницу.</summary>
    private void PushPropertiesToPage()
    {
        if (_project == null || _webView?.CoreWebView2 == null) return;
        var result = new Dictionary<string, object>();
        foreach (var p in _project.Properties)
        {
            string raw = null;
            _settings.UserProperties.TryGetValue(p.Key, out raw);
            result[p.Key] = ConvertPropertyValue(p, raw);
        }
        PostJson(new { type = "properties", properties = result });
    }

    /// <summary>Строка из settings -> типизированное значение JSON по определению свойства.</summary>
    public static object ConvertPropertyValue(WallpaperProperty p, string raw)
    {
        try
        {
            switch (p.Type)
            {
                case "slider":
                    if (double.TryParse(raw?.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
                    if (p.Default.HasValue && p.Default.Value.ValueKind == JsonValueKind.Number) return p.Default.Value.GetDouble();
                    return p.Min ?? 0.0;
                case "checkbox":
                    if (bool.TryParse(raw, out var b)) return b;
                    if (p.Default.HasValue && p.Default.Value.ValueKind == JsonValueKind.True) return true;
                    if (p.Default.HasValue && p.Default.Value.ValueKind == JsonValueKind.False) return false;
                    return false;
                default: // color, select — строки
                    if (!string.IsNullOrEmpty(raw)) return raw;
                    return p.Default.ToSettingString();
            }
        }
        catch { return raw; }
    }

    public void PostJson(object payload)
    {
        try
        {
            if (_webReady && _webView?.CoreWebView2 != null && _webView.CoreWebView2 != null)
                _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        }
        catch { }
    }

    // ======================= ПАУЗА / ВОЗОБНОВЛЕНИЕ =======================

    /// <summary>Пауза рендера. pauseAction: "pause" — стоп-кадр, "stop" — полная остановка (экономия ресурсов).</summary>
    public void PauseRendering()
    {
        if (_paused || _project == null) return;
        _paused = true;
        bool stop = _app.PauseAction == "stop";

        if (_project.Kind == WallpaperType.Video && _player != null)
        {
            try
            {
                if (stop) _player.Stop();
                else _player.SetPause(true);
            }
            catch { }
        }
        if (IsWeb) PostJson(new { type = "pause", value = true });
    }

    public void ResumeRendering()
    {
        if (!_paused || _project == null) return;
        _paused = false;

        if (_project.Kind == WallpaperType.Video && _player != null)
        {
            try
            {
                if (_app.PauseAction == "stop")
                {
                    string path = Path.Combine(_project.FolderPath, _project.File);
                    var media = new Media(_libVlc, path, FromType.FromPath);
                    media.AddOption(":input-repeat=65535");
                    _player.Play(media);
                    ApplyVideoSettings();
                }
                else _player.SetPause(false);
            }
            catch { }
        }
        if (IsWeb) PostJson(new { type = "pause", value = false });
    }

    /// <summary>Реакция на смену глобальной опции звука.</summary>
    public void ApplySoundState()
    {
        if (_project == null) return;
        if (_project.Kind == WallpaperType.Video) ApplyVideoSettings();
        else PostJson(new { type = "mute", value = !(_app.SoundEnabled && !_settings.Muted) });
    }

    // ======================= ВВОД: МЫШЬ / ВРЕМЯ / АУДИО =======================

    public void SendMouseMove(int globalX, int globalY)
    {
        if (!IsWeb) return;
        double nx = _bounds.Width > 0 ? (globalX - _bounds.X) / (double)_bounds.Width : 0;
        double ny = _bounds.Height > 0 ? (globalY - _bounds.Y) / (double)_bounds.Height : 0;
        PostJson(new
        {
            type = "mousemove",
            point = new { x = globalX - _bounds.X, y = globalY - _bounds.Y, nx, ny }
        });
    }

    public void SendTime()
    {
        if (!IsWeb) return;
        var now = DateTime.Now;
        PostJson(new
        {
            type = "time",
            time = new { hour = now.Hour, minute = now.Minute, second = now.Second, iso = now.ToString("o") }
        });
    }

    public void SendAudio(float[] bands)
    {
        if (!IsWeb) return;
        PostJson(new { type = "audio", bands });
    }

    // ======================= ОЧИСТКА =======================

    private void TearDownContent()
    {
        _fpsTimer?.Stop(); _fpsTimer = null;

        if (_player != null)
        {
            try { _player.Stop(); } catch { }
            _player.Dispose();
            _player = null;
        }

        // скрытое окно libvlc закрываем отложенно: сразу после Dispose окно ещё
        // разбирается потоком VLC — Close в этот момент ронял процесс нативно.
        // PostMessage (WM_CLOSE) выполнится на родном потоке VLC
        if (_vlcShadowWnd != IntPtr.Zero)
        {
            var wnd = _vlcShadowWnd;
            _vlcShadowWnd = IntPtr.Zero;
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(800);
                if (WinApi.IsWindow(wnd))
                    WinApi.PostMessage(wnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            });
        }

        _videoPanel = null;

        if (_webView != null)
        {
            _webReady = false;
            try { _webView.Dispose(); } catch { }
            _webView = null;
        }
        WebHost.Child = null;
        VideoHost.Child = null;
        WebHost.Visibility = Visibility.Collapsed;
        VideoHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>Кадр текущих обоев — для экрана блокировки.</summary>
    public async Task<bool> CaptureFrameAsync(string outputPath, int width, int height)
    {
        try
        {
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }

            // веб-обои: снимок страницы из WebView2
            if (_webView?.CoreWebView2 != null)
            {
                if (await GrabWebAsync(outputPath) && LockScreenService.IsUsableFrame(outputPath)) return true;

                // страница могла ещё не отрисоваться — одна повторная попытка
                await Task.Delay(800);
                try { File.Delete(outputPath); } catch { }
                if (await GrabWebAsync(outputPath) && LockScreenService.IsUsableFrame(outputPath)) return true;

                return TryPreviewFallback(outputPath);
            }

            // видео-обои: снапшот живого плеера на D3D11 часто отдаёт зелёный кадр,
            // поэтому берём кадр из самого файла через ThumbnailGenerator (без аппаратного декодирования)
            if (_project != null && _project.Kind == WallpaperType.Video)
            {
                string video = Path.Combine(_project.FolderPath, _project.File);
                if (File.Exists(video) && ThumbnailGenerator.FromVideo(video, outputPath, Math.Max(640, width)))
                {
                    if (LockScreenService.IsUsableFrame(outputPath)) return true;
                    try { File.Delete(outputPath); } catch { }
                }
                return TryPreviewFallback(outputPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CaptureFrame failed: " + ex.Message);
        }
        return false;
    }

    private async Task<bool> GrabWebAsync(string outputPath)
    {
        try
        {
            using var fs = File.Create(outputPath);
            await _webView.CoreWebView2.CapturePreviewAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, fs);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 2048;
        }
        catch { return false; }
    }

    /// <summary>Запасной вариант — preview.png из папки обоев, если он не однотонный.</summary>
    private bool TryPreviewFallback(string outputPath)
    {
        try
        {
            string preview = _project?.PreviewPath;
            if (!string.IsNullOrEmpty(preview) && File.Exists(preview) && LockScreenService.IsUsableFrame(preview))
            {
                File.Copy(preview, outputPath, true);
                return true;
            }
        }
        catch { }
        return false;
    }

    public void CloseWallpaper()
    {
        TearDownContent();
        Close();
    }

    private static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));

    private static string SafeDeviceId(string id)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length > 0 ? sb.ToString() : "d";
    }
}
