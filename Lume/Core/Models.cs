using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VividDesk.Core;

public enum WallpaperType { Video, Web, Url, Image }

/// <summary>Обои: папка проекта с project.json (веб) или видео-файл.</summary>
public class WallpaperProject
{
    [JsonPropertyName("title")] public string Title { get; set; } = "Без названия";
    [JsonPropertyName("type")] public string Type { get; set; } = "web"; // "video" | "web" | "url"
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("preview")] public string Preview { get; set; } = "preview.png";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("properties")] public List<WallpaperProperty> Properties { get; set; } = new();

    [JsonIgnore] public string FolderPath { get; set; } = "";

    [JsonIgnore]
    public WallpaperType Kind => Type == "video" ? WallpaperType.Video
        : Type == "url" ? WallpaperType.Url
        : Type == "image" ? WallpaperType.Image
        : WallpaperType.Web;

    [JsonIgnore] public string PreviewPath => Path.Combine(FolderPath, Preview);

    /// <summary>Почему обои не запустятся: пусто, "unsupported" или "missing".</summary>
    [JsonIgnore] public string Problem { get; set; } = "";

    public WallpaperProject Clone()
    {
        var copy = JsonSerializer.Deserialize<WallpaperProject>(JsonSerializer.Serialize(this));
        copy.FolderPath = FolderPath;
        return copy;
    }
}

/// <summary>Свойство обоев, объявленное в project.json (слайдер/цвет/флажок/список).</summary>
public class WallpaperProperty
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "slider"; // slider | color | checkbox | select
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("step")] public double? Step { get; set; }
    [JsonPropertyName("default")] public JsonElement? Default { get; set; }
    [JsonPropertyName("options")] public List<PropertyOption> Options { get; set; } = new();
}

/// <summary>Вариант списка — принимается и строкой, и объектом { value, label }.</summary>
[JsonConverter(typeof(PropertyOptionConverter))]
public class PropertyOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";

    public PropertyOption() { }

    public PropertyOption(string v) { Value = Label = v; }
}

public class PropertyOptionConverter : JsonConverter<PropertyOption>
{
    public override PropertyOption Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new PropertyOption(reader.GetString());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            string value = "", label = "";
            if (doc.RootElement.TryGetProperty("value", out var v)) value = v.ToString();
            if (doc.RootElement.TryGetProperty("label", out var l)) label = l.ToString();
            if (string.IsNullOrEmpty(label)) label = value;
            return new PropertyOption { Value = value, Label = label };
        }

        return new PropertyOption();
    }

    public override void Write(Utf8JsonWriter writer, PropertyOption value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// <summary>Пользовательские настройки конкретных обоев (скорость, яркость, звук…).</summary>
public class WallpaperSettings
{
    [JsonPropertyName("userProperties")] public Dictionary<string, string> UserProperties { get; set; } = new();
    [JsonPropertyName("rate")] public double Rate { get; set; } = 1.0;          // 0.25..3
    [JsonPropertyName("brightness")] public double Brightness { get; set; } = 1.0; // 0..2
    [JsonPropertyName("contrast")] public double Contrast { get; set; } = 1.0;     // 0..2
    [JsonPropertyName("saturation")] public double Saturation { get; set; } = 1.0; // 0..3
    [JsonPropertyName("volume")] public double Volume { get; set; } = 0.6;     // 0..1
    [JsonPropertyName("muted")] public bool Muted { get; set; } = true;        // по умолчанию звук выключен
    [JsonPropertyName("fpsLimit")] public int FpsLimit { get; set; } = 0;      // 0 = без лимита

    public WallpaperSettings Clone()
    {
        var copy = (WallpaperSettings)MemberwiseClone();
        copy.UserProperties = new Dictionary<string, string>(UserProperties);
        return copy;
    }
}

/// <summary>Какие обои и с какими настройками стоят на конкретном мониторе.</summary>
public class MonitorConfig
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("wallpaperPath")] public string WallpaperPath { get; set; } = "";
    [JsonPropertyName("settings")] public WallpaperSettings Settings { get; set; } = new();
}

/// <summary>Автосмена обоев по таймеру и по расписанию.</summary>
public class PlaylistConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
    [JsonPropertyName("intervalMinutes")] public int IntervalMinutes { get; set; } = 15;
    [JsonPropertyName("order")] public string Order { get; set; } = "sequential"; // sequential | random
    [JsonPropertyName("items")] public List<string> Items { get; set; } = new(); // пути к папкам проектов
    [JsonPropertyName("useSchedule")] public bool UseSchedule { get; set; } = false;
    [JsonPropertyName("from")] public string From { get; set; } = "09:00";
    [JsonPropertyName("to")] public string To { get; set; } = "21:00";
}

/// <summary>Конфигурация приложения целиком — %APPDATA%\VividDesk\config.json.</summary>
public class AppConfig
{
    [JsonPropertyName("wallpaperDir")] public string WallpaperDir { get; set; } = "";
    [JsonPropertyName("soundEnabled")] public bool SoundEnabled { get; set; } = false;
    [JsonPropertyName("autoPauseOnFullscreen")] public bool AutoPauseOnFullscreen { get; set; } = true;
    [JsonPropertyName("pauseAction")] public string PauseAction { get; set; } = "pause"; // pause | stop
    [JsonPropertyName("startWithSystem")] public bool StartWithSystem { get; set; } = false;
    [JsonPropertyName("startMinimized")] public bool StartMinimized { get; set; } = false;
    [JsonPropertyName("audioCapture")] public bool AudioCapture { get; set; } = true;
    [JsonPropertyName("language")] public string Language { get; set; } = "ru"; // ru | en
    [JsonPropertyName("uiScale")] public double UiScale { get; set; } = 0.75;   // масштаб интерфейса
    [JsonPropertyName("lockScreen")] public bool LockScreen { get; set; } = false;
    [JsonPropertyName("wallpaperScale")] public double WallpaperScale { get; set; } = 1.0; // масштаб содержимого обоев (0.5..1.5) // кадр обоев на экран блокировки
    [JsonPropertyName("liveLock")] public bool LiveLock { get; set; } = false;
    [JsonPropertyName("liveLockMinutes")] public int LiveLockMinutes { get; set; } = 5;
    [JsonPropertyName("liveLockLockOnExit")] public bool LiveLockLockOnExit { get; set; } = true;
    [JsonPropertyName("monitors")] public List<MonitorConfig> Monitors { get; set; } = new();
    [JsonPropertyName("playlist")] public PlaylistConfig Playlist { get; set; } = new();
}

/// <summary>Пресет — экспортируемая конфигурация (обои на мониторах + все настройки + плейлист).</summary>
public class PresetData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Пресет";
    [JsonPropertyName("monitors")] public List<MonitorConfig> Monitors { get; set; } = new();
    [JsonPropertyName("playlist")] public PlaylistConfig Playlist { get; set; } = new();
}
