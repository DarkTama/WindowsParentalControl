using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParentalControl.Agent;

public enum CornerPreset
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft
}

public sealed class WidgetSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CornerPreset CornerDock { get; set; } = CornerPreset.TopRight;
    public int TargetMonitorIndex { get; set; } = 1;
    public double FullscreenOpacity { get; set; } = 0.6;
    public bool AutoCollapseSingleScreen { get; set; } = true;
    public double LastUserX { get; set; } = -1;
    public double LastUserY { get; set; } = -1;

    public static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ParentalControl",
        "widget.json");

    public static WidgetSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<WidgetSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }
        return new WidgetSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
