using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AsrUi;

internal enum SubtitlePreset
{
    QwenQualityQ4,
    ParakeetFast,
    QwenQualityQ8
}

internal enum ProcessProgressStage
{
    None,
    AudioConversion,
    Recognition
}

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Color _navColor = Color.FromArgb(23, 32, 52);
    private readonly Color _accentColor = Color.FromArgb(47, 109, 246);
    private readonly Color _pageColor = Color.FromArgb(245, 247, 251);

    private readonly ListBox _inputFiles = new() { HorizontalScrollbar = true };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RadioButton _sourceOutput = new() { Text = "输出到每个源文件同目录" };
    private readonly RadioButton _customOutput = new() { Text = "统一输出到指定文件夹" };
    private readonly TextBox _outputFolder = new();
    private Button _startButton = null!;
    private readonly Label _subtitleStatus = new() { AutoSize = false };
    private readonly ProgressBar _subtitleProgress = new() { Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Label _subtitleProgressStatus = new() { AutoSize = false };
    private int _batchFileCount = 1;
    private int _batchFileIndex;
    private int _lastFileProgress;
    private double? _activeDurationSeconds;
    private static readonly Regex FfmpegDurationPattern = new(@"Duration:\s*(?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex FfmpegTimePattern = new(@"time=(?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex SrtTimestampPattern = new(@"\[\d{2}:\d{2}:\d{2}(?:\.\d+)?\s*-->\s*(?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]", RegexOptions.Compiled);

    private readonly CheckedListBox _components = new() { CheckOnClick = true };
    private readonly RichTextBox _componentDetails = new() { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _useProxy = new() { Text = "下载和模型自动下载使用 HTTP 代理" };
    private readonly TextBox _proxyUrl = new();
    private readonly TextBox _modelDirectory = new();
    private Button _downloadButton = null!;
    private readonly ProgressBar _downloadProgress = new();
    private readonly Label _downloadStatus = new() { AutoSize = false };

    private readonly RichTextBox _log = new() { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9F) };

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        AppPaths.ConfigureModelDirectory(_settings.ModelDirectory);
        AppPaths.EnsureDirectories();
        Text = "ASR-UI v0.1.4 · 日语字幕工作台";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 820);
        Size = new Size(1280, 900);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = _pageColor;

        var nav = BuildNavigation();
        var host = new Panel { Dock = DockStyle.Fill, BackColor = _pageColor, Padding = new Padding(30, 26, 30, 22) };
        Controls.Add(host);
        Controls.Add(nav);

        AddPage(host, "subtitle", BuildSubtitlePage());
        AddPage(host, "components", BuildComponentsPage());
        AddPage(host, "legacy", BuildLegacyPage());
        AddPage(host, "log", BuildLogPage());
        ShowPage("subtitle");

        Shown += (_, _) => EnsureComponentListFitsScreen();
        AppendLog("ASR-UI v0.1.4 已启动。首次使用请先打开“组件与下载”下载默认勾选项目。");
        AppendLog($"工作目录：{AppPaths.BaseDirectory}");
        AppendLog($"模型目录：{AppPaths.ModelDirectory}");
    }

    private Panel BuildNavigation()
    {
        var panel = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = _navColor, Padding = new Padding(16, 22, 16, 18) };
        var brand = new Label
        {
            Text = "ASR-UI\n日语字幕工作台",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            AutoSize = false,
            Height = 84,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 2, 0, 0)
        };
        panel.Controls.Add(brand);

        var bottom = new Label
        {
            Text = "本地处理 · 模型按需下载\n默认代理：127.0.0.1:10809",
            ForeColor = Color.FromArgb(180, 196, 218),
            AutoSize = false,
            Height = 54,
            Dock = DockStyle.Bottom,
            Padding = new Padding(8, 0, 0, 0)
        };
        panel.Controls.Add(bottom);

        AddNavButton(panel, "subtitle", "日语字幕", "Qwen 高质量 / Parakeet 快速");
        AddNavButton(panel, "components", "组件与下载", "模型、引擎、代理与许可证");
        AddNavButton(panel, "legacy", "完整旧版", "WhisperDesktop v3 全部功能");
        AddNavButton(panel, "log", "运行日志", "下载、转换和识别错误信息");
        return panel;
    }

    private void AddNavButton(Panel nav, string page, string title, string subtitle)
    {
        var button = new Button
        {
            Text = title + "\n" + subtitle,
            Tag = page,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(222, 230, 244),
            BackColor = _navColor,
            Height = 64,
            Dock = DockStyle.Top,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
            Padding = new Padding(10, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => ShowPage(page);
        nav.Controls.Add(button);
        nav.Controls.SetChildIndex(button, 1);
        _navButtons[page] = button;
    }

    private void AddPage(Panel host, string key, Control content)
    {
        var page = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = _pageColor };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        host.Controls.Add(page);
        _pages[key] = page;
    }

    private void ShowPage(string key)
    {
        foreach (var (name, page) in _pages) page.Visible = name == key;
        foreach (var (name, button) in _navButtons)
        {
            button.BackColor = name == key ? _accentColor : _navColor;
            button.Font = new Font("Microsoft YaHei UI", 9F, name == key ? FontStyle.Bold : FontStyle.Regular);
        }
        if (key == "components")
        {
            RefreshComponentDetails();
            EnsureComponentListFitsScreen();
        }
    }

    private Control BuildSubtitlePage()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, Dock = DockStyle.Fill, BackColor = _pageColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));

        root.Controls.Add(TitleBlock("日语字幕", "选择两个本地模式之一。每个文件单独处理；视频先由 FFmpeg 转 WAV，某个文件失败后会继续处理后续文件。"), 0, 0);

        var presetCard = Card();
        var presetLabel = new Label { Text = "识别模式", AutoSize = true, Location = new Point(20, 22), Font = BoldFont(10F) };
        _preset.Items.AddRange(new object[]
        {
            "高质量 · Qwen 日语 Anime/Galgame Q4 + ForcedAligner（推荐）",
            "快速 · Parakeet Japanese Q8（原生时间码）",
            "可选 A/B · Qwen 日语 Anime/Galgame Q8 + ForcedAligner（下载后使用）"
        });
        _preset.SelectedIndex = 0;
        _preset.Location = new Point(110, 18);
        _preset.Width = 490;
        var modeHint = new Label { Text = "建议先对同一短片分别跑一次，然后比较两份 SRT。", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(620, 22) };
        presetCard.Controls.AddRange(new Control[] { presetLabel, _preset, modeHint });
        root.Controls.Add(presetCard, 0, 1);

        var fileCard = Card();
        var choose = PrimaryButton("选择视频 / 音频（可 Ctrl / Shift 多选）");
        choose.Location = new Point(18, 16);
        choose.Width = 290;
        choose.Click += (_, _) => ChooseInputFiles();
        var clear = SecondaryButton("清空列表");
        clear.Location = new Point(322, 16);
        clear.Click += (_, _) => _inputFiles.Items.Clear();
        _inputFiles.Location = new Point(18, 62);
        _inputFiles.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _inputFiles.Size = new Size(800, 240);
        fileCard.Resize += (_, _) => _inputFiles.Width = fileCard.ClientSize.Width - 36;
        fileCard.Resize += (_, _) => _inputFiles.Height = fileCard.ClientSize.Height - 80;
        fileCard.Controls.AddRange(new Control[] { choose, clear, _inputFiles });
        root.Controls.Add(fileCard, 0, 2);

        var outputCard = Card();
        _sourceOutput.Checked = _settings.OutputBesideSource;
        _sourceOutput.Location = new Point(18, 14);
        _customOutput.Checked = !_settings.OutputBesideSource;
        _customOutput.Location = new Point(18, 44);
        _outputFolder.Text = _settings.OutputFolder;
        _outputFolder.Location = new Point(206, 42);
        _outputFolder.Width = 500;
        var browse = SecondaryButton("选择文件夹");
        browse.Location = new Point(718, 38);
        browse.Click += (_, _) => ChooseOutputFolder();
        var outputHint = new Label { Text = "输出名：文件名.qwen-ja.srt 或 文件名.parakeet-ja.srt；两种模式不会互相覆盖。", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(206, 15) };
        outputCard.Controls.AddRange(new Control[] { _sourceOutput, _customOutput, _outputFolder, browse, outputHint });
        root.Controls.Add(outputCard, 0, 3);

        var action = new Panel { Dock = DockStyle.Fill, BackColor = _pageColor };
        _startButton = PrimaryButton("开始批量生成 SRT");
        _startButton.Font = BoldFont(11F);
        _startButton.Location = new Point(0, 10);
        _startButton.Size = new Size(220, 46);
        _startButton.Click += async (_, _) => await StartBatchAsync();
        _subtitleProgress.Location = new Point(236, 10);
        _subtitleProgress.Size = new Size(350, 18);
        _subtitleProgress.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _subtitleProgressStatus.Location = new Point(236, 33);
        _subtitleProgressStatus.Size = new Size(380, 25);
        _subtitleProgressStatus.Text = "等待开始";
        _subtitleProgressStatus.ForeColor = Color.DimGray;
        _subtitleStatus.Location = new Point(630, 10);
        _subtitleStatus.Size = new Size(430, 48);
        _subtitleStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _subtitleStatus.Text = "高质量模式需要：Qwen Anime Q4_K + ForcedAligner Q4_K；快速模式需要：Parakeet Japanese Q8_0。";
        _subtitleStatus.ForeColor = Color.DimGray;
        action.Controls.AddRange(new Control[] { _startButton, _subtitleProgress, _subtitleProgressStatus, _subtitleStatus });
        root.Controls.Add(action, 0, 4);
        return root;
    }

    private Control BuildComponentsPage()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill, BackColor = _pageColor, AutoScroll = true };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.Controls.Add(TitleBlock("组件与下载", "默认勾选约 3.4 GiB。模型和运行时直接由本程序从上游下载，不进入 Git 仓库，也不会被推送到 GitHub。"), 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 480, IsSplitterFixed = false, BackColor = _pageColor };
        _components.Dock = DockStyle.Fill;
        _components.BorderStyle = BorderStyle.FixedSingle;
        _components.Font = new Font("Microsoft YaHei UI", 10F);
        _components.IntegralHeight = false;
        _components.ItemHeight = 34;
        for (var i = 0; i < ComponentCatalog.All.Count; i++)
        {
            var c = ComponentCatalog.All[i];
            _components.Items.Add(FormatComponent(c), c.DefaultSelected);
        }
        _components.SelectedIndex = 0;
        _components.SelectedIndexChanged += (_, _) => RefreshComponentDetails();
        split.Panel1.Padding = new Padding(0, 0, 12, 0);
        split.Panel1.Controls.Add(_components);
        _componentDetails.Dock = DockStyle.Fill;
        _componentDetails.BackColor = Color.White;
        _componentDetails.Font = new Font("Microsoft YaHei UI", 9F);
        split.Panel2.Padding = new Padding(12, 0, 0, 0);
        split.Panel2.Controls.Add(_componentDetails);
        root.Controls.Add(split, 0, 1);

        var proxyCard = Card();
        _useProxy.Checked = _settings.UseProxy;
        _useProxy.Location = new Point(18, 12);
        _useProxy.AutoSize = true;
        var proxyLabel = new Label { Text = "代理地址", AutoSize = true, Location = new Point(18, 42) };
        _proxyUrl.Text = _settings.ProxyUrl;
        _proxyUrl.Location = new Point(82, 38);
        _proxyUrl.Width = 280;
        var proxyHint = new Label { Text = "默认：http://127.0.0.1:10809。取消勾选可改为直连。", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(380, 42) };
        var modelLabel = new Label { Text = "模型保存目录", AutoSize = true, Location = new Point(18, 78) };
        _modelDirectory.Text = AppPaths.ModelDirectory;
        _modelDirectory.Location = new Point(110, 74);
        _modelDirectory.Width = 540;
        var modelBrowse = SecondaryButton("选择目录");
        modelBrowse.Location = new Point(665, 70);
        modelBrowse.Click += (_, _) => ChooseModelDirectory();
        var modelHint = new Label { Text = "可以放到空间更大的磁盘；切换后新下载模型将保存到该目录。", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(18, 104) };
        proxyCard.Controls.AddRange(new Control[] { _useProxy, proxyLabel, _proxyUrl, proxyHint, modelLabel, _modelDirectory, modelBrowse, modelHint });
        root.Controls.Add(proxyCard, 0, 2);

        var action = new Panel { Dock = DockStyle.Fill, BackColor = _pageColor };
        _downloadButton = PrimaryButton("下载所选组件");
        _downloadButton.Location = new Point(0, 11);
        _downloadButton.Size = new Size(220, 42);
        _downloadButton.Click += async (_, _) => await DownloadSelectedAsync();
        var openModels = SecondaryButton("打开模型文件夹");
        openModels.Location = new Point(235, 11);
        openModels.Click += (_, _) => OpenFolder(AppPaths.ModelDirectory);
        _downloadProgress.Location = new Point(370, 12);
        _downloadProgress.Size = new Size(330, 22);
        _downloadStatus.Location = new Point(370, 39);
        _downloadStatus.Size = new Size(570, 24);
        _downloadStatus.ForeColor = Color.DimGray;
        _downloadStatus.Text = "选择组件后点击下载。已存在且大小正常的组件会自动跳过。";
        action.Controls.AddRange(new Control[] { _downloadButton, openModels, _downloadProgress, _downloadStatus });
        root.Controls.Add(action, 0, 3);
        return root;
    }

    private Control BuildLegacyPage()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, BackColor = _pageColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.Controls.Add(TitleBlock("完整旧版 · WhisperDesktop v3", "新的 ASR-UI 使用独立、现代的界面，不再受旧界面布局限制；但旧版完整功能仍会随同一个发布包保留。"), 0, 0);
        var card = Card();
        var info = new Label
        {
            Text = "点击下方按钮会启动完整 WhisperDesktop v3。以下能力不会被删除：\n\n• 启动时模型选择窗口；语言、翻译、输出文字 / SRT / WebVTT 类型选择；GPU / 实现高级控制。\n• 文件转写、实时麦克风、Ctrl / Shift 多选顺序批处理、失败后继续后续文件。\n• FLV 无法由 Windows Media Foundation 打开时，使用同目录 FFmpeg 转临时 WAV 后继续。\n\n旧版模型 `ggml-large-v3-q5_0.bin` 可在“组件与下载”按需下载。",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            Font = new Font("Microsoft YaHei UI", 10F),
            ForeColor = Color.FromArgb(45, 52, 64)
        };
        card.Controls.Add(info);
        root.Controls.Add(card, 0, 1);
        var action = new Panel { Dock = DockStyle.Fill, BackColor = _pageColor };
        var launch = PrimaryButton("打开完整 WhisperDesktop v3");
        launch.Location = new Point(0, 17);
        launch.Size = new Size(280, 48);
        launch.Click += (_, _) => LaunchLegacy();
        var note = new Label { Text = "提示：请从 GitHub Actions / Release 解压完整发布包；不要只复制 ASR-UI.exe。", Location = new Point(305, 30), AutoSize = true, ForeColor = Color.DimGray };
        action.Controls.AddRange(new Control[] { launch, note });
        root.Controls.Add(action, 0, 2);
        return root;
    }

    private Control BuildLogPage()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, BackColor = _pageColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(TitleBlock("运行日志", "下载、FFmpeg 转换、CrispASR 输出和每个文件的错误都会显示在这里。发生问题时，请保留此页内容并截图。"), 0, 0);
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Color.FromArgb(30, 35, 43);
        _log.ForeColor = Color.FromArgb(224, 234, 245);
        _log.WordWrap = false;
        root.Controls.Add(_log, 0, 1);
        return root;
    }

    private Control TitleBlock(string title, string description)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = _pageColor };
        var heading = new Label { Text = title, AutoSize = true, Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), ForeColor = Color.FromArgb(25, 42, 75), Location = new Point(0, 0) };
        var sub = new Label { Text = description, AutoSize = false, ForeColor = Color.DimGray, Location = new Point(2, 42), Size = new Size(900, 36) };
        panel.Controls.AddRange(new Control[] { heading, sub });
        return panel;
    }

    private Panel Card() => new() { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 12) };
    private Button PrimaryButton(string text) => NewButton(text, _accentColor, Color.White);
    private Button SecondaryButton(string text) => NewButton(text, Color.White, Color.FromArgb(35, 62, 112));
    private Button NewButton(string text, Color background, Color foreground) => new()
    {
        Text = text, BackColor = background, ForeColor = foreground, FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderColor = Color.FromArgb(185, 198, 221), BorderSize = 1 }, Height = 36, AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand
    };
    private Font BoldFont(float size) => new("Microsoft YaHei UI", size, FontStyle.Bold);

    private void ChooseInputFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "选择视频或音频文件",
            Filter = "媒体文件|*.mp4;*.mkv;*.avi;*.mov;*.mpeg;*.mpg;*.webm;*.flv;*.wmv;*.m4v;*.ts;*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _inputFiles.Items.Clear();
        _inputFiles.Items.AddRange(dialog.FileNames);
        _subtitleStatus.Text = $"已选择 {dialog.FileNames.Length} 个文件。";
        AppendLog($"已选择 {dialog.FileNames.Length} 个输入文件。");
    }

    private void ChooseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择 SRT 统一输出文件夹" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputFolder.Text = dialog.SelectedPath;
            _customOutput.Checked = true;
        }
    }

    private void ChooseModelDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择模型保存目录（建议选择空间充足的磁盘）",
            SelectedPath = Directory.Exists(_modelDirectory.Text) ? _modelDirectory.Text : AppPaths.ModelDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AppPaths.ConfigureModelDirectory(dialog.SelectedPath);
        AppPaths.EnsureDirectories();
        _modelDirectory.Text = AppPaths.ModelDirectory;
        SaveSettings();
        RefreshComponentList();
        RefreshComponentDetails();
        AppendLog($"模型保存目录已切换为：{AppPaths.ModelDirectory}");
    }

    private bool ApplyModelDirectory()
    {
        try
        {
            AppPaths.ConfigureModelDirectory(_modelDirectory.Text);
            AppPaths.EnsureDirectories();
            _modelDirectory.Text = AppPaths.ModelDirectory;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "模型保存目录无法使用：\n" + ex.Message, "模型目录错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void EnsureComponentListFitsScreen()
    {
        if (!IsHandleCreated || _components.Items.Count == 0) return;
        // Keep every currently known component visible on normal displays. On a very
        // small display the component page itself scrolls, rather than clipping controls.
        var componentListHeight = _components.Items.Count * _components.ItemHeight + 18;
        var desiredClientHeight = 84 + Math.Max(360, componentListHeight) + 126 + 74 + 34;
        var windowChrome = Height - ClientSize.Height;
        var desiredHeight = desiredClientHeight + windowChrome;
        var workArea = Screen.FromControl(this).WorkingArea;
        var maximumHeight = Math.Max(620, workArea.Height - 32);
        var targetHeight = Math.Min(desiredHeight, maximumHeight);
        if (Height < targetHeight) Height = targetHeight;
        _components.IntegralHeight = false;
    }

    private void RefreshComponentDetails()
    {
        if (_components.SelectedIndex < 0) return;
        var c = ComponentCatalog.All[_components.SelectedIndex];
        _componentDetails.Text = $"名称：{c.Title}\n大小：{c.Size}\n已安装：{(c.IsInstalled() ? "是" : "否")}\n\n模型/下载保存位置：\n{c.TargetPath}\n\n用途说明：\n{c.Description}\n\n上游来源：\n{c.Source}\n\n许可证 / 提示：\n{c.License}\n\n下载地址：\n{c.Url}";
    }

    private string FormatComponent(ComponentDefinition c) => $"{c.Title}  ·  {c.Size}{(c.IsInstalled() ? "  [已安装]" : "")}";

    private async Task DownloadSelectedAsync()
    {
        var selected = Enumerable.Range(0, _components.Items.Count)
            .Where(i => _components.GetItemChecked(i))
            .Select(i => ComponentCatalog.All[i])
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请至少勾选一个组件。", "未选择组件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_useProxy.Checked && !Uri.TryCreate(_proxyUrl.Text.Trim(), UriKind.Absolute, out _))
        {
            MessageBox.Show(this, "代理地址无效。示例：http://127.0.0.1:10809", "代理设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ApplyModelDirectory()) return;
        SaveSettings();
        _downloadButton.Enabled = false;
        _downloadProgress.Style = ProgressBarStyle.Marquee;
        var failures = new List<string>();
        try
        {
            var service = new DownloadService();
            foreach (var component in selected)
            {
                if (component.IsInstalled())
                {
                    AppendLog($"已跳过已安装组件：{component.Title}");
                    continue;
                }
                _downloadStatus.Text = $"正在下载：{component.Title}";
                AppendLog($"开始下载：{component.Title}（{component.Source}）");
                try
                {
                    var progress = new Progress<(long downloaded, long? total)>(p => UpdateDownloadProgress(component, p.downloaded, p.total));
                    await service.DownloadAsync(component, _useProxy.Checked, _proxyUrl.Text.Trim(), progress, message =>
                    {
                        AppendLog(message);
                        _downloadStatus.Text = message;
                    }, CancellationToken.None);
                    AppendLog($"组件安装完成：{component.Title}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{component.Title}：{ex.Message}");
                    AppendLog($"下载失败：{component.Title} - {ex.Message}");
                }
            }
        }
        finally
        {
            _downloadProgress.Style = ProgressBarStyle.Blocks;
            _downloadProgress.Value = 0;
            _downloadButton.Enabled = true;
            RefreshComponentList();
            RefreshComponentDetails();
        }

        if (failures.Count == 0)
        {
            _downloadStatus.Text = "所选组件已准备完成。";
            MessageBox.Show(this, "所选组件已准备完成。现在可进入“日语字幕”页。", "下载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _downloadStatus.Text = $"下载结束，但有 {failures.Count} 项失败。";
            MessageBox.Show(this, "部分下载失败：\n\n" + string.Join("\n", failures) + "\n\n请检查代理开关、代理地址和网络后重试。", "下载有错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateDownloadProgress(ComponentDefinition component, long downloaded, long? total)
    {
        if (total is > 0)
        {
            _downloadProgress.Style = ProgressBarStyle.Continuous;
            _downloadProgress.Value = Math.Clamp((int)(downloaded * 100 / total.Value), 0, 100);
            _downloadStatus.Text = $"正在下载：{component.Title} · {FormatBytes(downloaded)} / {FormatBytes(total.Value)}";
        }
        else
        {
            _downloadProgress.Style = ProgressBarStyle.Marquee;
            _downloadStatus.Text = $"正在下载：{component.Title} · {FormatBytes(downloaded)}";
        }
    }

    private void RefreshComponentList()
    {
        var checkedStates = Enumerable.Range(0, _components.Items.Count).Select(_components.GetItemChecked).ToArray();
        _components.Items.Clear();
        for (var i = 0; i < ComponentCatalog.All.Count; i++)
            _components.Items.Add(FormatComponent(ComponentCatalog.All[i]), checkedStates.ElementAtOrDefault(i));
        _components.SelectedIndex = 0;
    }

    private async Task StartBatchAsync()
    {
        var files = _inputFiles.Items.Cast<string>().ToArray();
        if (files.Length == 0)
        {
            MessageBox.Show(this, "请先选择一个或多个视频/音频文件。", "未选择输入文件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_sourceOutput.Checked && string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            MessageBox.Show(this, "请选择统一输出文件夹，或切回输出至源文件同目录。", "未选择输出文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var preset = _preset.SelectedIndex switch
        {
            0 => SubtitlePreset.QwenQualityQ4,
            1 => SubtitlePreset.ParakeetFast,
            _ => SubtitlePreset.QwenQualityQ8
        };
        var required = preset switch
        {
            SubtitlePreset.QwenQualityQ4 => new[] { ComponentCatalog.CrispAsrCuda126, ComponentCatalog.Ffmpeg, ComponentCatalog.QwenAnimeQ4, ComponentCatalog.QwenAlignerQ4 },
            SubtitlePreset.QwenQualityQ8 => new[] { ComponentCatalog.CrispAsrCuda126, ComponentCatalog.Ffmpeg, ComponentCatalog.QwenAnimeQ8, ComponentCatalog.QwenAlignerQ4 },
            _ => new[] { ComponentCatalog.CrispAsrCuda126, ComponentCatalog.Ffmpeg, ComponentCatalog.ParakeetJaQ8 }
        };
        var missing = required.Where(c => !c.IsInstalled()).ToList();
        if (missing.Count > 0)
        {
            ShowPage("components");
            MessageBox.Show(this, "缺少以下组件：\n\n" + string.Join("\n", missing.Select(c => "• " + c.Title)) + "\n\n请在“组件与下载”页下载。", "缺少组件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ApplyModelDirectory()) return;
        SaveSettings();
        _startButton.Enabled = false;
        _subtitleStatus.Text = "任务正在运行；下方进度依据 FFmpeg 时间和识别到的字幕时间实时更新。";
        BeginBatchProgress(files.Length);
        var successful = 0;
        var failures = new List<string>();
        try
        {
            for (var index = 0; index < files.Length; index++)
            {
                var input = files[index];
                BeginFileProgress(index, input);
                try
                {
                    await TranscribeOneAsync(input, preset);
                    successful++;
                    SetFileProgress(100, "字幕已保存");
                }
                catch (Exception ex)
                {
                    failures.Add($"{input}：{ex.Message}");
                    SetFileProgress(100, "本文件失败，继续下一个文件");
                    AppendLog($"失败，继续下一个文件：{input}\n{ex.Message}");
                }
            }
        }
        finally
        {
            _startButton.Enabled = true;
        }

        var summary = $"完成 {successful} 个文件。";
        if (failures.Count > 0) summary += $"\n\n跳过/失败 {failures.Count} 个：\n" + string.Join("\n", failures);
        _subtitleStatus.Text = summary.Replace("\n", " ");
        AppendLog(summary);
        MessageBox.Show(this, summary, "批量任务完成", MessageBoxButtons.OK, failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task TranscribeOneAsync(string input, SubtitlePreset preset)
    {
        var crisp = AppPaths.FindCrispAsr() ?? throw new FileNotFoundException("未找到 crispasr.exe。");
        var ffmpeg = AppPaths.FindFfmpeg() ?? throw new FileNotFoundException("未找到 ffmpeg.exe。");
        var tempWav = Path.Combine(Path.GetTempPath(), "ASR-UI-" + Guid.NewGuid() + ".wav");
        string? stagingSrt = null;
        try
        {
            AppendLog($"========== 开始：{input} ==========");
            SetFileProgress(0, "正在用 FFmpeg 转换音频");
            await RunProcessAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-y", "-i", input, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", tempWav }, null, ProcessProgressStage.AudioConversion);
            if (!File.Exists(tempWav)) throw new InvalidDataException("FFmpeg 没有生成临时 WAV 文件。");

            var outputDirectory = _sourceOutput.Checked ? Path.GetDirectoryName(input)! : _outputFolder.Text.Trim();
            Directory.CreateDirectory(outputDirectory);
            var isQwen = preset is SubtitlePreset.QwenQualityQ4 or SubtitlePreset.QwenQualityQ8;
            var qwenModel = preset == SubtitlePreset.QwenQualityQ8 ? ComponentCatalog.QwenAnimeQ8 : ComponentCatalog.QwenAnimeQ4;
            var suffix = preset switch
            {
                SubtitlePreset.QwenQualityQ4 => ".qwen-ja",
                SubtitlePreset.QwenQualityQ8 => ".qwen-ja-q8",
                _ => ".parakeet-ja"
            };
            var requestedBaseName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(input) + suffix);
            var requestedSrt = requestedBaseName + ".srt";

            // CrispASR v0.8.45 writes SRT through a narrow-character Windows stream.
            // It can recognize Unicode source paths, but cannot always create an SRT whose
            // output path contains Chinese/Japanese characters. Always write to an ASCII-safe
            // staging base first, then let .NET copy the completed subtitle to the requested path.
            Directory.CreateDirectory(AppPaths.OutputStagingDirectory);
            var stagingBaseName = Path.Combine(AppPaths.OutputStagingDirectory, "asr-" + Guid.NewGuid().ToString("N"));
            stagingSrt = stagingBaseName + ".srt";
            AppendLog($"字幕目标：{requestedSrt}");
            AppendLog($"识别临时输出：{stagingSrt}（完成后将自动复制到字幕目标，以兼容中文/日文路径）");

            var arguments = isQwen
                ? new[] { "--backend", "qwen3", "-m", qwenModel.TargetPath, "-am", ComponentCatalog.QwenAlignerQ4.TargetPath, "--gpu-backend", "cuda", "-f", tempWav, "-l", "ja", "--vad", "--split-on-punct", "-osrt", "-of", stagingBaseName }
                : new[] { "--backend", "parakeet", "-m", ComponentCatalog.ParakeetJaQ8.TargetPath, "--gpu-backend", "cuda", "-f", tempWav, "-l", "ja", "--vad", "--split-on-punct", "--parakeet-decoder", "ctc", "-osrt", "-of", stagingBaseName };
            SetFileProgress(8, "正在加载模型、VAD 并识别");
            await RunProcessAsync(crisp, arguments, BuildEngineEnvironment(), ProcessProgressStage.Recognition);
            if (!File.Exists(stagingSrt)) throw new InvalidDataException($"识别引擎已结束，但没有生成临时字幕：{stagingSrt}");
            SetFileProgress(98, "正在保存字幕到所选输出位置");
            File.Copy(stagingSrt, requestedSrt, true);
            File.Delete(stagingSrt);
            AppendLog($"完成：{requestedSrt}");
        }
        finally
        {
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { /* Temporary file removal should not fail the batch. */ }
            try { if (stagingSrt is not null && File.Exists(stagingSrt)) File.Delete(stagingSrt); } catch { /* Staging cleanup should not fail the batch. */ }
        }
    }

    private Dictionary<string, string?> BuildEngineEnvironment()
    {
        var values = new Dictionary<string, string?> { ["CRISPASR_CACHE_DIR"] = AppPaths.CacheDirectory };
        if (_useProxy.Checked)
        {
            values["HTTP_PROXY"] = _proxyUrl.Text.Trim();
            values["HTTPS_PROXY"] = _proxyUrl.Text.Trim();
        }
        else
        {
            values["HTTP_PROXY"] = null;
            values["HTTPS_PROXY"] = null;
        }
        return values;
    }

    private void BeginBatchProgress(int fileCount)
    {
        _batchFileCount = Math.Max(1, fileCount);
        _batchFileIndex = 0;
        _lastFileProgress = 0;
        _activeDurationSeconds = null;
        _subtitleProgress.Style = ProgressBarStyle.Continuous;
        _subtitleProgress.Value = 0;
        _subtitleProgressStatus.Text = $"准备处理 {_batchFileCount} 个文件";
    }

    private void BeginFileProgress(int fileIndex, string input)
    {
        _batchFileIndex = fileIndex;
        _lastFileProgress = 0;
        _activeDurationSeconds = null;
        SetFileProgress(0, "准备处理：" + Path.GetFileName(input));
    }

    private void SetFileProgress(int filePercent, string status)
    {
        filePercent = Math.Clamp(filePercent, 0, 100);
        _lastFileProgress = Math.Max(_lastFileProgress, filePercent);
        var overall = Math.Clamp((int)Math.Round((_batchFileIndex * 100d + _lastFileProgress) / _batchFileCount), 0, 100);
        _subtitleProgress.Style = ProgressBarStyle.Continuous;
        _subtitleProgress.Value = overall;
        _subtitleProgressStatus.Text = $"第 {_batchFileIndex + 1}/{_batchFileCount} 个 · {status} · 总进度 {overall}%";
    }

    private void ObserveProcessProgress(string line, ProcessProgressStage stage)
    {
        if (stage == ProcessProgressStage.None) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ObserveProcessProgress(line, stage)));
            return;
        }

        if (stage == ProcessProgressStage.AudioConversion)
        {
            var durationMatch = FfmpegDurationPattern.Match(line);
            if (durationMatch.Success && TryParseTimestamp(durationMatch.Groups["time"].Value, out var duration))
            {
                _activeDurationSeconds = duration.TotalSeconds;
                SetFileProgress(0, $"正在转换音频，时长 {FormatProgressTime(duration)}");
                return;
            }

            var timeMatch = FfmpegTimePattern.Match(line);
            if (timeMatch.Success && _activeDurationSeconds is > 0 && TryParseTimestamp(timeMatch.Groups["time"].Value, out var current))
            {
                var ratio = Math.Clamp(current.TotalSeconds / _activeDurationSeconds.Value, 0d, 1d);
                SetFileProgress((int)Math.Round(ratio * 8d), $"正在转换音频 {FormatProgressTime(current)} / {FormatProgressTime(TimeSpan.FromSeconds(_activeDurationSeconds.Value))}");
            }
            return;
        }

        if (line.Contains("loaded ", StringComparison.OrdinalIgnoreCase))
        {
            SetFileProgress(10, "模型已加载，正在进行 VAD 和识别");
        }
        else if (line.Contains("detecting speech", StringComparison.OrdinalIgnoreCase))
        {
            SetFileProgress(12, "正在检测语音片段");
        }
        else if (line.Contains("[aligner]", StringComparison.OrdinalIgnoreCase))
        {
            SetFileProgress(94, "正在强制对齐和整理时间轴");
        }

        var timestampMatch = SrtTimestampPattern.Match(line);
        if (timestampMatch.Success && _activeDurationSeconds is > 0 && TryParseTimestamp(timestampMatch.Groups["time"].Value, out var recognized))
        {
            var ratio = Math.Clamp(recognized.TotalSeconds / _activeDurationSeconds.Value, 0d, 1d);
            var progress = 12 + (int)Math.Round(ratio * 84d);
            SetFileProgress(progress, $"正在识别 {FormatProgressTime(recognized)} / {FormatProgressTime(TimeSpan.FromSeconds(_activeDurationSeconds.Value))}");
        }
    }

    private static bool TryParseTimestamp(string value, out TimeSpan timestamp)
    {
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out timestamp);
    }

    private static string FormatProgressTime(TimeSpan value)
    {
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private async Task RunProcessAsync(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment, ProcessProgressStage progressStage = ProcessProgressStage.None)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null) start.Environment.Remove(key);
                else start.Environment[key] = value;
            }
        }

        AppendLog("> " + executable + " " + string.Join(" ", start.ArgumentList));
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => HandleProcessLine(e.Data, progressStage);
        process.ErrorDataReceived += (_, e) => HandleProcessLine(e.Data, progressStage);
        if (!process.Start()) throw new InvalidOperationException("无法启动进程。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"进程返回退出代码 {process.ExitCode}。");
    }

    private void HandleProcessLine(string? line, ProcessProgressStage progressStage)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        AppendLog(line);
        ObserveProcessProgress(line, progressStage);
    }

    private void LaunchLegacy()
    {
        var legacy = AppPaths.FindLegacyWhisperDesktop();
        if (legacy is null)
        {
            MessageBox.Show(this, "没有找到 WhisperDesktop.exe。请从 GitHub Actions 或 Release 下载并完整解压发布包，不要只复制 ASR-UI.exe。", "缺少完整旧版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        AppendLog("启动完整旧版：" + legacy);
        Process.Start(new ProcessStartInfo { FileName = legacy, WorkingDirectory = Path.GetDirectoryName(legacy)!, UseShellExecute = true });
    }

    private void SaveSettings()
    {
        _settings.UseProxy = _useProxy.Checked;
        _settings.ProxyUrl = _proxyUrl.Text.Trim();
        _settings.OutputBesideSource = _sourceOutput.Checked;
        _settings.OutputFolder = _outputFolder.Text.Trim();
        _settings.ModelDirectory = AppPaths.ModelDirectory;
        _settings.Save();
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static string FormatBytes(long bytes)
    {
        var mib = bytes / 1024d / 1024d;
        return mib >= 1024 ? $"{mib / 1024d:F2} GiB" : $"{mib:F1} MiB";
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = folder, UseShellExecute = true });
    }
}
