# WhisperDesktop：简体中文日语字幕版

这是基于 `const-me/whisper` 的 Windows DirectCompute GUI 改版；底层使用 `sffxzzp/WhisperDesktop` 的量化模型支持，因此可使用 `ggml-large-v3-q5_0.bin`，也可使用同系列 q4/q8 模型。GTX 1080 Ti 使用 **GPU** 模型实现即可。

## 本版行为

- 所有中文 C++ 界面源码保留 UTF-8 BOM，避免 MSVC 将中文字符串编译成乱码；
- 每次启动先显示 **加载 Whisper 模型** 窗口，已记住的模型路径会预填，但不会自动加载；
- 默认语言为**日语**、默认输出为 **SubRip（.srt）**，但语言、翻译、字幕类型、输出位置均可自行选择；
- 保留全部原有界面与功能：模型实现/GPU 高级设置、调试控制台、文件转写、实时麦克风转写、语言选择、翻译、文本/SRT/WebVTT 输出；
- 媒体选择器加入 AVI、MP4、MKV、MOV、MPEG、WebM、FLV、WMV、WAV、MP3、M4A、AAC、FLAC、OGG 等常见格式；
- 文件选择窗口支持按 **Ctrl / Shift 多选**；多选后会依次转写，每个文件输出同名字幕到各自源文件所在目录；单个文件失败会被记录并跳过，后续文件继续处理。
- Windows Media Foundation 无法正常解码 FLV 时，会自动调用成品包内同目录的 `ffmpeg.exe` 转换为临时 WAV 后再转写；GitHub Actions 会将该文件一并放入 `WhisperDesktop-x64`。

## 日语 SRT 的常用操作

1. 启动 `WhisperDesktop.exe`，选择模型文件，例如 `ggml-large-v3-q5_0.bin`。
2. 在“转写音频文件”窗口确认：语言为“日语”、输出格式为“SubRip 字幕”。
3. 点击 `...` 选择一个或多个视频/音频文件；多选时请保持“输出至源文件位置”勾选。
4. 点击“转写”。同目录会生成 `.srt` 文件。

Whisper 的内置“翻译”只翻译为英语；要中文 SRT，请先生成日文 SRT，再用翻译工具翻译文本并保留时间码。

## 模型校验

官方 `ggml-large-v3-q5_0.bin` 的 SHA1：

```text
e6e2ed78495d403bef4b7cff42ef4aaadcfea8de
```

Windows 校验命令：

```bat
certutil -hashfile ggml-large-v3-q5_0.bin SHA1
```

## 日语字幕整合工具（Qwen / Parakeet / WhisperDesktop v3）

GitHub Actions 的 `WhisperDesktop-x64` 成品现在会额外带有 `JapaneseSubtitleHub` 文件夹。运行其中的 `Start-JapaneseSubtitleHub.cmd`，可使用一个简单中文界面完成：

- **高质量模式**：Qwen Japanese Anime/Galgame Q4_K + Qwen ForcedAligner Q4_K，输出 `.qwen-ja.srt`；
- **快速模式**：Parakeet Japanese Q8_0（原生时间码），输出 `.parakeet-ja.srt`；
- **图形化下载列表**：显示每个引擎 / 模型用途、大小、上游来源和许可证；默认通过 `http://127.0.0.1:10809` 下载，可关闭代理直连；
- **完整 v3 仍保留**：整合工具的“Full WhisperDesktop v3”页会打开未删减的原界面，而不是用新工具替代它。

默认勾选下载约 3.4 GiB 的上游组件；模型不进入 Git 仓库。详细步骤、空间说明和第三方许可见 `JapaneseSubtitleHub\README_整合包.md`。

## 构建或 GitHub Actions

仓库内 `.github/workflows/build.yml` 会在推送 `master` 分支后自动用 Windows 构建。完成后在 GitHub Actions 下载 artifact：`WhisperDesktop-x64`。

本地构建需要 Windows 10/11、Visual Studio 2022（“使用 C++ 的桌面开发”）和 .NET SDK；在源码根目录运行：

```bat
build_windows.cmd
```

生成的 `WhisperDesktop.exe` 与 `Whisper.dll` 必须放在同一文件夹。
