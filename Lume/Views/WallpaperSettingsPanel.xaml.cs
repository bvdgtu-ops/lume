using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VividDesk.Core;

namespace VividDesk.Views;

/// <summary>
/// Переиспользуемая панель настройки одного обоев на одном мониторе:
/// пользовательские свойства из project.json + встроенные регулировки.
/// Используется и в правой панели библиотеки, и в отдельном окне WallpaperSettingsWindow.
/// </summary>
public partial class WallpaperSettingsPanel : UserControl
{
    private App _app;
    private WallpaperProject _project;
    private WpfScreen _screen;
    private WallpaperSettings _settings;
    private bool _building;
    private readonly DispatcherTimer _saveDebounce;

    public WallpaperProject Project => _project;
    public WallpaperSettings CurrentSettings => _settings;
    public WpfScreen CurrentScreen => _screen;

    public WallpaperSettingsPanel()
    {
        InitializeComponent();
        EmptyText.Text = Strings.T("panel.empty");
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveDebounce.Tick += (s, e) => { _saveDebounce.Stop(); _app?.Config.Save(); };
    }

    /// <summary>Пустое состояние (обои не выбраны).</summary>
    public void SetEmpty(string message)
    {
        _project = null;
        EmptyText.Text = message;
        EmptyText.Visibility = Visibility.Visible;
        Scroller.Visibility = Visibility.Collapsed;
    }

    /// <summary>Загружает настройки обоев для монитора: существующие, либо значения по умолчанию.</summary>
    public void Load(App app, WallpaperProject project, WpfScreen screen)
    {
        var mc = app.Manager.GetMonitorConfig(screen);
        WallpaperSettings settings =
            (mc != null && mc.WallpaperPath == project.FolderPath && mc.Settings != null)
                ? mc.Settings
                : PlaylistManager.DefaultSettingsFor(project);
        LoadExisting(app, project, screen, settings);
    }

    /// <summary>Строит панель для конкретных настроек (без смены значений).</summary>
    public void LoadExisting(App app, WallpaperProject project, WpfScreen screen, WallpaperSettings settings)
    {
        _app = app;
        _project = project;
        _screen = screen;
        _settings = settings;
        _building = true;

        EmptyText.Visibility = Visibility.Collapsed;
        Scroller.Visibility = Visibility.Visible;
        Root.Children.Clear();

        Root.Children.Add(new TextBlock
        {
            Text = project.Title,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        Root.Children.Add(new TextBlock
        {
            Text = string.Format(Strings.T("panel.monitorHint"), MonitorNumber(screen)),
            FontSize = 12,
            Opacity = 0.55,
            Margin = new Thickness(0, 2, 0, 4),
            TextWrapping = TextWrapping.Wrap
        });
        Root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        // -------- пользовательские свойства из project.json --------
        if (project.Properties.Count > 0)
        {
            Root.Children.Add(Header(Strings.T("panel.propsHeader")));
            foreach (var p in project.Properties)
                BuildUserProperty(p, settings);
            Root.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
        }

        // -------- встроенные регулировки --------
        Root.Children.Add(Header(Strings.T("panel.playbackHeader")));

        Root.Children.Add(SliderRow(Strings.T("panel.rate"), 0.25, 3, 0.05, settings.Rate, "×{0:0.00}", v =>
        {
            settings.Rate = v; ApplyLive();
        }));
        Root.Children.Add(SliderRow(Strings.T("panel.brightness"), 0, 2, 0.05, settings.Brightness, "{0:0.00}", v =>
        {
            settings.Brightness = v; ApplyLive();
        }));
        Root.Children.Add(SliderRow(Strings.T("panel.contrast"), 0, 2, 0.05, settings.Contrast, "{0:0.00}", v =>
        {
            settings.Contrast = v; ApplyLive();
        }));
        Root.Children.Add(SliderRow(Strings.T("panel.saturation"), 0, 3, 0.05, settings.Saturation, "{0:0.00}", v =>
        {
            settings.Saturation = v; ApplyLive();
        }));
        Root.Children.Add(SliderRow(Strings.T("panel.volume"), 0, 1, 0.01, settings.Volume, "{0:P0}", v =>
        {
            settings.Volume = v; ApplyLive();
        }));
        Root.Children.Add(SliderRow(Strings.T("panel.fps"), 0, 60, 1, settings.FpsLimit, "{0:0}", v =>
        {
            settings.FpsLimit = (int)Math.Round(v); ApplyLive();
        }));

        var mute = new CheckBox
        {
            Content = Strings.T("panel.mute"),
            IsChecked = settings.Muted,
            Margin = new Thickness(0, 10, 0, 0)
        };
        mute.Checked += (s, e) => { settings.Muted = true; ApplyLive(); };
        mute.Unchecked += (s, e) => { settings.Muted = false; ApplyLive(); };
        Root.Children.Add(mute);

        if (!app.Config.Config.SoundEnabled)
        {
            Root.Children.Add(new TextBlock
            {
                Text = Strings.T("panel.soundWarn"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.6,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12
            });
        }

        _building = false;
    }

    private static int MonitorNumber(WpfScreen s) => WpfScreen.AllScreens.ToList().IndexOf(s) + 1;

    private TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        FontSize = 13,
        Margin = new Thickness(0, 6, 0, 6),
        Foreground = (Brush)FindResource("AccentBrush")
    };

    private void BuildUserProperty(WallpaperProperty p, WallpaperSettings settings)
    {
        settings.UserProperties.TryGetValue(p.Key, out string raw);

        switch (p.Type)
        {
            case "slider":
            {
                double min = p.Min ?? 0, max = p.Max ?? 100, step = p.Step ?? 1;
                double val = double.TryParse(raw?.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var d)
                    ? d
                    : (p.Default.HasValue && p.Default.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? p.Default.Value.GetDouble() : min);
                Root.Children.Add(SliderRow(p.Label, min, max, step, val, "{0:0.##}", v =>
                {
                    settings.UserProperties[p.Key] = v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyLive();
                }));
                break;
            }
            case "checkbox":
            {
                bool val = bool.TryParse(raw, out var b)
                    ? b
                    : (p.Default.HasValue && p.Default.Value.ValueKind == System.Text.Json.JsonValueKind.True);
                var cb = new CheckBox { Content = p.Label, IsChecked = val, Margin = new Thickness(0, 6, 0, 0) };
                cb.Checked += (s, e) => { settings.UserProperties[p.Key] = "true"; ApplyLive(); };
                cb.Unchecked += (s, e) => { settings.UserProperties[p.Key] = "false"; ApplyLive(); };
                Root.Children.Add(cb);
                break;
            }
            case "color":
            {
                string val = !string.IsNullOrEmpty(raw) ? raw : p.Default.ToSettingString();
                if (string.IsNullOrEmpty(val)) val = "#ffffff";
                var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                panel.Children.Add(new TextBlock { Text = p.Label });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var swatch = new Border
                {
                    Width = 34, Height = 22, CornerRadius = new CornerRadius(4),
                    Background = ParseBrush(val),
                    BorderBrush = (Brush)Application.Current.FindResource("BorderBrush1"),
                    BorderThickness = new Thickness(1)
                };
                var hex = new TextBox { Text = val, Width = 90, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
                var pick = new Button { Content = Strings.T("panel.pick"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };

                void Set(string color)
                {
                    settings.UserProperties[p.Key] = color;
                    hex.Text = color;
                    swatch.Background = ParseBrush(color);
                    ApplyLive();
                }

                hex.LostFocus += (s, e) => { if (hex.Text.Trim().StartsWith("#")) Set(hex.Text.Trim()); };
                pick.Click += (s, e) =>
                {
                    using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
                    if (ParseColor(val, out var c0)) dlg.Color = c0;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        Set($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
                };

                row.Children.Add(swatch);
                row.Children.Add(hex);
                row.Children.Add(pick);
                panel.Children.Add(row);
                Root.Children.Add(panel);
                break;
            }
            case "select":
            {
                var label = new TextBlock { Text = p.Label, Margin = new Thickness(0, 6, 0, 2) };
                var combo = new ComboBox { Margin = new Thickness(0, 2, 0, 0) };
                foreach (var o in p.Options) combo.Items.Add(o.Label);
                int sel = p.Options.FindIndex(o => o.Value == raw
                    || (string.IsNullOrEmpty(raw) && o.Value == p.Default.ToSettingString()));
                if (sel < 0 && p.Options.Count > 0) sel = 0;
                combo.SelectedIndex = sel;
                combo.SelectionChanged += (s, e) =>
                {
                    if (combo.SelectedIndex >= 0 && combo.SelectedIndex < p.Options.Count)
                    {
                        settings.UserProperties[p.Key] = p.Options[combo.SelectedIndex].Value;
                        ApplyLive();
                    }
                };
                Root.Children.Add(label);
                Root.Children.Add(combo);
                break;
            }
            default:
            {
                var box = new TextBox { Text = raw ?? "", Margin = new Thickness(0, 2, 0, 6) };
                var lbl = new TextBlock { Text = p.Label, Margin = new Thickness(0, 6, 0, 2) };
                box.LostFocus += (s, e) => { settings.UserProperties[p.Key] = box.Text; ApplyLive(); };
                Root.Children.Add(lbl);
                Root.Children.Add(box);
                break;
            }
        }
    }

    private static Brush ParseBrush(string hex)
    {
        try { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        catch { return Brushes.Gray; }
    }

    private static bool ParseColor(string hex, out System.Drawing.Color color)
    {
        try
        {
            color = (System.Drawing.Color)new System.Drawing.ColorConverter().ConvertFromString(hex);
            return true;
        }
        catch { color = System.Drawing.Color.White; return false; }
    }

    /// <summary>Строка «подпись + слайдер + значение». onChange вызывается только при действии пользователя.</summary>
    private UIElement SliderRow(string label, double min, double max, double step, double value,
        string format, Action<double> onChange)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var valText = new TextBlock { Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, value) };
        var header = new DockPanel();
        DockPanel.SetDock(valText, Dock.Right);
        header.Children.Add(valText);
        header.Children.Add(new TextBlock { Text = label });

        var slider = new Slider
        {
            Minimum = min, Maximum = max,
            SmallChange = step, LargeChange = step * 5,
            TickFrequency = step, IsSnapToTickEnabled = false,
            Value = value,
        };
        // ValueChanged срабатывает и при программной установке — фильтруем по захвату мыши/фокусу
        slider.ValueChanged += (s, e) =>
        {
            valText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, e.NewValue);
            if (slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin)
                onChange(e.NewValue);
        };
        var def = value;
        var dbl = new Button
        {
            Content = Strings.T("panel.reset"), FontSize = 11, Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
        };
        dbl.Click += (s, e) => { slider.Value = def; onChange(def); };

        panel.Children.Add(header);
        panel.Children.Add(slider);
        panel.Children.Add(dbl);
        return panel;
    }

    /// <summary>Применить настройки к активному окну немедленно + отложенное сохранение конфига.</summary>
    private void ApplyLive()
    {
        if (_building || _project == null) return;
        var win = _app.Manager.Windows.FirstOrDefault(w =>
            string.Equals(w.Project?.FolderPath, _project.FolderPath, StringComparison.OrdinalIgnoreCase) &&
            w.DeviceId == _screen.DeviceName);
        win?.ApplySettings(_settings);

        var mc = _app.Manager.GetMonitorConfig(_screen);
        if (mc != null && mc.WallpaperPath == _project.FolderPath)
        {
            mc.Settings = _settings;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
    }
}
