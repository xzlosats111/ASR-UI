# ASR-UI：日语字幕工作台

一个**新的 Windows 简体中文 GUI**，把两种较适合日语字幕的本地 ASR 路线放在同一个工作台中，同时保留完整的 WhisperDesktop v3 作为“完整旧版”入口。

这个仓库不是 WhisperDesktop 的界面补丁：新界面采用独立的 .NET WinForms 设计，使用侧栏工作台布局；旧版功能作为兼容模块保留，不强迫新 UI 沿用旧 UI 的布局或交互。

## 两种核心模式

| 模式 | 本地模型 / 引擎 | SRT 时间码 | 适用情况 | 输出名 |
|---|---|---|---|---|
| **高质量** | CrispASR CUDA 12.6 legacy + Qwen 日语 Anime/Galgame Q4_K + Qwen ForcedAligner Q4_K | 强制对齐 | 动画、视觉小说、游戏对白、专有名词较多的内容 | `文件名.qwen-ja.srt` |
| **快速** | CrispASR CUDA 12.6 legacy + Parakeet Japanese Q8_0 | 原生时间码 | 快速批量出日语 SRT | `文件名.parakeet-ja.srt` |
| **完整旧版** | 保留的 WhisperDesktop v3 + Whisper large-v3 Q5_0（可选） | Whisper 分段 | 需要完整原有功能时 | 原程序决定 |

Qwen 和 Parakeet 使用同一个 CrispASR CUDA 运行时，因此不需要再额外装 Python、PyTorch、CUDA Toolkit 或另一个 speech.cpp 运行时。Qwen Anime/Galgame 是模型预设名称，运行时实际使用 CrispASR 的 `qwen3` 后端。

## 新 UI 的设计原则

- **一页只做一件事**：日语字幕、组件下载、完整旧版、运行日志四个页面；
- **默认可用**：默认启用 `http://127.0.0.1:10809` HTTP 代理；可一键关闭改为直连；
- **模型目录可选**：可把模型切换到空间更大的磁盘；路径会保存，下次启动继续使用；
- **大文件断点续传**：Hugging Face / Xet 下载中断时保留 `.part` 文件，自动最多重试 5 次并用 Range 续传；仍失败时会调用 Windows 自带 `curl.exe` 再次续传；
- **下载内容说清楚**：每个组件都显示用途、大小、上游来源、许可证和安装状态；
- **批量不因一个文件中断**：任何转换或识别失败都会记录原因并继续后面的文件；
- **中文/日文路径可输出**：CrispASR 先写入 ASCII 临时目录，再由应用复制到实际字幕目录，避免 v0.8.45 Windows SRT writer 无法直接写入中文/日文路径；
- **自动优先日语音轨**：随发布包提供 `ffprobe.exe`；选择输入后可查看首个文件音轨，批处理时每个文件优先选 `jpn` / `ja`，也可指定第几条音轨；
- **真实识别进度**：CrispASR 使用 `-pp` 回报已完成 VAD 片段 / 总片段；Qwen 进度包含每个片段的识别和强制对齐，不再等待最后集中输出字幕才跳变；
- **翻译优先分段**：可合并短停顿并增加前后音频留白，增强 Qwen 上下文；平衡分段仍可一键切回；
- **保守时间轴清理**：自动处理零时长、重复零时长、小重叠和“极短文本占用异常长时段”，大重叠会保留原始时间而不盲目移动；
- **不覆盖 A/B 结果**：Qwen 和 Parakeet 各自使用不同 SRT 后缀；
- **功能不删**：完整旧版仍有启动时模型选择、语言/输出类型、GPU 高级设置、实时麦克风、多选批处理和 FLV 回退。

## 默认下载清单

默认勾选约 **3.4 GiB**：

| 组件 | 大小 | 作用 |
|---|---:|---|
| CrispASR Windows CUDA 12.6 legacy | 680 MiB | GTX 1080 Ti / Pascal 的本地 C++ ASR 运行时 |
| Qwen 日语 Anime/Galgame Q4_K | 1.42 GiB | 高质量日语动画 / galgame 转写 |
| Qwen ForcedAligner Q4_K | 505 MiB | Qwen 文本的 SRT 时间码 |
| Parakeet Japanese Q8_0 | 642 MiB | 快速日语 SRT |
| FFmpeg LGPL | 约 170 MiB | 将视频、FLV 和媒体转换为临时 WAV |

另有可选 Qwen Q8_0（约 2.39 GiB）和 Whisper large-v3 Q5_0（约 1.01 GiB）。请在模型所在磁盘至少保留 **6 GiB** 空闲空间，并按输入视频大小为临时 WAV 额外留空间。

模型和引擎由应用从上游直接下载至 `models` / `runtime`，均已列在 `.gitignore` 中，**绝不提交到仓库、Git 历史或 Actions artifact**。

## 用户使用方式

1. 从 GitHub Actions 下载完整 `ASR-UI-win-x64` artifact 并解压；不要单独复制 EXE。
2. 运行 `ASR-UI.exe`。
3. 在“组件与下载”确认默认代理或关闭代理，然后下载默认勾选项。
4. 在“日语字幕”选择高质量或快速模式，选择一个或多个媒体文件；确认“音轨”是自动优先日语，或按需指定第 N 条音轨。
5. 默认勾选“翻译优先”，适合把完整 SRT 交给机器翻译 / 大模型；若需回到原有细分 VAD 策略可取消勾选。
6. 开始生成 SRT；识别阶段会显示 CrispASR 实际完成的 `已完成片段 / 总片段` 进度。
7. 建议首次用同一个短片跑两种模式，再比较 `*.qwen-ja.srt` 与 `*.parakeet-ja.srt`。
8. 需要旧版全部功能时，进入“完整旧版”并打开 WhisperDesktop v3。

## 源码目录与首次推送

```text
src/AsrUi/                         新的独立中文 WinForms 工作台
legacy/WhisperDesktopSource/       已保留的 WhisperDesktop v3 源码与功能
.github/workflows/build.yml        同时构建新 GUI 和完整旧版，打包成一个 artifact
push-asr-ui.cmd                      常规 ASCII 推送脚本，走 127.0.0.1:10809
推送到ASR-UI.cmd                     同一脚本的中文文件名入口
scripts/PublishCleanSource.ps1       全量源码覆盖后使用的干净重建/推送工具
scripts/PublishCleanSource.cmd       上一工具的 ASCII CMD 入口
```

若从源码 ZIP 解压而不是 Git 克隆，根目录 `push-asr-ui.cmd` 会在没有 `.git` 时初始化本地 Git、配置代理、把远端设为 `https://github.com/xzlosats111/ASR-UI.git`、使用 `main` 分支并推送。若曾误加入 artifact、模型、EXE 或大文件，请改用 `scripts\PublishCleanSource.cmd`：它会以远端 `main` 为索引基准重新暂存源码，遵守 `.gitignore`，并自动排除任何大于 5 MiB 的误暂存文件。首次使用 Git 的电脑需要先配置一次 `user.name` 和 `user.email`。

## 构建

GitHub Actions 在 `main` 分支推送后运行。它会：

1. 构建 `legacy/WhisperDesktopSource` 的 WhisperDesktop v3；
2. 发布 `src/AsrUi` 的 WinForms 新 GUI；
3. 下载 BtbN LGPL FFmpeg；
4. 在同一个 `ASR-UI-win-x64` artifact 中放入 `ASR-UI.exe`、`WhisperDesktop.exe`、`Whisper.dll`、`ffmpeg.exe`、文档和许可证提示。

本 Linux 工作区无法编译或运行 Windows GUI/CUDA 成品；GitHub Actions 和你的 GTX 1080 Ti 是实际验证环境。

## 上游、模型与许可证

- CrispASR: <https://github.com/CrispStrobe/CrispASR>
- Qwen Anime GGUF: <https://huggingface.co/cstr/qwen3-asr-1.7b-ja-anime-GGUF> — Apache-2.0
- Qwen ForcedAligner GGUF: <https://huggingface.co/cstr/qwen3-forced-aligner-0.6b-GGUF> — Apache-2.0
- Parakeet Japanese GGUF: <https://huggingface.co/cstr/parakeet-tdt-0.6b-ja-GGUF> — CC-BY-4.0，使用时须遵守署名要求
- BtbN FFmpeg Builds: <https://github.com/BtbN/FFmpeg-Builds> — GUI 选择 LGPL Windows 包

模型所有权和使用权以各上游项目与模型卡为准；本项目不授予模型权利。
