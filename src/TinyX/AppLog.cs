namespace TinyX;

internal static class AppLog
{
    private const long MaximumBytes = 1_048_576;
    private static readonly object Sync = new();
    private static string? _path;

    public static void Initialize(bool smoke)
    {
        try
        {
            string directory = Path.Combine(AppSettings.DataDirectory, "logs");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, smoke ? "smoke.log" : "tinyx.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }
    }

    public static void Info(string action, string? detail = null) => Write("INFO", action, detail);

    public static void Error(string action, Exception? exception) =>
        Write("ERROR", action, exception is null ? "unknown" :
            exception.GetType().Name + ";hresult=" + exception.HResult.ToString("X8"));

    private static void Write(string level, string action, string? detail)
    {
        lock (Sync)
        {
            if (_path is null) return;
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length >= MaximumBytes)
                    File.Move(_path, _path + ".1", true);
                File.AppendAllText(_path,
                    $"{DateTimeOffset.Now:O} {level} {action} {detail}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never prevent hiding or exiting the window.
            }
        }
    }
}
