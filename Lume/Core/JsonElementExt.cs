using System.Text.Json;

namespace VividDesk.Core;

/// <summary>Значение свойства из project.json → строка для настроек обоев.</summary>
public static class JsonElementExt
{
    public static string ToSettingString(this JsonElement? el)
    {
        if (!el.HasValue) return "";

        return el.Value.ValueKind switch
        {
            JsonValueKind.String => el.Value.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.Value.GetRawText(),
        };
    }
}
