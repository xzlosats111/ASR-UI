# 已迁移到独立 ASR-UI 仓库

本项目从旧的 `whisper-desktop-fix` 开发树中分离，新的唯一推送目标为：

```text
https://github.com/xzlosats111/ASR-UI.git
```

- 新的默认分支：`main`
- Windows 推送代理：`http://127.0.0.1:10809`
- 推送入口：仓库根目录 `推送到ASR-UI.cmd`
- 不再使用旧目录中的 `从更新包一键覆盖并推送.cmd` 或任何指向 `whisper-desktop-fix` 的脚本。

## 为什么分离

新项目不再被旧 WhisperDesktop 的窗口布局束缚：它有独立的现代中文工作台 UI，负责 Qwen / Parakeet 的下载、代理、批处理和日志；但为避免删功能，完整 WhisperDesktop v3 的源码仍保留在 `legacy/WhisperDesktopSource`，并会随发布包一起构建。

## 首次推送

1. 将源码 ZIP 解压到例如 `C:\Users\111\Desktop\ASR-UI`。
2. 如未配置 Git 身份，先执行一次：

   ```bat
   git config --global user.name "你的名字"
   git config --global user.email "你的邮箱"
   ```

3. 双击根目录的 `推送到ASR-UI.cmd`。
4. 登录 / 输入 GitHub 凭据后，打开 GitHub Actions 等待 **Build ASR-UI for Windows**。
5. 下载 `ASR-UI-win-x64` artifact 并完整解压。

新 GUI 和模型下载尚未在真实 Windows / GTX 1080 Ti 上实测；首次 Actions 构建和真实机器运行结果是后续修正依据。
