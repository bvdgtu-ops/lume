using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using LibVLCSharp.Shared;
using Microsoft.Web.WebView2.Core;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2;
using VividDesk.Core;

namespace VividDesk.Views;

/// <summary>
/// Полноэкранная живая заставка: анимированные обои текущего монитора.
/// Любой ввод закрывает её (часы — в отдельном окне LiveLockClockWindow),
/// после чего система блокируется по-настоящему.
/// </summary>
public partial class LiveLockWindow : Window
{
    private readonly LibVLC _libVlc;
    private readonly WallpaperProject _project;
    private readonly WallpaperSettings _settings;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly DateTime _shownAt = DateTime.UtcNow;

    private MediaPlayer _player;
    private System.Windows.Forms.Panel _panel;
    private WebView2 _webView;

    public event Action Dismissed;

    public LiveLockWindow(LibVLC vlc, WallpaperProject project, WallpaperSettings settings,
                          System.Drawing.Rectangle bounds)
    {
        InitializeComponent();
        _libVlc = vlc;
        _project = project;
        _settings = settings;
        _bounds = bounds;

        WindowState = WindowState.Normal;
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        Cursor = Cursors.None;

        Loaded += (s, e) =>
        {
            Focus();
            Keyboard.Focus(this);
            if (_project != null && _project.Kind == WallpaperType.Video) LoadVideo();
            else if (_project != null) _ = LoadWebAsync();
        };

        PreviewKeyDown += (s, e) => Dismiss();
        MouseDown += (s, e) => Dismiss();
        MouseMove += (s, e) => Dismiss();
        MouseWheel += (s, e) => Dismiss();
    }

    private void LoadVideo()
    {
        try
        {
            var host = new System.Windows.Forms.Integration.WindowsFormsHost();
            _panel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
            host.Child = _panel;
            ContentRoot.Children.Add(host);

            _player = new MediaPlayer(_libVlc) { EnableHardwareDecoding = true, Scale = 0f, Mute = true };
            _player.Hwnd = _panel.Handle;

            string path = Path.Combine(_project.FolderPath, _project.File);
            if (!File.Exists(path)) return;

            using var media = new Media(_libVlc, path, FromType.FromPath);
            media.AddOption(":input-repeat=65535");
            _player.Play(media);
            try { _player.SetRate((float)Math.Clamp(_settings?.Rate ?? 1.0, 0.25, 3.0)); } catch { }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("LiveLock video: " + ex.Message); }
    }

    private async System.Threading.Tasks.Task LoadWebAsync()
    {
        try
        {
            string folder = Path.Combine(ConfigService.AppDataDir, "WebView2", "livelock");
            Directory.CreateDirectory(folder);
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
            ContentRoot.Children.Add(_webView);
            await _webView.EnsureCoreWebView2Async(env);

            var core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;

            if (_project.Kind == WallpaperType.Url) core.Navigate(_project.Url);
            else core.Navigate(new Uri(Path.Combine(_project.FolderPath, _project.File)).AbsoluteUri);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("LiveLock web: " + ex.Message); }
    }

    private bool _dismissed;

    private void Dismiss()
    {
        if (_dismissed) return;
        // окно появляется под курсором: первый WM_MOUSEMOVE не должен его закрывать
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 900) return;
        _dismissed = true;

        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        try { _webView?.Dispose(); } catch { }

        Dismissed?.Invoke();
    }
}
