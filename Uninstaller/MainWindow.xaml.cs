using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LumeUninstall;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, string> S;
    private bool _busy;
    private string _installDir;

    private static string DefaultInstallDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume");

    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lume";

    public MainWindow()
    {
        InitializeComponent();
        bool ru = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        S = ru ? Ru : En;
        ApplyStrings();
        LoadLogo();
        DetectInstall();
    }

    private void LoadLogo()
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            if (icon != null)
                ImgLogo.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { }
    }

    private void ApplyStrings()
    {
        Title = S["title"];
        LblTitle.Text = S["title"];
        LblConfirm.Text = S["confirm"];
        ChkData.Content = S["delData"];
        BtnRemove.Content = S["remove"];
        BtnCancel.Content = S["cancel"];
    }

    private void DetectInstall()
    {
        string dir = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            dir = key?.GetValue("InstallLocation") as string;
        }
        catch { }
        if (string.IsNullOrWhiteSpace(dir) || !File.Exists(Path.Combine(dir, "Lume.exe")))
        {
            dir = DefaultInstallDir();
            if (!File.Exists(Path.Combine(dir, "Lume.exe")))
            {
                // не установлен — оставляем только «Закрыть»
                LblPath.Text = S["notInstalled"];
                BtnRemove.IsEnabled = false;
                ChkData.Visibility = Visibility.Collapsed;
                Pbar.Visibility = Visibility.Collapsed;
                BtnRemove.Visibility = Visibility.Collapsed;
                return;
            }
        }
        _installDir = dir;
        LblPath.Text = dir;
    }

    private async void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_installDir)) return;
        if (MessageBox.Show(this, S["confirmBox"], S["title"],
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _busy = true;
        BtnRemove.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        LblStatus.Text = S["removing"];

        // чтение WPF-контролов — только на UI-потоке: внутри Task.Run это бросает
        // InvalidOperationException и убивает процесс до запуска очистки папки
        bool deleteData = ChkData.IsChecked == true;

        // библиотека обоев может быть переназначена в настройках — путь берём из
        // config.json ДО удаления %APPDATA%\VividDesk
        string customLib = null;
        {
            try
            {
                var cfgPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VividDesk", "config.json");
                if (File.Exists(cfgPath))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfgPath));
                    if (doc.RootElement.TryGetProperty("wallpaperDir", out var wd)
                        && wd.ValueKind == System.Text.Json.JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(wd.GetString()))
                        customLib = wd.GetString();
                }
            }
            catch { }
        }

        await Task.Run(() =>
        {
            foreach (var p in Process.GetProcessesByName("Lume"))
            {
                try { p.Kill(true); p.WaitForExit(5000); } catch { }
            }

            // WebView2-процессы (браузерный слой веб-обоев) живут вне дерева Lume
            // и держат блокировки на папку данных внутри установки — убираем по
            // ссылке на папку установки в их командной строке
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
                foreach (var mo in searcher.Get())
                {
                    var cl = mo["CommandLine"] as string;
                    if (cl == null || cl.IndexOf(_installDir, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        var wp = Process.GetProcessById((int)(uint)mo["ProcessId"]);
                        wp.Kill(true);
                    }
                    catch { }
                }
            }
            catch { }

            // реестр: запись об удалении + автозапуск
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                run?.DeleteValue("Lume", false);
            }
            catch { }

            // ярлыки
            string desktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lume.lnk");
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs\Lume.lnk");
            try { if (File.Exists(desktop)) File.Delete(desktop); } catch { }
            try { if (File.Exists(startMenu)) File.Delete(startMenu); } catch { }

            // пользовательские данные — только по галочке
            if (deleteData)
            {
                try { Directory.Delete(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VividDesk"), true); } catch { }
                try { Directory.Delete(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VividDesk Wallpapers"), true); } catch { }
                DeleteLibrarySafe(customLib);
            }
        });

        // удаляем папку установки: сам деинсталлятор занят, поэтому отдаём это
        // PowerShell после своего завершения (Remove-Item — проверено, cmd/rd капризен)
        // The wallpaper library now lives inside the install folder, so a plain uninstall would
// wipe the user's own wallpapers together with the app. Move it to Documents first.
        if (!deleteData)
        {
            try
            {
                string lib = customLib;
                if (string.IsNullOrWhiteSpace(lib)) lib = Path.Combine(_installDir, "wallpapers");
                if (Directory.Exists(lib) && lib.StartsWith(_installDir, StringComparison.OrdinalIgnoreCase))
                {
                    string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string dest = Path.Combine(docs, "Lume Wallpapers");
                    for (int i = 2; Directory.Exists(dest); i++)
                        dest = Path.Combine(docs, "Lume Wallpapers (" + i + ")");
                    Directory.Move(lib, dest);
                }
            }
            catch { }
        }

        string exe = Environment.ProcessPath;
        string script =
            "Start-Sleep -Seconds 3; " +
            $"Remove-Item -LiteralPath '{exe}' -Force -ErrorAction SilentlyContinue; " +
            $"Remove-Item -LiteralPath '{_installDir}' -Recurse -Force -ErrorAction SilentlyContinue";
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            });
        }
        catch { }

        LblStatus.Text = S["done"];
        Pbar.Visibility = Visibility.Collapsed;
        ChkData.Visibility = Visibility.Collapsed;
        await Task.Delay(1200);
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Удаление переназначенной библиотеки обоев: только если путь строго внутри
    /// профиля пользователя (защита от вырезанного в конфиге пути вроде C:\ или Desktop).
    /// </summary>
    private static void DeleteLibrarySafe(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            string full = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string profile = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string desktop = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
                .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!full.StartsWith(profile, StringComparison.OrdinalIgnoreCase)) return;   // вне профиля — не трогаем
            if (full.Equals(profile, StringComparison.OrdinalIgnoreCase)) return;        // сам профиль
            if (full.Equals(desktop, StringComparison.OrdinalIgnoreCase)) return;        // рабочий стол
            if (full.Length <= 3) return;                                                // корень диска
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch { }
    }

    private static readonly Dictionary<string, string> Ru = new()
    {
        ["title"] = "Удаление Lume",
        ["confirm"] = "Удалить Lume с этого компьютера?",
        ["delData"] = "Также удалить настройки и обои (библиотеку в «Документах»)",
        ["remove"] = "Удалить",
        ["cancel"] = "Отмена",
        ["removing"] = "Удаление…",
        ["done"] = "Lume удалён.",
        ["notInstalled"] = "Lume не найден на этом компьютере.",
        ["confirmBox"] = "Удалить Lume и все его ярлыки?",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["title"] = "Uninstall Lume",
        ["confirm"] = "Remove Lume from this computer?",
        ["delData"] = "Also remove settings and wallpapers (the Documents library)",
        ["remove"] = "Uninstall",
        ["cancel"] = "Cancel",
        ["removing"] = "Removing…",
        ["done"] = "Lume has been removed.",
        ["notInstalled"] = "Lume was not found on this computer.",
        ["confirmBox"] = "Remove Lume and all its shortcuts?",
    };
}
