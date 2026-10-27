using System.Text.Json;

namespace AsrUi;

internal static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;
    public static string DataDirectory => Path.Combine(BaseDirectory, "data");
    public static string DownloadDirectory => Path.Combine(DataDirectory, "downloads");
    public static string RuntimeDirectory => Path.Combine(BaseDirectory, "runtime");
    public static string DefaultModelDirectory => Path.Combine(BaseDirectory, "models");
    private static string _modelDirectory = DefaultModelDirectory;
    public static string ModelDirectory => _modelDirectory;
    public static string CacheDirectory => Path.Combine(BaseDirectory, "cache");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void ConfigureModelDirectory(string? directory)
    {
        _modelDirectory = string.IsNullOrWhiteSpace(directory)
            ? DefaultModelDirectory
            : Path.GetFullPath(directory.Trim());
    }

    public static void EnsureDirectories()
    {
        foreach (var path in new[] { DataDirectory, DownloadDirectory, RuntimeDirectory, ModelDirectory, CacheDirectory })
            Directory.CreateDirectory(path);
    }

    public static string? FindCrispAsr() => FindFile(RuntimeDirectory, "crispasr.exe");

    public static string? FindFfmpeg()
    {
        var bundled = Path.Combine(BaseDirectory, "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : FindFile(RuntimeDirectory, "ffmpeg.exe");
    }

    public static string? FindLegacyWhisperDesktop()
    {
        var direct = Path.Combine(BaseDirectory, "WhisperDesktop.exe");
        return File.Exists(direct) ? direct : FindFile(BaseDirectory, "WhisperDesktop.exe", RuntimeDirectory);
    }

    private static string? FindFile(string root, string fileName, string? excludedRoot = null)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .FirstOrDefault(p => excludedRoot is null || !p.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException) { return null; }
    }
}

internal sealed class AppSettings
{
    public bool UseProxy { get; set; } = true;
    public string ProxyUrl { get; set; } = "http://127.0.0.1:10809";
    public bool OutputBesideSource { get; set; } = true;
    public string OutputFolder { get; set; } = string.Empty;
    public string ModelDirectory { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
