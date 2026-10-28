namespace AsrUi;

/// <summary>
/// Owns all transient files for one input file. The held lease lets the next application
/// launch distinguish a live job from an orphan left by a crash or power loss.
/// </summary>
internal sealed class TemporaryJobWorkspace : IDisposable
{
    private readonly FileStream _lease;
    private bool _cleaned;

    private TemporaryJobWorkspace(string directoryPath, FileStream lease)
    {
        DirectoryPath = directoryPath;
        _lease = lease;
    }

    public string DirectoryPath { get; }

    public string GetPath(string fileName) => Path.Combine(DirectoryPath, fileName);

    public static TemporaryJobWorkspace Create()
    {
        Directory.CreateDirectory(AppPaths.JobDirectory);
        var name = "job-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(AppPaths.JobDirectory, name);
        Directory.CreateDirectory(directory);
        try
        {
            var lease = new FileStream(
                Path.Combine(directory, ".active"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new TemporaryJobWorkspace(directory, lease);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public bool TryCleanup(out string? error)
    {
        error = null;
        if (_cleaned) return true;
        try
        {
            _lease.Dispose();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        Exception? lastFailure = null;
        // Antivirus/indexing can hold a just-closed WAV or SRT momentarily. Retry in-process first;
        // if Windows still refuses, startup recovery will retry only after the lock has gone away.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                TryDeleteDirectory(DirectoryPath);
                _cleaned = true;
                return true;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                if (attempt < 3) Thread.Sleep(150 * attempt);
            }
        }

        error = lastFailure?.Message;
        return false;
    }

    public void Dispose()
    {
        TryCleanup(out _);
    }

    internal static bool TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return true;
        Directory.Delete(directory, recursive: true);
        return true;
    }
}
