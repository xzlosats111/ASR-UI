using System.Diagnostics;
using System.IO.Compression;
using System.Net;

namespace AsrUi;

internal sealed class DownloadService
{
    private const int HttpAttempts = 5;

    public async Task DownloadAsync(
        ComponentDefinition component,
        bool useProxy,
        string proxyUrl,
        IProgress<(long downloaded, long? total)>? progress,
        Action<string>? reportStatus,
        CancellationToken cancellationToken)
    {
        if (component.IsInstalled()) return;

        Directory.CreateDirectory(component.IsArchive ? AppPaths.DownloadDirectory : AppPaths.ModelDirectory);
        var target = component.TargetPath;
        var temporary = target + ".part";
        Exception? lastFailure = null;
        var downloaded = false;

        // Hugging Face/Xet files are large and some local proxy implementations
        // close an otherwise valid response early. Keep the .part file and resume
        // with Range requests instead of throwing away hundreds of MiB.
        for (var attempt = 1; attempt <= HttpAttempts && !downloaded; attempt++)
        {
            try
            {
                var previous = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
                if (previous > 0)
                    reportStatus?.Invoke($"下载连接重试 {attempt}/{HttpAttempts}，从 {FormatBytes(previous)} 继续：{component.Title}");
                else if (attempt > 1)
                    reportStatus?.Invoke($"下载连接重试 {attempt}/{HttpAttempts}：{component.Title}");

                await DownloadHttpAttemptAsync(component.Url, temporary, previous, useProxy, proxyUrl, progress, cancellationToken);
                downloaded = true;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < HttpAttempts)
            {
                lastFailure = ex;
                var partial = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
                reportStatus?.Invoke($"连接中断：{ex.Message}。已保留 {FormatBytes(partial)}，{2 * attempt} 秒后自动继续。");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                break;
            }
        }

        // Windows 10/11 normally includes curl.exe. It is used only if .NET's
        // HTTP stream still fails, because curl's proxy and resume behaviour is
        // often more tolerant of long Hugging Face/Xet downloads.
        if (!downloaded && FindCurl() is { } curl)
        {
            try
            {
                reportStatus?.Invoke(".NET 下载多次中断，正在用 Windows curl 继续未完成下载...");
                await DownloadWithCurlAsync(curl, component.Url, temporary, useProxy, proxyUrl, reportStatus, cancellationToken);
                downloaded = true;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }
        }

        if (!downloaded)
        {
            var partial = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
            throw new IOException($"下载在 {HttpAttempts} 次自动重试后仍未完成。已保留 {FormatBytes(partial)} 的 .part 文件，下次点击下载会从断点继续。最后错误：{lastFailure?.Message}", lastFailure);
        }

        var actual = new FileInfo(temporary).Length;
        if (actual < component.MinimumBytes)
            throw new InvalidDataException($"下载文件异常偏小：{actual:N0} 字节。已保留 .part 文件以便重试。请检查代理、网络或上游链接。");

        File.Move(temporary, target, true);
        if (component.IsArchive)
        {
            var extractDirectory = Path.Combine(AppPaths.BaseDirectory, component.ExtractFolder!);
            Directory.CreateDirectory(extractDirectory);
            ZipFile.ExtractToDirectory(target, extractDirectory, true);
        }
    }

    private static async Task DownloadHttpAttemptAsync(
        string url,
        string temporary,
        long existingBytes,
        bool useProxy,
        string proxyUrl,
        IProgress<(long downloaded, long? total)>? progress,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = useProxy,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None
        };
        if (useProxy)
        {
            if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxyUri))
                throw new InvalidOperationException("代理地址无效。示例：http://127.0.0.1:10809");
            handler.Proxy = new WebProxy(proxyUri.ToString());
        }

        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ASR-UI/1.1");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var acceptsRange = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var writeStart = acceptsRange ? existingBytes : 0;
        var expectedTotal = response.Content.Headers.ContentRange?.Length
            ?? (response.Content.Headers.ContentLength is long length ? writeStart + length : null);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            temporary,
            acceptsRange ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            true);

        var buffer = new byte[1024 * 128];
        long writtenThisAttempt = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            writtenThisAttempt += read;
            progress?.Report((writeStart + writtenThisAttempt, expectedTotal));
        }
        await destination.FlushAsync(cancellationToken);

        var actual = writeStart + writtenThisAttempt;
        if (expectedTotal is long total && actual < total)
            throw new EndOfStreamException($"传输提前结束：收到 {FormatBytes(actual)}，应为 {FormatBytes(total)}。");
    }

    private static async Task DownloadWithCurlAsync(
        string curl,
        string url,
        string temporary,
        bool useProxy,
        string proxyUrl,
        Action<string>? reportStatus,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = curl,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("--fail");
        start.ArgumentList.Add("--location");
        start.ArgumentList.Add("--retry");
        start.ArgumentList.Add("5");
        start.ArgumentList.Add("--retry-all-errors");
        start.ArgumentList.Add("--continue-at");
        start.ArgumentList.Add("-");
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(temporary);
        if (useProxy)
        {
            start.ArgumentList.Add("--proxy");
            start.ArgumentList.Add(proxyUrl);
        }
        start.ArgumentList.Add(url);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动 Windows curl.exe。");
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new IOException($"curl 返回退出代码 {process.ExitCode}：{detail.Trim()}");
        }
        reportStatus?.Invoke("Windows curl 已完成下载，正在验证文件大小...");
    }

    private static bool IsRetryable(Exception ex) => ex is HttpRequestException or IOException or TaskCanceledException;

    private static string? FindCurl()
    {
        var systemCurl = Path.Combine(Environment.SystemDirectory, "curl.exe");
        return File.Exists(systemCurl) ? systemCurl : null;
    }

    private static string FormatBytes(long bytes)
    {
        var mib = bytes / 1024d / 1024d;
        return mib >= 1024 ? $"{mib / 1024d:F2} GiB" : $"{mib:F1} MiB";
    }
}
