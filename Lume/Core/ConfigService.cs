using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VividDesk.Core;

/// <summary>Загрузка/сохранение config.json в %APPDATA%\VividDesk.</summary>
public class ConfigService
{
    public static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VividDesk");

    private static readonly string ConfigPath = Path.Combine(AppDataDir, "config.json");

    /// <summary>Язык из конфига без полной инициализации сервиса (нужен до старта приложения).</summary>
    public static string PeekLanguage()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return "ru";
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts);
            return cfg?.Language ?? "ru";
        }
        catch { return "ru"; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AppConfig Config { get; private set; } = new();

    public ConfigService()
    {
        Directory.CreateDirectory(AppDataDir);
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts);
                if (cfg != null) Config = cfg;
            }
        }
        catch { /* повреждённый конфиг — используем дефолт */ }

        string oldDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VividDesk Wallpapers");
        string newDefault = Path.Combine(AppContext.BaseDirectory, "wallpapers");
        bool unset = string.IsNullOrWhiteSpace(Config.WallpaperDir);
        bool legacy = string.Equals(Config.WallpaperDir, oldDefault, StringComparison.OrdinalIgnoreCase);
        if (unset || legacy)
        {
            if (legacy) MigrateLibrary(oldDefault, newDefault);
            Config.WallpaperDir = newDefault;
        }
    }

// One-time move of the old Documents library into the install folder.
    private static void MigrateLibrary(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.GetDirectories(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(dir));
                if (!Directory.Exists(dest)) Directory.Move(dir, dest);
            }
            foreach (var file in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(file));
                if (!File.Exists(dest)) File.Move(file, dest);
            }
            if (!Directory.EnumerateFileSystemEntries(from).Any()) Directory.Delete(from, true);
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, JsonOpts));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Config save failed: " + ex.Message); }
    }
}
