# 测试记录与限制

## 已做的源码检查

- 恢复了原始完整的简体中文资源对话框：模型/GPU 高级设置、语言、翻译、字幕格式、输出路径和实时麦克风转写均可用。
- 恢复所有含中文的 C++ 文件的 UTF-8 BOM；这是修复截图中“多语言 / 开始转写”等文字乱码的关键。
- 启动行为改为始终先显示选择模型窗口，不自动加载上次模型。
- 文件选择器使用 Windows `OFN_ALLOWMULTISELECT`，支持 Ctrl/Shift 多选；批量任务逐个转写并为每个源文件生成对应扩展名的输出文件。
- 已对修改运行 `git diff --check`，并检查批量选择、文件筛选、SRT 默认、完整资源控件和 UTF-8 BOM 的静态条件。
- FLV 文件在原 Windows Media Foundation 打开失败后，会尝试调用旁边的 `ffmpeg.exe` 转为临时 WAV；批量任务会记录失败项、删除其不完整输出并继续后续文件。

## 仍未在本工作区执行的测试

此环境没有 Windows、Visual Studio、Windows SDK、Direct3D 编译器或 GTX 1080 Ti。因此此更新包是**未在此环境编译/运行的源码更新**，不是已验证的 EXE。

推送到 GitHub 后，Windows Actions 会负责实际编译；请先下载其 `WhisperDesktop-x64` 产物测试。如果构建或运行报错，提供 Actions 日志或完整报错截图即可继续修复。
