using System.ComponentModel;
using System.Diagnostics;
using Md2Word.Core;

namespace Md2Word.Desktop;

internal sealed class MainForm : Form
{
    private readonly List<QueueEntry> entries = [];
    private readonly Font interfaceFont = new("Microsoft YaHei UI", 9F);
    private readonly SettingsStore settingsStore;
    private readonly ConversionResources resources;
    private readonly FileQueueView queue = new() { Dock = DockStyle.Fill };
    private readonly Button add = MakeButton("addFiles", "添加文件(&A)");
    private readonly Button remove = MakeButton("removeFiles", "移除选中(&R)");
    private readonly Button clear = MakeButton("clearFiles", "清空列表");
    private readonly Button start = MakeButton("startConversion", "开始转换(&S)");
    private readonly Button cancel = MakeButton("cancelConversion", "取消转换");
    private readonly Button retry = MakeButton("retryFailed", "重试失败文件");
    private readonly Button openFolder = MakeButton("openOutputDirectory", "打开输出目录");
    private readonly LinkLabel about = new() { Name = "aboutHelp", Text = "关于与帮助", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
    private readonly Label fileCount = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
    private readonly ComboBox template = new() { Name = "templatePicker", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "文档模板" };
    private readonly Label templateNote = MakeText("统一中文排版，无序列表改为编号。", "templateNote");
    private readonly LinkLabel templateHelp = new() { Name = "templateHelp", Text = "查看模板说明", AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
    private readonly Label templateDetails = MakeText("", "templateDetails");
    private readonly ComboBox outputMode = new() { Name = "outputMode", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "输出位置" };
    private readonly TextBox outputPath = new() { Name = "outputDirectory", Dock = DockStyle.Fill, AccessibleName = "输出文件夹" };
    private readonly Button browse = MakeButton("browseOutput", "浏览…");
    private readonly TableLayoutPanel customOutput = new() { Name = "customOutput", Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
    private readonly CheckBox titleOption = new() { Name = "titleOption", Text = "将开头的一级标题用作文档标题", AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16), Checked = true };
    private readonly Label settingsNotice = MakeText("", "settingsNotice");
    private readonly Label summary = new() { Name = "conversionSummary", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "转换进度" };
    private readonly ProgressBar progress = new() { Name = "conversionProgress", Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, AccessibleName = "正在转换", Visible = false };
    private readonly TableLayoutPanel settings = new() { Name = "conversionSettings", AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(20, 16, 16, 16) };
    private readonly System.Windows.Forms.Timer saveDelay = new() { Interval = 500 };
    private readonly ToolTip tips = new();
    private CancellationTokenSource? cancellation;
    private bool loading = true;
    private bool closeRequested;
    private string idleSummary = "请添加文件。";
    private int generation;
    internal bool IsRunning { get; private set; }
    internal IReadOnlyList<QueueEntry> Entries => entries;
    internal FileQueueView QueueView => queue;
    internal Task? CurrentRun { get; private set; }
    internal event Action<BatchConversionProgress>? ProgressObserved;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string> OpenPath { get; set; } = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string> RevealPath { get; set; } = path =>
    {
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        info.ArgumentList.Add("/select,");
        info.ArgumentList.Add(path);
        Process.Start(info);
    };

    internal MainForm(ConversionResources? conversionResources = null, SettingsStore? store = null)
    {
        SuspendLayout();
        resources = conversionResources ?? ConversionResources.FromApplicationDirectory();
        settingsStore = store ?? new SettingsStore(SettingsStore.ResolveDirectory(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        Text = "Markdown转Word";
        Icon = AppBranding.WindowIcon;
        ClientSize = new Size(1040, 650);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = interfaceFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 246, 248);
        AllowDrop = true;
        BuildLayout();
        WireEvents();
        var loaded = settingsStore.Load();
        template.Items.AddRange(["中文通用（CN-General）", "保留原有列表和加粗"]);
        outputMode.Items.AddRange(["源文件所在目录", "指定文件夹"]);
        // 每次打开使用中文通用，其他设置沿用已保存的选择。
        template.SelectedIndex = 0;
        outputMode.SelectedIndex = loaded.Settings.UseCustomOutput ? 1 : 0;
        outputPath.Text = loaded.Settings.OutputDirectory;
        titleOption.Checked = loaded.Settings.UseFirstHeadingAsTitle;
        settingsNotice.Text = loaded.Notice ?? "";
        loading = false;
        RefreshTemplate();
        RefreshState();
        ResumeLayout(true);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0), Margin = Padding.Empty };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 100));
        var workspace = new TableLayoutPanel { Name = "workspace", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        workspace.RowStyles.Add(new(SizeType.Percent, 100));
        workspace.ColumnStyles.Add(new(SizeType.Percent, 100));
        workspace.ColumnStyles.Add(new(SizeType.Absolute, 295));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(20, 16, 16, 20), Margin = Padding.Empty };
        left.ColumnStyles.Add(new(SizeType.Percent, 100));
        left.RowStyles.Add(new(SizeType.Absolute, 46));
        left.RowStyles.Add(new(SizeType.Percent, 100));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        for (var i = 0; i < 3; i++) toolbar.ColumnStyles.Add(new(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new(SizeType.Percent, 100));
        toolbar.Controls.Add(add, 0, 0);
        toolbar.Controls.Add(remove, 1, 0);
        toolbar.Controls.Add(clear, 2, 0);
        toolbar.Controls.Add(fileCount, 3, 0);
        left.Controls.Add(toolbar, 0, 0);
        left.Controls.Add(queue, 0, 1);
        var side = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Margin = Padding.Empty };
        side.Controls.Add(settings);
        settings.ColumnStyles.Add(new(SizeType.Percent, 100));
        settings.Controls.Add(new Label { Text = "转换设置", AutoSize = true, Margin = new Padding(0, 0, 0, 22) });
        settings.Controls.Add(MakeFieldLabel("文档模板", template));
        settings.Controls.Add(template);
        template.Margin = new Padding(0, 0, 0, 8);
        settings.Controls.Add(templateNote);
        settings.Controls.Add(templateHelp);
        settings.Controls.Add(templateDetails);
        templateDetails.Visible = false;
        settings.Controls.Add(MakeFieldLabel("输出位置", outputMode));
        settings.Controls.Add(outputMode);
        outputMode.Margin = new Padding(0, 0, 0, 8);
        var outputNote = MakeText("重名时自动另存，不覆盖已有文件。");
        outputNote.Margin = new Padding(0, 0, 0, 16);
        settings.Controls.Add(outputNote);
        customOutput.ColumnStyles.Add(new(SizeType.Percent, 100));
        customOutput.ColumnStyles.Add(new(SizeType.AutoSize));
        browse.Margin = new Padding(6, 0, 0, 0);
        outputPath.Margin = new Padding(0, 4, 0, 0);
        customOutput.Controls.Add(outputPath, 0, 0);
        customOutput.Controls.Add(browse, 1, 0);
        settings.Controls.Add(customOutput);
        settings.Controls.Add(titleOption);
        settings.Controls.Add(settingsNotice);
        settings.Controls.Add(about);
        settings.SizeChanged += (_, _) => WrapSettingsText();
        workspace.Controls.Add(left, 0, 0);
        workspace.Controls.Add(side, 1, 0);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(20, 8, 20, 12), BackColor = Color.White, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new(SizeType.Percent, 100));
        bottom.RowStyles.Add(new(SizeType.Percent, 100));
        bottom.RowStyles.Add(new(SizeType.Absolute, 8));
        bottom.RowStyles.Add(new(SizeType.Absolute, 37));
        bottom.Controls.Add(summary, 0, 0);
        bottom.Controls.Add(progress, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        start.BackColor = Color.FromArgb(36, 89, 188);
        start.ForeColor = Color.White;
        start.FlatStyle = FlatStyle.Flat;
        start.FlatAppearance.BorderColor = start.BackColor;
        foreach (var button in new[] { start, cancel, retry, openFolder }) { button.Margin = new Padding(8, 3, 0, 0); buttons.Controls.Add(button); }
        bottom.Controls.Add(buttons, 0, 2);
        root.Controls.Add(workspace, 0, 0);
        root.Controls.Add(bottom, 0, 1);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        add.Click += (_, _) => PickFiles();
        remove.Click += (_, _) => RemoveSelected();
        clear.Click += (_, _) => { if (!IsRunning) { entries.Clear(); queue.SetEntries(entries); idleSummary = "请添加文件。"; RefreshState(); } };
        start.Click += (_, _) => CurrentRun = RunConversionAsync(false);
        retry.Click += (_, _) => CurrentRun = RunConversionAsync(true);
        cancel.Click += (_, _) => RequestCancellation();
        openFolder.Click += (_, _) => OpenOutputFolder();
        queue.OpenRequested += entry => { if (entry.Result?.OutputPath is { } path) TryOpen(path); };
        queue.RevealRequested += RevealOutput;
        queue.ReconvertRequested += () => CurrentRun = RunSelectedAgainAsync();
        queue.SelectionChanged += RefreshState;
        about.LinkClicked += (_, _) =>
        {
            using var dialog = new AboutForm(AppInfo.Diagnostics(entries), TryOpenLink);
            dialog.ShowDialog(this);
        };
        template.SelectedIndexChanged += (_, _) => { RefreshTemplate(); SettingsChanged(); };
        outputMode.SelectedIndexChanged += (_, _) => SettingsChanged();
        outputPath.TextChanged += (_, _) => SettingsChanged();
        titleOption.CheckedChanged += (_, _) => SettingsChanged();
        templateHelp.LinkClicked += (_, _) =>
        {
            templateDetails.Visible = !templateDetails.Visible;
            templateHelp.Text = templateDetails.Visible ? "收起模板说明" : "查看模板说明";
        };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "选择输出文件夹", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(outputPath.Text) ? outputPath.Text : "" };
            if (picker.ShowDialog(this) == DialogResult.OK) outputPath.Text = picker.SelectedPath;
        };
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); SaveSettings(); };
        DragEnter += HandleDragEnter;
        DragDrop += HandleDrop;
        queue.AcceptFileDrops(HandleDragEnter, HandleDrop);
        FormClosing += HandleClosing;
    }

    internal void AddFiles(IEnumerable<string> paths)
    {
        if (IsRunning) return;
        var added = 0;
        var skipped = 0;
        var known = entries.Select(entry => entry.InputPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath) || !IsMarkdown(fullPath) || !known.Add(fullPath)) { skipped++; continue; }
                entries.Add(new(fullPath));
                added++;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            { skipped++; }
        }
        queue.SetEntries(entries);
        idleSummary = skipped == 0 ? $"已添加{added}个文件。" : $"已添加{added}个文件，略过{skipped}个重复、无效或非Markdown文件。";
        RefreshState();
    }

    private void PickFiles()
    {
        using var picker = new OpenFileDialog { Filter = "Markdown文件 (*.md;*.markdown)|*.md;*.markdown", Multiselect = true, Title = "添加Markdown文件" };
        if (picker.ShowDialog(this) == DialogResult.OK) AddFiles(picker.FileNames);
    }

    internal void RemoveSelected()
    {
        if (IsRunning) return;
        var selected = queue.SelectedEntries.ToHashSet();
        entries.RemoveAll(selected.Contains);
        queue.SetEntries(entries);
        idleSummary = $"已移除{selected.Count}个文件。";
        RefreshState();
        if (queue.FileRows.Count > 0) queue.FileRows[0].Focus();
    }

    private bool ValidSettings => template.SelectedIndex >= 0 && (outputMode.SelectedIndex != 1 || !string.IsNullOrWhiteSpace(outputPath.Text));
    private DesktopSettings ReadSettings() => new(template.SelectedIndex switch { 0 => DocumentTemplate.CnGeneral, 1 => DocumentTemplate.Original, _ => null }, titleOption.Checked, outputMode.SelectedIndex == 1, outputPath.Text.Trim());

    internal async Task RunConversionAsync(bool onlyFailed)
    {
        if (IsRunning || !ValidSettings) return;
        var targets = entries.Where(entry => onlyFailed ? entry.Result?.Status == ConversionStatus.Failed
            : entry.Result is null || entry.Result.Status == ConversionStatus.Cancelled).ToArray();
        await RunTargetsAsync(targets);
    }

    internal Task RunSelectedAgainAsync() => RunTargetsAsync(entries
        .Where(entry => queue.SelectedEntries.Contains(entry) && entry.Result?.Status is ConversionStatus.Succeeded or ConversionStatus.Warning).ToArray());

    private async Task RunTargetsAsync(QueueEntry[] targets)
    {
        if (IsRunning || !ValidSettings) return;
        if (targets.Length == 0) return;
        var options = ReadSettings();
        string? directory;
        try { directory = options.UseCustomOutput ? Path.GetFullPath(options.OutputDirectory, AppContext.BaseDirectory) : null; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        { idleSummary = "输出路径无效，请重新选择文件夹。"; RefreshState(); return; }
        saveDelay.Stop();
        SaveSettings();
        IsRunning = true;
        var run = ++generation;
        cancellation = new();
        foreach (var entry in targets) { entry.Result = null; entry.IsRunning = false; }
        queue.RefreshEntries();
        RefreshState();
        summary.Text = $"正在准备{targets.Length}个文件…";
        var updates = new Progress<BatchConversionProgress>(value =>
        {
            if (!IsRunning || run != generation) return;
            var entry = targets[value.FileNumber - 1];
            entry.IsRunning = value.Result is null;
            if (value.Result is { } result) entry.Result = result;
            queue.RefreshEntries();
            summary.Text = cancellation?.IsCancellationRequested == true ? "正在取消，已完成的文件会保留…"
                : $"正在处理{value.FileNumber}/{value.TotalFiles}：{Path.GetFileName(value.InputPath)} · {value.Stage}";
            tips.SetToolTip(summary, summary.Text);
            ProgressObserved?.Invoke(value);
        });
        try
        {
            var requests = targets.Select(entry => new ConversionRequest(entry.InputPath, directory, options.Template!.Value, options.UseFirstHeadingAsTitle)).ToArray();
            // 资源检查和OOXML后处理也在工作线程执行，避免同步部分占用界面线程。
            var results = await Task.Run(() => new BatchConversionService(new(resources)).ConvertAsync(requests, updates, cancellation.Token));
            for (var i = 0; i < targets.Length; i++) targets[i].Result = results[i];
            var completed = results.Count(result => result.Status is ConversionStatus.Succeeded or ConversionStatus.Warning);
            var warned = results.Count(result => result.Status == ConversionStatus.Warning);
            var failed = results.Count(result => result.Status == ConversionStatus.Failed);
            idleSummary = results.Any(result => result.Status == ConversionStatus.Cancelled)
                ? $"已取消，已完成{completed}个，失败{failed}个。"
                : $"已完成{completed}个，失败{failed}个。";
            if (warned > 0) idleSummary += $"其中{warned}个请检查。";
        }
        catch (Exception ex)
        {
            idleSummary = "转换未完成，请查看文件原因。";
            foreach (var entry in targets.Where(entry => entry.Result is null)) entry.Result = new(ConversionStatus.Failed, null, Array.Empty<string>(), ex.Message);
        }
        finally
        {
            foreach (var entry in targets) entry.IsRunning = false;
            IsRunning = false;
            cancellation.Dispose();
            cancellation = null;
            queue.RefreshEntries();
            RefreshState();
            if (closeRequested) BeginInvoke(new Action(Close));
        }
    }

    internal void RequestCancellation()
    {
        if (!IsRunning) return;
        cancellation?.Cancel();
        cancel.Enabled = false;
        summary.Text = "正在取消，已完成的文件会保留…";
    }

    private void HandleClosing(object? sender, FormClosingEventArgs e)
    {
        if (IsRunning)
        {
            e.Cancel = true;
            closeRequested = true;
            RequestCancellation();
            return;
        }
        saveDelay.Stop();
        SaveSettings();
    }

    private void SettingsChanged()
    {
        if (loading) return;
        saveDelay.Stop();
        saveDelay.Start();
        RefreshState();
    }
    private void SaveSettings() => settingsNotice.Text = settingsStore.Save(ReadSettings()) ?? "";

    private void RefreshTemplate()
    {
        templateNote.Text = template.SelectedIndex == 1 ? "保留原有列表、加粗和斜体。" : "统一中文排版，无序列表改为编号。";
        templateDetails.Text = template.SelectedIndex switch
        {
            1 => "保留列表类型、编号起点及文字强调，使用基础Word样式。外观可能与Markdown编辑器不同。",
            _ => "统一字体、标题、编号和表格。顶层无序事项改为数字编号；正文开头的引导词取消加粗。保留嵌套结构和已有章节编号。"
        };
    }

    private void RefreshState()
    {
        if (loading) return;
        add.Enabled = clear.Enabled = !IsRunning;
        clear.Enabled &= entries.Count > 0;
        remove.Enabled = !IsRunning && queue.SelectedEntries.Count > 0;
        template.Enabled = outputMode.Enabled = outputPath.Enabled = browse.Enabled = titleOption.Enabled = !IsRunning;
        customOutput.Visible = outputMode.SelectedIndex == 1;
        var pending = entries.Count(entry => entry.Result is null || entry.Result.Status == ConversionStatus.Cancelled);
        start.Enabled = !IsRunning && pending > 0 && ValidSettings;
        start.BackColor = start.Enabled ? Color.FromArgb(36, 89, 188) : Color.FromArgb(225, 228, 233);
        start.FlatAppearance.BorderColor = start.BackColor;
        start.Text = pending > 0 ? $"转换{pending}个文件(&S)" : entries.Count > 0 ? "没有待转换文件" : "开始转换(&S)";
        queue.CanReconvert = !IsRunning && ValidSettings;
        cancel.Visible = IsRunning;
        cancel.Enabled = IsRunning && cancellation?.IsCancellationRequested != true;
        retry.Visible = !IsRunning && entries.Any(entry => entry.Result?.Status == ConversionStatus.Failed);
        retry.Enabled = ValidSettings;
        openFolder.Visible = !IsRunning && entries.Any(entry => entry.Result?.OutputPath is not null);
        progress.Visible = IsRunning;
        fileCount.Text = $"共{entries.Count}个文件";
        if (!IsRunning) summary.Text = entries.Count > 0 && !ValidSettings ? "请选择输出位置。" : idleSummary;
    }

    private void WrapSettingsText()
    {
        var width = Math.Max(1, settings.ClientSize.Width - settings.Padding.Horizontal - 3);
        foreach (var label in new[] { templateNote, templateDetails, settingsNotice }) label.MaximumSize = new Size(width, 0);
        titleOption.MaximumSize = new Size(width, 0);
    }

    internal void OpenOutputFolder()
    {
        var selectedOutput = queue.SelectedEntries.FirstOrDefault(entry => entry.Result?.OutputPath is not null)?.Result?.OutputPath;
        var output = selectedOutput ?? entries.LastOrDefault(entry => entry.Result?.OutputPath is not null)?.Result?.OutputPath;
        if (output is not null) TryOpen(Path.GetDirectoryName(output)!);
    }

    private void TryOpen(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("文件或文件夹已移动，请重新转换后打开。");
            OpenPath(path);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1155)
        { MessageBox.Show(this, "未找到可打开此文件的应用。请安装支持DOCX的软件，或在文件夹中找到文档后选择打开方式。", "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, $"无法打开，请检查文件位置和访问权限。\n\n详情：{ex.Message}", "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    internal void RevealOutput(QueueEntry entry)
    {
        if (entry.Result?.OutputPath is not { } path) return;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("文档已移动或删除，请检查原输出位置。");
            RevealPath(path);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, $"无法定位文档。\n\n详情：{ex.Message}", "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void TryOpenLink(string url)
    {
        try { OpenPath(url); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, $"无法打开网页，请检查浏览器设置。\n\n详情：{ex.Message}", "打开失败"); }
    }

    private void HandleDragEnter(object? sender, DragEventArgs e) => e.Effect = !IsRunning && e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IsMarkdown) ? DragDropEffects.Copy : DragDropEffects.None;
    private void HandleDrop(object? sender, DragEventArgs e) { if (!IsRunning && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); }
    private static bool IsMarkdown(string path) => new[] { ".md", ".markdown" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.O) && !IsRunning) { PickFiles(); return true; }
        if (keyData == Keys.Escape && IsRunning) { RequestCancellation(); return true; }
        // 文本框保留Ctrl+A和Delete的正常编辑行为。
        if (ContainsFocus && !FocusedTextInput() && !IsRunning)
        {
            if (keyData == (Keys.Control | Keys.A)) { queue.SelectAllEntries(); return true; }
            if (keyData == Keys.Delete) { RemoveSelected(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private bool FocusedTextInput()
    {
        Control? focused = this;
        while (focused is ContainerControl container && container.ActiveControl is { } child) focused = child;
        return focused is TextBoxBase or ComboBox;
    }

    private static Button MakeButton(string name, string text) => new() { Name = name, Text = text, AutoSize = true, MinimumSize = new Size(76, 32), Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 0, 8, 0), UseVisualStyleBackColor = true };
    private static Label MakeText(string text, string name = "") => new() { Name = name, Text = text, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 10) };
    private static Label MakeFieldLabel(string text, Control target)
    {
        var label = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        target.TabIndex = label.TabIndex + 1;
        return label;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { saveDelay.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
        if (disposing) interfaceFont.Dispose();
    }
}
