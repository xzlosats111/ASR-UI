using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AsrUi;

internal sealed record AudioTrackInfo(
    int AudioOrdinal,
    int StreamIndex,
    string Language,
    string Title,
    string Codec,
    int Channels,
    int SampleRate,
    bool IsDefault)
{
    public bool IsJapanese => Language.Equals("jpn", StringComparison.OrdinalIgnoreCase)
        || Language.Equals("ja", StringComparison.OrdinalIgnoreCase)
        || Language.StartsWith("ja-", StringComparison.OrdinalIgnoreCase);

    public string DisplayName
    {
        get
        {
            var language = string.IsNullOrWhiteSpace(Language) ? "未标注语言" : Language;
            var title = string.IsNullOrWhiteSpace(Title) ? string.Empty : " · " + Title;
            var channels = Channels > 0 ? $" · {Channels} 声道" : string.Empty;
            var rate = SampleRate > 0 ? $" · {SampleRate / 1000d:0.#} kHz" : string.Empty;
            var defaultMark = IsDefault ? " · 默认" : string.Empty;
            return $"第 {AudioOrdinal + 1} 条音轨 · {language} · {Codec}{channels}{rate}{defaultMark}{title}";
        }
    }
}

internal sealed class MediaProbeService
{
    public async Task<IReadOnlyList<AudioTrackInfo>> ProbeAudioTracksAsync(string ffprobeExecutable, string input)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffprobeExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-select_streams");
        start.ArgumentList.Add("a");
        start.ArgumentList.Add("-show_entries");
        start.ArgumentList.Add("stream=index,codec_name,sample_rate,channels,disposition:stream_tags=language,title");
        start.ArgumentList.Add("-of");
        start.ArgumentList.Add("json");
        start.ArgumentList.Add(input);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动 ffprobe.exe。");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(standardOutput, standardError, process.WaitForExitAsync());
        if (process.ExitCode != 0)
        {
            var detail = (await standardError).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? $"ffprobe 返回退出代码 {process.ExitCode}。" : detail);
        }

        using var document = JsonDocument.Parse(await standardOutput);
        if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return Array.Empty<AudioTrackInfo>();

        var tracks = new List<AudioTrackInfo>();
        var ordinal = 0;
        foreach (var stream in streams.EnumerateArray())
        {
            var streamIndex = GetInt(stream, "index");
            var codec = GetString(stream, "codec_name");
            var sampleRate = GetInt(stream, "sample_rate");
            var channels = GetInt(stream, "channels");
            var language = string.Empty;
            var title = string.Empty;
            if (stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            {
                language = GetString(tags, "language");
                title = GetString(tags, "title");
            }

            var isDefault = false;
            if (stream.TryGetProperty("disposition", out var disposition) && disposition.ValueKind == JsonValueKind.Object)
                isDefault = GetInt(disposition, "default") != 0;

            tracks.Add(new AudioTrackInfo(ordinal++, streamIndex, language, title, codec, channels, sampleRate, isDefault));
        }
        return tracks;
    }

    public static AudioTrackInfo? ChooseTrack(IReadOnlyList<AudioTrackInfo> tracks, int requestedOrdinal)
    {
        if (tracks.Count == 0) return null;
        if (requestedOrdinal >= 0)
            return tracks.FirstOrDefault(track => track.AudioOrdinal == requestedOrdinal);
        return tracks.FirstOrDefault(track => track.IsJapanese)
            ?? tracks.FirstOrDefault(track => track.IsDefault)
            ?? tracks[0];
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integer)) return integer;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed) ? parsed : 0;
    }
}
