using System.Text.Json;

namespace TinyX;

internal sealed class AppSettings
{
    public const int MinimumWidth = 280;
    public const int MinimumHeight = 260;
    private const int MaximumLogicalSize = 8192;
    private const int MaximumLogicalOffset = 32768;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ContentMode { get; set; } = true;
    public string? Monitor { get; set; }
    public int? Left { get; set; }
    public int? Top { get; set; }
    public int Width { get; set; } = 400;
    public int Height { get; set; } = 600;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyX");

    public static AppSettings Load()
    {
        try
        {
            string path = Path.Combine(DataDirectory, "settings.json");
            if (File.Exists(path))
            {
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("settings-load-failed", ex);
        }
        return new AppSettings();
    }

    private void Normalize()
    {
        Width = Math.Clamp(Width, MinimumWidth, MaximumLogicalSize);
        Height = Math.Clamp(Height, MinimumHeight, MaximumLogicalSize);
        if (Left is < 0 or > MaximumLogicalOffset || Top is < 0 or > MaximumLogicalOffset ||
            !Left.HasValue || !Top.HasValue)
        {
            Left = null;
            Top = null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string path = Path.Combine(DataDirectory, "settings.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("settings-save-failed", ex);
        }
    }
}
