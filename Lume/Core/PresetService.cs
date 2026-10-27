using System.IO;
using System.Text.Json;

namespace VividDesk.Core;

/// <summary>Пресеты: экспорт/импорт всей конфигурации (обои на каждом мониторе + все настройки + плейлист).</summary>
public class PresetService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConfigService _config;
    private readonly WallpaperManager _manager;

    public PresetService(ConfigService config, WallpaperManager manager)
    {
        _config = config;
        _manager = manager;
    }

    public void Export(string path, string name)
    {
        var preset = new PresetData
        {
            Name = name,
            Monitors = _config.Config.Monitors.Select(m => new MonitorConfig
            {
                DeviceId = m.DeviceId,
                WallpaperPath = m.WallpaperPath,
                Settings = m.Settings.Clone()
            }).ToList(),
            Playlist = JsonSerializer.Deserialize<PlaylistConfig>(
                JsonSerializer.Serialize(_config.Config.Playlist)) ?? new PlaylistConfig()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOpts));
    }

    /// <summary>Импорт пресета: заменяет конфиг мониторов и применяет обои немедленно.</summary>
    public void Import(string path)
    {
        var preset = JsonSerializer.Deserialize<PresetData>(File.ReadAllText(path), JsonOpts);
        if (preset == null) throw new InvalidDataException("Не удалось прочитать пресет");

        _config.Config.Monitors = preset.Monitors.Select(m => new MonitorConfig
        {
            DeviceId = m.DeviceId,
            WallpaperPath = m.WallpaperPath,
            Settings = m.Settings.Clone()
        }).ToList();
        _config.Config.Playlist = preset.Playlist ?? new PlaylistConfig();
        _config.Save();

        // снимаем текущие и применяем пресет
        foreach (var screen in WpfScreen.AllScreens)
            _manager.Clear(screen);
        _manager.RestoreFromConfig();
    }
}
