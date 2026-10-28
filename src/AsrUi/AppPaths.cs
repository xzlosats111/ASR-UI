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
    // Every conversion/transcription job receives one owned workspace below this directory.
    // Do not use Path.GetTempPath(): that scatters multi-gigabyte WAV files in the Windows user temp folder.
    public static string JobDirectory => Path.Combine(CacheDirectory, "jobs");
    // Persistent engine cache, separated from disposable per-file workspaces.
    public static string CrispAsrCacheDirectory => Path.Combine(CacheDirectory, "crispasr");
    // Legacy v0.1.4-v0.1.6 staging location. New jobs stage inside JobDirectory instead.
    public static string OutputStagingDirectory => Path.Combine(CacheDirectory, "output");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void ConfigureModelDirectory(string? directory)
    {
        _modelDirectory = string.IsNullOrWhiteSpace(directory)
            ? DefaultModelDirectory
            : Path.GetFullPath(directory.Trim());
    }

    public static void EnsureDirectories()
    {
        foreach (var path in new[] { DataDirectory, DownloadDirectory, RuntimeDirectory, ModelDirectory, CacheDirectory, JobDirectory, CrispAsrCacheDirectory, OutputStagingDirectory })
            Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Remove only abandoned per-job workspaces. A live job keeps .active open with FileShare.None,
    /// so a second ASR-UI instance cannot mistake it for garbage. Download .part files are deliberately
    /// excluded: they are resumable downloads, not disposable conversion temp files.
    /// </summary>
    public static int CleanupOrphanedWorkspaces()
    {
        var removed = 0;
        if (Directory.Exists(JobDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(JobDirectory, "job-*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var leasePath = Path.Combine(directory, ".active");
                    using var probe = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    probe.Dispose();
                    TemporaryJobWorkspace.TryDeleteDirectory(directory);
                    removed++;
                }
                catch
                {
                    // The directory belongs to a live ASR-UI process, is temporarily locked by Windows,
                    // or cannot be removed now. Never disrupt an active transcription.
                }
            }
        }

        // Clean only stale files from the old shared staging folder. A recent file may belong to an
        // older application instance that is still finishing, so keep it for 24 hours.
        if (Directory.Exists(OutputStagingDirectory))
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);
            foreach (var file in Directory.EnumerateFiles(OutputStagingDirectory, "asr-*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch { /* A locked file is not garbage yet. */ }
            }
        }

        return removed;
    }

    public static string? FindCrispAsr() => FindFile(RuntimeDirectory, "crispasr.exe");

    public static string? FindFfmpeg()
    {
        var bundled = Path.Combine(BaseDirectory, "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : FindFile(RuntimeDirectory, "ffmpeg.exe");
    }

    public static string? FindFfprobe()
    {
        var bundled = Path.Combine(BaseDirectory, "ffprobe.exe");
        return File.Exists(bundled) ? bundled : FindFile(RuntimeDirectory, "ffprobe.exe");
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
    // -1 means automatic Japanese/default/first-audio selection; 0+ is the audio ordinal selected by the user.
    public int AudioTrackOrdinal { get; set; } = -1;
    public bool TranslationPrioritySegmentation { get; set; } = true;
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
