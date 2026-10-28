namespace AsrUi;

internal sealed record ComponentDefinition(
    string Id,
    string Title,
    string Size,
    string Description,
    string Source,
    string License,
    string Url,
    string TargetName,
    long MinimumBytes,
    bool DefaultSelected,
    bool IsArchive = false,
    string? ExtractFolder = null)
{
    public string TargetPath => IsArchive
        ? Path.Combine(AppPaths.DownloadDirectory, TargetName)
        : Path.Combine(AppPaths.ModelDirectory, TargetName);

    public bool IsInstalled()
    {
        if (Id == "crispasr_cuda126") return AppPaths.FindCrispAsr() is not null;
        if (Id == "ffmpeg") return AppPaths.FindFfmpeg() is not null;
        return File.Exists(TargetPath) && new FileInfo(TargetPath).Length >= MinimumBytes;
    }
}

internal static class ComponentCatalog
{
    public static readonly ComponentDefinition CrispAsrCuda126 = new(
        "crispasr_cuda126",
        "CrispASR CUDA 12.6 legacy 引擎",
        "680 MiB",
        "本地识别核心引擎。此 Windows CUDA 12.6 legacy 包适合 GTX 1080 Ti / Pascal，同时运行 Qwen Anime 和 Parakeet；不需要 Python、PyTorch 或 CUDA Toolkit。",
        "CrispStrobe/CrispASR v0.8.45 上游发布包",
        "以 CrispASR 上游仓库与压缩包内的许可证/声明为准。",
        "https://github.com/CrispStrobe/CrispASR/releases/download/v0.8.45/crispasr-windows-x86_64-cuda126.zip",
        "crispasr-windows-x86_64-cuda126.zip", 600_000_000, true, true, Path.Combine("runtime", "crispasr"));

    public static readonly ComponentDefinition QwenAnimeQ4 = new(
        "qwen_anime_q4",
        "Qwen 日语 Anime/Galgame ASR Q4_K（高质量）",
        "1.42 GiB",
        "专门面向日语动画、视觉小说和游戏对白微调的 Qwen3-ASR 1.7B 模型。Q4_K 是 GTX 1080 Ti 上推荐的质量、显存和下载量平衡点。",
        "cstr/qwen3-asr-1.7b-ja-anime-GGUF，固定修订 1906c1f",
        "Apache-2.0；以模型卡为准。",
        "https://huggingface.co/cstr/qwen3-asr-1.7b-ja-anime-GGUF/resolve/1906c1f812b598d5e9dae091c79656d73b41ec28/qwen3-asr-1.7b-ja-anime-q4_k.gguf",
        "qwen3-asr-1.7b-ja-anime-q4_k.gguf", 1_400_000_000, true);

    public static readonly ComponentDefinition QwenAlignerQ4 = new(
        "qwen_aligner_q4",
        "Qwen ForcedAligner Q4_K（Qwen 的 SRT 时间码）",
        "505 MiB",
        "将 Qwen 识别出的文本重新和音频对齐，生成可用 SRT 时间码。Qwen 高质量模式必需；Parakeet 快速模式不需要它。",
        "cstr/qwen3-forced-aligner-0.6b-GGUF，固定修订 1ec5110",
        "Apache-2.0；以模型卡为准。",
        "https://huggingface.co/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/1ec5110602ccab18c878ddebedab0891e290a95c/qwen3-forced-aligner-0.6b-q4_k.gguf",
        "qwen3-forced-aligner-0.6b-q4_k.gguf", 500_000_000, true);

    public static readonly ComponentDefinition ParakeetJaQ8 = new(
        "parakeet_ja_q8",
        "Parakeet Japanese Q8_0（快速、原生时间码）",
        "642 MiB",
        "日语专用 Parakeet TDT/CTC 模型。它有原生时间码，是快速批量 SRT 的默认模式；程序使用 CTC 解码以取得更稳定的长视频边界时间码。",
        "cstr/parakeet-tdt-0.6b-ja-GGUF，固定修订 d9e3ba6",
        "CC-BY-4.0；使用时需遵守上游署名要求。",
        "https://huggingface.co/cstr/parakeet-tdt-0.6b-ja-GGUF/resolve/d9e3ba65a6579796389ea89e5939509ed257f972/parakeet-tdt-0.6b-ja-q8_0.gguf",
        "parakeet-tdt-0.6b-ja-q8_0.gguf", 630_000_000, true);

    public static readonly ComponentDefinition Ffmpeg = new(
        "ffmpeg",
        "FFmpeg LGPL 媒体转换器",
        "约 170 MiB",
        "FFmpeg 把视频、FLV 和不兼容音频先转为临时 16 kHz 单声道 WAV；同包的 ffprobe 会读取音轨语言标记，自动优先选择日语音轨。WhisperDesktop v3 也会使用同目录的 FFmpeg 做 FLV 回退。", 
        "BtbN FFmpeg Builds，Windows x64 LGPL 最新包",
        "LGPL；解压包中附带的许可证文件必须保留。",
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip",
        "ffmpeg-master-latest-win64-lgpl.zip", 30_000_000, true, true, Path.Combine("runtime", "ffmpeg"));

    public static readonly ComponentDefinition QwenAnimeQ8 = new(
        "qwen_anime_q8",
        "可选：Qwen 日语 Anime/Galgame ASR Q8_0",
        "2.39 GiB",
        "Qwen Anime 的更高精度版本，但下载和显存开销明显更高。先使用 Q4_K；只有在确实需要 A/B 精度对比时再下载。",
        "cstr/qwen3-asr-1.7b-ja-anime-GGUF，固定修订 1906c1f",
        "Apache-2.0；以模型卡为准。",
        "https://huggingface.co/cstr/qwen3-asr-1.7b-ja-anime-GGUF/resolve/1906c1f812b598d5e9dae091c79656d73b41ec28/qwen3-asr-1.7b-ja-anime-q8_0.gguf",
        "qwen3-asr-1.7b-ja-anime-q8_0.gguf", 2_300_000_000, false);

    public static readonly ComponentDefinition WhisperLargeV3Q5 = new(
        "whisper_large_v3_q5",
        "可选：Whisper large-v3 Q5_0（完整旧版模式）",
        "约 1.01 GiB",
        "供完整 WhisperDesktop v3 使用。需要旧版的语言、输出类型、麦克风录音、GPU 高级控制或 Whisper 翻译时，再下载此模型。",
        "ggerganov/whisper.cpp 官方转换模型仓库",
        "以 Whisper 上游模型条款为准。",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-q5_0.bin",
        "ggml-large-v3-q5_0.bin", 1_000_000_000, false);

    public static IReadOnlyList<ComponentDefinition> All { get; } = new[]
    {
        CrispAsrCuda126, QwenAnimeQ4, QwenAlignerQ4, ParakeetJaQ8, Ffmpeg, QwenAnimeQ8, WhisperLargeV3Q5
    };
}
