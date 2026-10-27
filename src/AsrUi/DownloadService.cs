using System.IO.Compression;
using System.Net;

namespace AsrUi;

internal sealed class DownloadService
{
    public async Task DownloadAsync(
        ComponentDefinition component,
        bool useProxy,
        string proxyUrl,
        IProgress<(long downloaded, long? total)>? progress,
        CancellationToken cancellationToken)
    {
        if (component.IsInstalled()) return;

        Directory.CreateDirectory(component.IsArchive ? AppPaths.DownloadDirectory : AppPaths.ModelDirectory);
        var target = component.TargetPath;
        var temporary = target + ".part";
        if (File.Exists(temporary)) File.Delete(temporary);

        using var handler = new HttpClientHandler { UseProxy = useProxy };
        if (useProxy)
        {
            if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxyUri))
                throw new InvalidOperationException("代理地址无效。示例：http://127.0.0.1:10809");
            handler.Proxy = new WebProxy(proxyUri);
        }

        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ASR-UI/1.0");
        using var response = await client.GetAsync(component.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        var buffer = new byte[1024 * 128];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report((received, total));
        }
        await destination.FlushAsync(cancellationToken);

        var actual = new FileInfo(temporary).Length;
        if (actual < component.MinimumBytes)
        {
            File.Delete(temporary);
            throw new InvalidDataException($"下载文件异常偏小：{actual:N0} 字节。请检查代理、网络或上游链接。");
        }

        File.Move(temporary, target, true);
        if (component.IsArchive)
        {
            var extractDirectory = Path.Combine(AppPaths.BaseDirectory, component.ExtractFolder!);
            Directory.CreateDirectory(extractDirectory);
            ZipFile.ExtractToDirectory(target, extractDirectory, true);
        }
    }
}
