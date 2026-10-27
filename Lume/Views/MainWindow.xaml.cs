using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VividDesk.Core;

namespace VividDesk.Views;

/// <summary>Мини-диалог ввода (для URL и названий).</summary>
public class InputDialog : Window
{
    private readonly TextBox _box = new() { Padding = new Thickness(6, 4, 6, 4), FontSize = 13 };
    public string Answer => _box.Text.Trim();

    public InputDialog(string title, string label, string defaultValue = "")
    {
        Title = title;
        double s = UiScale.Current; // базовый размер диалога 440×168
        Width = Math.Round(440 * s); Height = Math.Round(168 * s);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        var chrome = new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = Math.Round(40 * s),
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
        SourceInitialized += (s, e) => WinApi.EnableRoundedCorners(this);
        Background = FindResource("BgBrush") as Brush;
        FontFamily = FindResource("AppFont") as FontFamily;
        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            LayoutTransform = new ScaleTransform(s, s)
        };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        _box.Text = defaultValue;
        panel.Children.Add(_box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "ОК", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (s, e) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "Отмена", MinWidth = 84 };
        cancel.Click += (s, e) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
    }
}

public class WallpaperCardVM
{
    public WallpaperProject Project { get; set; }
    public string Title => string.IsNullOrEmpty(Project.Problem)
            ? Project.Title
            : Project.Title + " — " + Strings.T("wall.unsupportedShort");
    public string TypeLabel => Project.Kind switch
    {
        WallpaperType.Video => Strings.T("card.kindVideo") + Project.File,
        WallpaperType.Image => Strings.T("card.kindImage") + Project.File,
        WallpaperType.Url => Strings.T("card.kindWeb") + Project.Url,
        _ => Strings.T("card.kindWeb") + Project.File
    };
    public BitmapImage PreviewImage { get; set; }
}

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly List<WpfScreen> _screens;
    private WpfScreen _selectedScreen;
    private WallpaperProject _selectedProject;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;

        _screens = WpfScreen.AllScreens.ToList();
        _selectedScreen = _screens.FirstOrDefault(s => s.Primary) ?? _screens[0];

        ApplyLanguage();
        Strings.LanguageChanged += ApplyLanguage;

        ApplyUiScale(UiScale.Current);
        UiScale.Changed += ApplyUiScale;
        StateChanged += (s, e) => UpdateMaximizeMargin();

        _app.Library.WallpapersChanged += () => Dispatcher.BeginInvoke(RefreshLibrary);

        SourceInitialized += (s, e) => WinApi.EnableRoundedCorners(this);

        // контекстное меню карточки: MenuItem.Click всплывает до ListBox
        WallList.AddHandler(System.Windows.Controls.MenuItem.ClickEvent,
            new RoutedEventHandler(OnWallCardMenuClick));

        Closing += (s, e) =>
        {
            // крест скрывает окно, приложение живёт в трее
            e.Cancel = true;
            Hide();
        };
    }

    // ======================= ЯЗЫК =======================

    /// <summary>Обновляет все тексты окна на текущем языке (вызывается и при смене языка на лету).</summary>
    private void ApplyLanguage()
    {
        Title = Strings.T("app.title");
        SubTitleText.Text = Strings.T("app.subtitle");
        BtnAddFile.Content = Strings.T("btn.addFile");
        BtnAddUrl.Content = Strings.T("btn.addUrl");
        BtnWallpaperSettings.Content = Strings.T("btn.wallSettings");
        BtnPresets.Content = Strings.T("btn.presets");
        BtnSettings.Content = Strings.T("btn.settings");
        MonitorLabel.Text = Strings.T("monitor.label");
        BtnClearMonitor.Content = Strings.T("monitor.clear");
        BtnOpenFolder.Content = Strings.T("btn.openFolder");

        // контекстное меню карточек объявлено внутри стиля контейнера
        foreach (var st in WallList.ItemContainerStyle.Setters.OfType<System.Windows.Setter>())
        {
            if (st.Property != ListBoxItem.ContextMenuProperty || st.Value is not ContextMenu cm) continue;
            var items = cm.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            if (items.Count >= 2)
            {
                items[0].Header = Strings.T("card.settings");
                items[1].Header = Strings.T("card.delete");
            }
        }

        RefreshMonitorBar();
        RefreshLibrary();
    }

    // ======================= МАСШТАБ ИНТЕРФЕЙСА =======================

    // базовые (до масштабирования) размеры главного окна
    private const double BaseWidth = 1180, BaseHeight = 720, BaseMinWidth = 940, BaseMinHeight = 560;
    private const double BaseCaption = 68, BaseResizeBorder = 7;

    private void ApplyUiScale(double s)
    {
        RootScale.ScaleX = RootScale.ScaleY = s;
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null)
        {
            chrome.CaptionHeight = Math.Round(BaseCaption * s);
            chrome.ResizeBorderThickness = new Thickness(Math.Max(4, Math.Round(BaseResizeBorder * s)));
        }
        Width = Math.Round(BaseWidth * s);
        Height = Math.Round(BaseHeight * s);
        MinWidth = Math.Round(BaseMinWidth * s);
        MinHeight = Math.Round(BaseMinHeight * s);
        UpdateMaximizeMargin();
    }

    /// <summary>При maximize окно вылезает за экран на толщину рамки ресайза — компенсируем отступом.</summary>
    private void UpdateMaximizeMargin()
    {
        var border = System.Windows.Shell.WindowChrome.GetWindowChrome(this)?.ResizeBorderThickness.Left ?? BaseResizeBorder;
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(border) : new Thickness(0);
    }

    // ======================= БИБЛИОТЕКА =======================

    public void RefreshLibrary()
    {
        var vms = new List<WallpaperCardVM>();
        foreach (var p in _app.Library.Wallpapers)
        {
            var vm = new WallpaperCardVM { Project = p };
            try
            {
                if (File.Exists(p.PreviewPath))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bi.DecodePixelWidth = 360;
                    bi.UriSource = new Uri(p.PreviewPath);
                    bi.EndInit();
                    bi.Freeze();
                    vm.PreviewImage = bi;
                }
            }
            catch { }
            vms.Add(vm);
        }

        WallList.ItemsSource = vms;
        StatusText.Text = string.Format(Strings.T("status.format"), vms.Count, _app.Library.Root);

        // восстановить выделение
        if (_selectedProject != null)
        {
            var sel = vms.FirstOrDefault(v => v.Project.FolderPath == _selectedProject.FolderPath);
            if (sel != null && WallList.SelectedItem != sel) WallList.SelectedItem = sel;
        }
    }

    private void WallList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WallList.SelectedItem is not WallpaperCardVM vm) return;
        ApplyProjectToSelectedMonitor(vm.Project);
    }

    /// <summary>Двойной клик по карточке — отдельное окно настройки этого обои.</summary>
    private void WallList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WallList.SelectedItem is WallpaperCardVM vm)
            _app.OpenWallpaperSettings(vm.Project, _selectedScreen);
    }

    private void ApplyProjectToSelectedMonitor(WallpaperProject project)
    {
        _selectedProject = project;

        // если на мониторе уже этот обои — редактируем существующие настройки, иначе — дефолтные
        var mc = _app.Manager.GetMonitorConfig(_selectedScreen);
        WallpaperSettings settings =
            (mc != null && mc.WallpaperPath == project.FolderPath && mc.Settings != null)
                ? mc.Settings
                : PlaylistManager.DefaultSettingsFor(project);

        _app.Manager.Apply(_selectedScreen, project, settings);
    }

    // ======================= МОНИТОРЫ =======================

    private void RefreshMonitorBar()
    {
        MonitorBar.Children.Clear();
        for (int i = 0; i < _screens.Count; i++)
        {
            var s = _screens[i];
            var btn = new ToggleButton
            {
                Content = $"  {i + 1}: {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? Strings.T("monitor.primary") : "")}  ",
                IsChecked = s == _selectedScreen,
                Margin = new Thickness(0, 0, 8, 0),
                Tag = s,
            };
            btn.Click += (snd, e) =>
            {
                _selectedScreen = s;
                RefreshMonitorBar();
                // выделить в библиотеке то, что уже висит на этом мониторе
                var mc = _app.Manager.GetMonitorConfig(s);
                if (mc == null || string.IsNullOrEmpty(mc.WallpaperPath)) return;
                var proj = _app.Library.FindByPath(mc.WallpaperPath);
                if (proj == null) return;
                _selectedProject = proj;
                var item = WallList.ItemsSource?
                    .Cast<WallpaperCardVM>()
                    .FirstOrDefault(v => v.Project.FolderPath == proj.FolderPath);
                if (item != null) WallList.SelectedItem = item;
            };
            MonitorBar.Children.Add(btn);
        }
    }

    private void BtnClearMonitor_Click(object sender, RoutedEventArgs e)
    {
        _app.Manager.Clear(_selectedScreen);
    }

    // ======================= КНОПКИ ТУЛБАРА =======================

    private void BtnAddFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("dlg.addVideo"),
            Filter = Strings.T("filter.video") + "|" + Strings.T("filter.html") + "|" + Strings.T("filter.all")
        };
        if (dlg.ShowDialog(this) == true)
            ImportPaths(new[] { dlg.FileName });
    }

    private void BtnAddZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("btn.addZip"),
            Filter = Strings.T("filter.zip")
        };
        if (dlg.ShowDialog(this) == true) ImportPaths(new[] { dlg.FileName });
    }

    private void ImportPaths(IEnumerable<string> paths)
    {
        try
        {
            WallpaperProject last = null;
            foreach (var p in paths)
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (LibraryService.VideoExts.Contains(ext) || ext == ".html")
                    last = _app.Library.ImportFile(p, moveIntoLibrary: false);
                else if (ext == ".zip")
                {
                    var res = _app.Library.ImportZip(p);
                    if (res.Ok) last = res.Project;
                    else MessageBox.Show(this, Strings.T(res.Problem == "unsupported" ? "zip.unsupported" : "zip.noproject"),
                        "Lume", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            _app.Library.Rescan();
            if (last != null)
                Dispatcher.BeginInvoke(() =>
                {
                    var vm = WallList.ItemsSource?.Cast<WallpaperCardVM>()
                        .FirstOrDefault(v => v.Project.FolderPath == last.FolderPath);
                    if (vm != null) WallList.SelectedItem = vm;
                }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.T("msg.addFailed"), ex.Message), "Lume",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAddUrl_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog(Strings.T("url.title"), Strings.T("url.prompt"), "https://");
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && Uri.TryCreate(dlg.Answer, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var titleDlg = new InputDialog(Strings.T("name.title"), Strings.T("name.prompt"), uri.Host);
            titleDlg.Owner = this;
            if (titleDlg.ShowDialog() == true)
            {
                _app.Library.ImportUrl(uri.ToString(), titleDlg.Answer);
                _app.Library.Rescan();
            }
        }
        else if (dlg.DialogResult == true)
        {
            MessageBox.Show(this, Strings.T("url.bad"), "Lume");
        }
    }

    private void OnWallCardMenuClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.Controls.MenuItem mi ||
            mi.DataContext is not WallpaperCardVM vm) return;

        switch (mi.Tag as string)
        {
            case "settings":
                _app.OpenWallpaperSettings(vm.Project, _selectedScreen);
                break;
            case "delete":
                if (MessageBox.Show(this,
                        string.Format(Strings.T("card.deleteConfirm"), vm.Title, vm.Project.FolderPath),
                        "Lume", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _app.Manager.OnProjectDeleted(vm.Project);
                _app.Library.Delete(vm.Project);
                break;
        }
    }

    private void BtnWallpaperSettings_Click(object sender, RoutedEventArgs e)
    {
        // если обои не выбран в библиотеке — открываем настройки текущих активных обоев
        if (_selectedProject != null)
        {
            _app.OpenWallpaperSettings(_selectedProject, _selectedScreen);
            return;
        }
        var win = _app.Manager.Windows.FirstOrDefault();
        if (win?.Project != null)
        {
            var screen = _screens.FirstOrDefault(s => s.DeviceName == win.DeviceId) ?? _selectedScreen;
            _app.OpenWallpaperSettings(win.Project, screen);
            return;
        }
        var first = _app.Library.Wallpapers.FirstOrDefault();
        if (first != null)
        {
            _app.OpenWallpaperSettings(first, _selectedScreen);
            return;
        }
        MessageBox.Show(this, Strings.T("lib.empty"), "Lume");
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_app) { Owner = this };
        w.ShowDialog();
        RefreshLibrary();
    }

    private void BtnPresets_Click(object sender, RoutedEventArgs e)
    {
        // фон/текст задаёт неявный стеклянный стиль ContextMenu из App.xaml —
        // явная полупрозрачная заливка здесь делала меню невидимым
        var menu = new ContextMenu();
        var save = new MenuItem { Header = Strings.T("preset.save") };
        save.Click += (s, a) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = Strings.T("preset.save").TrimEnd('…'),
                Filter = Strings.T("preset.filterSave"),
                FileName = "Мой пресет.vdpreset"
            };
            if (dlg.ShowDialog(this) == true)
            {
                _app.Presets.Export(dlg.FileName, Path.GetFileNameWithoutExtension(dlg.FileName));
                MessageBox.Show(this, Strings.T("preset.saved"), "Lume");
            }
        };
        var load = new MenuItem { Header = Strings.T("preset.load") };
        load.Click += (s, a) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.T("preset.load").TrimEnd('…'),
                Filter = Strings.T("preset.filterLoad")
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    _app.Presets.Import(dlg.FileName);
                    RefreshLibrary();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Strings.T("preset.loadFailed"), ex.Message), "Lume");
                }
            }
        };
        menu.Items.Add(save);
        menu.Items.Add(load);
        menu.PlacementTarget = BtnPresets;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_app.Library.Root}\"") { UseShellExecute = true }); }
        catch { }
    }

    // ======================= КНОПКИ ОКНА (безшовный заголовок) =======================

    private void BtnMinWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaxWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnCloseWindow_Click(object sender, RoutedEventArgs e) => Close(); // Closing скрывает окно в трей

    // ======================= DRAG & DROP =======================

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        ImportPaths(files.Where(f =>
            LibraryService.VideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()) ||
            Path.GetExtension(f).ToLowerInvariant() == ".html" ||
            Path.GetExtension(f).ToLowerInvariant() == ".zip"));
    }
}
