using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using Md2Word.Core;
using Md2Word.Desktop;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Md2Word.Desktop.Tests;

public sealed class DesktopTests
{
    private static readonly string Root = FindRoot();
    private static readonly ConversionResources Resources = ConversionResources.FromApplicationDirectory(Environment.GetEnvironmentVariable("MD2WORD_TEST_PANDOC") ?? Path.Combine(Root, "pandoc-3.10/pandoc.exe"));
    private readonly string workspace = Path.Combine(Root, "artifacts", "desktop-test-runs", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void InitializeWindowsForms()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    [Fact]
    public Task FirstUseDefaultsToChineseTemplateAndFilesCanBeAddedSelectedAndRemoved() => OnUiThread(() =>
    {
        using var form = CreateForm();
        Assert.Equal(0, Control<ComboBox>(form, "templatePicker").SelectedIndex);
        Assert.Equal(2, Control<ComboBox>(form, "templatePicker").Items.Count);
        Assert.Equal("中文通用（CN-General）", Control<ComboBox>(form, "templatePicker").SelectedItem);
        Assert.False(Control<Button>(form, "startConversion").Enabled);
        var otherDirectory = Directory.CreateDirectory(Path.Combine(workspace, "另一目录"));
        var sameName = Path.Combine(otherDirectory.FullName, "basic.md");
        File.Copy(Fixture("basic.md"), sameName);
        form.AddFiles([Fixture("basic.md"), Fixture("tables.md"), Fixture("basic.md"), sameName, "not-found.md", "file.txt"]);
        Assert.Equal(3, form.Entries.Count);
        Assert.True(Control<Button>(form, "startConversion").Enabled);
        form.QueueView.SelectEntry(form.QueueView.FileRows[0], Keys.None);
        form.QueueView.SelectEntry(form.QueueView.FileRows[2], Keys.Shift);
        Assert.Equal(3, form.QueueView.SelectedEntries.Count);
        form.QueueView.SelectEntry(form.QueueView.FileRows[1], Keys.Control);
        Assert.Equal(2, form.QueueView.SelectedEntries.Count);
        Control<Button>(form, "removeFiles").PerformClick();
        Assert.Single(form.Entries);
        Assert.EndsWith("tables.md", form.Entries[0].InputPath);
        form.QueueView.Focus();
        Shortcut(form, Keys.Control | Keys.A);
        Shortcut(form, Keys.Delete);
        Assert.Empty(form.Entries);
        form.AddFiles([Fixture("basic.md")]);
        Control<Button>(form, "clearFiles").PerformClick();
        Assert.Empty(form.Entries);
        return Task.CompletedTask;
    });

    [Fact]
    public Task RealConversionShowsResultsDetailsAndOpensActualPaths() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md"), Fixture("warnings.md"), Fixture("missing-image.md")]);
        Control<Button>(form, "startConversion").PerformClick();
        await form.CurrentRun!;
        Assert.Equal(new[] { ConversionStatus.Succeeded, ConversionStatus.Warning, ConversionStatus.Failed }, form.Entries.Select(entry => entry.Result!.Status));
        Assert.True(form.QueueView.FileRows[0].HasOpenAction);
        Assert.False(form.QueueView.FileRows[0].HasReasonAction);
        Assert.True(form.QueueView.FileRows[1].HasOpenAction);
        Assert.True(form.QueueView.FileRows[1].HasReasonAction);
        var failed = form.QueueView.FileRows[2];
        Assert.False(failed.HasOpenAction);
        failed.ToggleReason();
        Assert.True(failed.IsExpanded);
        Assert.Contains("找不到图片", form.Entries[2].Details);
        failed.ToggleReason();
        Assert.False(failed.IsExpanded);
        string? opened = null;
        form.OpenPath = value => opened = value;
        form.QueueView.SelectEntry(form.QueueView.FileRows[0], Keys.None);
        form.OpenOutputFolder();
        Assert.Equal(Path.GetDirectoryName(form.Entries[0].Result!.OutputPath), opened);
        var link = Descendants(form.QueueView.FileRows[0]).OfType<LinkLabel>().Single(control => control.Text == "打开文档");
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(link, [new LinkLabelLinkClickedEventArgs(link.Links[0])]);
        Assert.Equal(form.Entries[0].Result!.OutputPath, opened);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(workspace, "output"), "*.docx").Length);
    });

    [Fact]
    public Task OriginalTemplateAndTitleChoiceReachCoreAndDefaultOutputUsesSourceDirectory() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        var source = Path.Combine(workspace, "原文 选项.md");
        File.Copy(Fixture("lists.md"), source);
        Control<ComboBox>(form, "templatePicker").SelectedIndex = 1;
        Control<CheckBox>(form, "titleOption").Checked = false;
        form.AddFiles([source]);
        await form.RunConversionAsync(false);
        var result = form.Entries[0].Result!;
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(workspace, "原文 选项.docx"), result.OutputPath);
        using var document = WordprocessingDocument.Open(result.OutputPath!, false);
        var first = document.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().First();
        Assert.Equal("Heading1", first.ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Contains(document.MainDocumentPart.NumberingDefinitionsPart!.Numbering!.Descendants<W.StartOverrideNumberingValue>(), item => item.Val!.Value == 7);
        Assert.Contains(document.MainDocumentPart.Document.Descendants<W.Run>(), run => run.InnerText.Contains("检查项目", StringComparison.Ordinal) && run.RunProperties?.Bold is not null);
    });

    [Fact]
    public Task RowKeyboardNavigationSelectionAndActionsWork() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md"), Fixture("warnings.md")]);
        form.QueueView.SelectEntry(form.QueueView.FileRows[0], Keys.None);
        KeyDown(form.QueueView.FileRows[0], Keys.Shift | Keys.Down);
        Assert.Equal(2, form.QueueView.SelectedEntries.Count);
        Assert.True(form.QueueView.FileRows[1].Focused);
        KeyDown(form.QueueView.FileRows[1], Keys.Space);
        Assert.Single(form.QueueView.SelectedEntries);
        await form.RunConversionAsync(false);
        KeyDown(form.QueueView.FileRows[1], Keys.F4);
        Assert.True(form.QueueView.FileRows[1].IsExpanded);
        string? opened = null;
        form.OpenPath = path => opened = path;
        KeyDown(form.QueueView.FileRows[0], Keys.Enter);
        Assert.Equal(form.Entries[0].Result!.OutputPath, opened);
    });

    [Fact]
    public Task RetryProcessesOnlyFailedFilesAfterTheirProblemIsFixed() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        var input = Path.Combine(workspace, "待修复.md");
        await File.WriteAllTextAsync(input, "# 图片测试\n\n![图片](fixed.png)");
        form.AddFiles([Fixture("basic.md"), input]);
        await form.RunConversionAsync(false);
        var completed = form.Entries[0].Result!;
        Assert.Equal(ConversionStatus.Failed, form.Entries[1].Result!.Status);
        File.Copy(Fixture("assets/diagram.png"), Path.Combine(workspace, "fixed.png"));
        Control<Button>(form, "retryFailed").PerformClick();
        await form.CurrentRun!;
        Assert.Same(completed, form.Entries[0].Result);
        Assert.All(form.Entries, entry => Assert.Equal(ConversionStatus.Succeeded, entry.Result!.Status));
        Assert.False(Control<Button>(form, "retryFailed").Visible);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(workspace, "output"), "*.docx").Length);
    });

    [Fact]
    public Task RunningLocksSettingsEscapeCancelsAndCompletedFilesRemain() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md"), Fixture("long-text.md"), Fixture("long-table.md"), Fixture("local-images.md")]);
        var observedLocked = false;
        var cancelled = false;
        form.ProgressObserved += update =>
        {
            Assert.False(Control<ComboBox>(form, "templatePicker").Enabled);
            Assert.False(Control<ComboBox>(form, "outputMode").Enabled);
            Assert.False(Control<Button>(form, "addFiles").Enabled);
            observedLocked = true;
            form.AddFiles([Fixture("tables.md")]);
            if (!cancelled && update.FileNumber == 2 && update.Result is null)
            { cancelled = true; Assert.True(Shortcut(form, Keys.Escape)); }
        };
        await form.RunConversionAsync(false);
        Assert.True(observedLocked);
        Assert.True(cancelled);
        Assert.Equal(4, form.Entries.Count);
        Assert.Equal(ConversionStatus.Succeeded, form.Entries[0].Result!.Status);
        Assert.Equal(ConversionStatus.Cancelled, form.Entries[^1].Result!.Status);
        Assert.All(form.Entries, entry => Assert.False(entry.IsRunning));
        Assert.True(File.Exists(form.Entries[0].Result!.OutputPath));
        Assert.True(Control<ComboBox>(form, "templatePicker").Enabled);
        Assert.Empty(Directory.GetFiles(Path.Combine(workspace, "output"), ".md2word-*"));
    });

    [Fact]
    public Task ClosingWhileRunningWaitsForCancellationBeforeDisposal() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("long-text.md"), Fixture("long-table.md")]);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.FormClosed += (_, _) => closed.TrySetResult();
        var requested = false;
        form.ProgressObserved += value =>
        {
            if (requested) return;
            requested = true;
            form.Close();
            Assert.False(form.IsDisposed);
        };
        await form.RunConversionAsync(false);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(form.IsDisposed);
        Assert.Contains(form.Entries, entry => entry.Result!.Status == ConversionStatus.Cancelled);
        Assert.Empty(Directory.GetFiles(Path.Combine(workspace, "output"), ".md2word-*"));
    });

    [Fact]
    public Task SettingsRememberOutputAndTitleButEveryLaunchDefaultsToChineseTemplate() => OnUiThread(() =>
    {
        using (var form = CreateForm())
        {
            Assert.Equal(0, Control<ComboBox>(form, "templatePicker").SelectedIndex);
            Control<ComboBox>(form, "templatePicker").SelectedIndex = 1;
            Control<ComboBox>(form, "outputMode").SelectedIndex = 1;
            Control<TextBox>(form, "outputDirectory").Text = Path.Combine(workspace, "中文 输出");
            Control<CheckBox>(form, "titleOption").Checked = false;
            form.Close();
        }
        using (var remembered = CreateForm())
        {
            Assert.Equal(0, Control<ComboBox>(remembered, "templatePicker").SelectedIndex);
            Assert.Equal(1, Control<ComboBox>(remembered, "outputMode").SelectedIndex);
            Assert.EndsWith("中文 输出", Control<TextBox>(remembered, "outputDirectory").Text);
            Assert.False(Control<CheckBox>(remembered, "titleOption").Checked);
            remembered.Close();
        }
        File.WriteAllText(Path.Combine(workspace, "settings.json"), "{invalid");
        using var recovered = CreateForm();
        Assert.Equal(0, Control<ComboBox>(recovered, "templatePicker").SelectedIndex);
        Assert.Contains("无法读取", Control<Label>(recovered, "settingsNotice").Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReadOnlySettingsDirectoryDoesNotPreventConversion() => OnUiThread(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(workspace, "readonly-settings"));
        var original = directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var restricted = directory.GetAccessControl();
        restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.Write, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(restricted);
            using var form = CreateForm(new SettingsStore(directory.FullName));
            Configure(form);
            form.AddFiles([Fixture("basic.md")]);
            await form.RunConversionAsync(false);
            Assert.Equal(ConversionStatus.Succeeded, form.Entries[0].Result!.Status);
            Assert.Contains("本次选择仍可使用", Control<Label>(form, "settingsNotice").Text);
            Assert.False(File.Exists(Path.Combine(directory.FullName, "settings.json")));
            form.Close();
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorSddlForm(original, AccessControlSections.Access);
            directory.SetAccessControl(restored);
        }
        File.WriteAllText(Path.Combine(directory.FullName, "restored.txt"), "权限已恢复");
    });

    [Fact]
    public Task TextEditingShortcutsDoNotRemoveSelectedFiles() => OnUiThread(() =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md")]);
        form.QueueView.SelectAllEntries();
        var path = Control<TextBox>(form, "outputDirectory");
        path.Focus();
        Assert.True(path.Focused);
        Shortcut(form, Keys.Control | Keys.A);
        Shortcut(form, Keys.Delete);
        Assert.Single(form.Entries);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DropDataUsesTheSameValidationAndIsRejectedDuringConversion() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        var data = new DataObject(DataFormats.FileDrop, new[] { Fixture("basic.md"), Fixture("tables.md"), Fixture("basic.md") });
        var enter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        InvokeControlEvent(form, "OnDragEnter", enter);
        Assert.Equal(DragDropEffects.Copy, enter.Effect);
        InvokeControlEvent(form, "OnDragDrop", enter);
        Assert.Equal(2, form.Entries.Count);
        var fileLabel = Descendants(form.QueueView.FileRows[0]).OfType<Label>().First();
        var childEnter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        InvokeControlEvent(fileLabel, "OnDragEnter", childEnter);
        Assert.Equal(DragDropEffects.Copy, childEnter.Effect);
        form.ProgressObserved += _ =>
        {
            var busyEnter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            InvokeControlEvent(form, "OnDragEnter", busyEnter);
            Assert.Equal(DragDropEffects.None, busyEnter.Effect);
            InvokeControlEvent(form, "OnDragDrop", busyEnter);
        };
        await form.RunConversionAsync(false);
        Assert.Equal(2, form.Entries.Count);
    });

    [Theory]
    [InlineData(1F, 1040, 650)]
    [InlineData(1F, 900, 600)]
    [InlineData(1.25F, 1040, 650)]
    [InlineData(1.5F, 1040, 650)]
    [InlineData(2F, 1040, 650)]
    public Task LayoutAndPreviewAtWindowSizesAndSimulatedDpi(float scale, int width, int height) => OnUiThread(async () =>
    {
        using var form = CreateForm();
        var naturalScale = form.DeviceDpi / 96F;
        var layoutScale = scale / naturalScale;
        if (Math.Abs(layoutScale - 1) > 0.001F)
        {
            // 按比例缩放控件与字体作布局模拟，不代表实际切换显示器DPI。
            form.Scale(new SizeF(layoutScale, layoutScale));
            form.Font = new Font(form.Font.FontFamily, form.Font.Size * layoutScale);
        }
        form.MinimumSize = new Size((int)(900 * scale), (int)(600 * scale));
        form.ClientSize = new Size((int)(width * scale), (int)(height * scale));
        Configure(form);
        form.AddFiles([Fixture("basic.md"), Fixture("warnings.md"), Fixture("missing-image.md")]);
        await form.RunConversionAsync(false);
        form.QueueView.FileRows[1].ToggleReason();
        form.QueueView.FileRows[2].ToggleReason();
        Control<LinkLabel>(form, "templateHelp").Focus();
        var workspacePanel = Control<TableLayoutPanel>(form, "workspace");
        Capture(form, $"results-{width}-{(int)(scale * 100)}");
        Assert.All(form.QueueView.FileRows, row => Assert.True(row.StatusTextFits, "状态文字不应被截断。"));
        Assert.InRange(workspacePanel.GetColumnWidths()[1], (int)(295 * scale) - 3, (int)(295 * scale) + 3);
        foreach (var name in new[] { "templatePicker", "outputMode", "titleOption", "startConversion", "retryFailed", "openOutputDirectory" })
        {
            var control = form.Controls.Find(name, true).Single();
            Assert.True(control.Visible, $"控件应可见：{name}");
            var rectangle = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Assert.True(form.ClientRectangle.Contains(rectangle), $"控件越出窗口：{name}，{rectangle}");
        }
    });

    [Fact]
    public Task ActualMonitorDpiHasScaledSettingsWidthAndCanResize() => OnUiThread(() =>
    {
        using var form = CreateForm();
        var workspacePanel = Control<TableLayoutPanel>(form, "workspace");
        var expected = (int)Math.Round(295F * form.DeviceDpi / 96);
        Assert.InRange(workspacePanel.GetColumnWidths()[1], expected - 2, expected + 2);
        form.Size = form.MinimumSize;
        Capture(form, "actual-dpi-minimum");
        var selector = Control<ComboBox>(form, "templatePicker");
        Assert.True(form.ClientRectangle.Contains(form.RectangleToClient(selector.RectangleToScreen(selector.ClientRectangle))));
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmptyWindowShowsChineseTemplateAndItsDetails() => OnUiThread(() =>
    {
        using var form = CreateForm();
        var help = Control<LinkLabel>(form, "templateHelp");
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(help, [new LinkLabelLinkClickedEventArgs(help.Links[0])]);
        Assert.True(Control<Label>(form, "templateDetails").Visible);
        Assert.Equal(0, Control<ComboBox>(form, "templatePicker").SelectedIndex);
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(help, [new LinkLabelLinkClickedEventArgs(help.Links[0])]);
        Capture(form, "first-use");
        return Task.CompletedTask;
    });

    [Fact]
    public Task PendingOnlyConversionAndExplicitReconversionPreserveExistingResults() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md")]);
        await form.RunConversionAsync(false);
        var first = form.Entries[0].Result!;
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(first.OutputPath!));
        Assert.False(Control<Button>(form, "startConversion").Enabled);
        form.AddFiles([Fixture("tables.md")]);
        Assert.Contains("1个", Control<Button>(form, "startConversion").Text);
        await form.RunConversionAsync(false);
        Assert.Same(first, form.Entries[0].Result);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(workspace, "output"), "*.docx").Length);
        form.QueueView.SelectEntry(form.QueueView.FileRows[0], Keys.None);
        await form.RunSelectedAgainAsync();
        Assert.NotEqual(first.OutputPath, form.Entries[0].Result!.OutputPath);
        Assert.Equal(hash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(first.OutputPath!)));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(workspace, "output"), "*.docx").Length);
        form.QueueView.SelectEntry(form.QueueView.FileRows[1], Keys.None);
        form.QueueView.SelectEntry(form.QueueView.FileRows[0], Keys.Control);
        var order = new List<string>();
        form.ProgressObserved += value => { if (value.Result is not null) order.Add(Path.GetFileName(value.InputPath)); };
        await form.RunSelectedAgainAsync();
        Assert.Equal(new[] { "basic.md", "tables.md" }, order);
    });

    [Fact]
    public Task CancelledItemsCanResumeWithoutReprocessingCompletedItems() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md"), Fixture("tables.md")]);
        form.ProgressObserved += CancelAfterFirst;
        void CancelAfterFirst(BatchConversionProgress progress)
        {
            if (progress.FileNumber == 1 && progress.Result is not null) form.RequestCancellation();
        }
        await form.RunConversionAsync(false);
        form.ProgressObserved -= CancelAfterFirst;
        var completed = form.Entries[0].Result!;
        Assert.Equal(ConversionStatus.Succeeded, completed.Status);
        Assert.Equal(ConversionStatus.Cancelled, form.Entries[1].Result!.Status);
        await form.RunConversionAsync(false);
        Assert.Same(completed, form.Entries[0].Result);
        Assert.Equal(ConversionStatus.Succeeded, form.Entries[1].Result!.Status);
    });

    [Fact]
    public Task RevealUsesExactOutputAndDiagnosticsExcludePathsAndRawErrors() => OnUiThread(async () =>
    {
        using var form = CreateForm();
        Configure(form);
        form.AddFiles([Fixture("basic.md"), Fixture("missing-image.md")]);
        await form.RunConversionAsync(false);
        string? revealed = null;
        form.RevealPath = path => revealed = path;
        form.RevealOutput(form.Entries[0]);
        Assert.Equal(form.Entries[0].Result!.OutputPath, revealed);
        Assert.True(form.QueueView.FileRows[0].HasRevealAction);
        var diagnostics = AppInfo.Diagnostics(form.Entries);
        Assert.Contains(AppInfo.Version, diagnostics);
        Assert.DoesNotContain(Root, diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing-image", diagnostics);
        Assert.DoesNotContain(form.Entries[1].Result!.Error!, diagnostics);
        Assert.Contains("请按下方原因", form.Entries[1].Details);
        using var help = new AboutForm(diagnostics, _ => { });
        help.Show(form);
        Assert.Contains(AppInfo.Version, Descendants(help).OfType<Label>().First().Text);
        using var image = new Bitmap(help.Width, help.Height);
        help.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(workspace, "about-help.png"));
        help.Close();
    });

    [Fact]
    public void SettingsModeCreatesDirectoryAndSurvivesVersionUpgrade()
    {
        var app = Directory.CreateDirectory(Path.Combine(workspace, "app")).FullName;
        var data = Path.Combine(workspace, "local");
        var installed = SettingsStore.ResolveDirectory(app, data);
        Assert.Equal(Path.Combine(data, "md2word"), installed);
        var saved = new DesktopSettings(DocumentTemplate.Original, false, true, "输出 文件夹");
        Assert.Null(new SettingsStore(installed).Save(saved));
        Assert.Equal(saved, new SettingsStore(installed).Load().Settings);
        File.WriteAllText(Path.Combine(app, "portable.flag"), "");
        Assert.Equal(app, SettingsStore.ResolveDirectory(app, data));
        Assert.False(new SettingsStore(app).Load().Settings.UseCustomOutput);
        Assert.Equal(saved, new SettingsStore(installed).Load().Settings);
    }

    [Fact]
    public Task ExecutableAndWindowsUseThePackagedApplicationIcon() => OnUiThread(() =>
    {
        using var form = CreateForm();
        Assert.Same(AppBranding.WindowIcon, form.Icon);
        using var about = new AboutForm("", _ => { });
        Assert.Same(AppBranding.WindowIcon, about.Icon);
        using var native = Icon.ExtractAssociatedIcon(Path.ChangeExtension(typeof(MainForm).Assembly.Location, ".exe"));
        Assert.NotNull(native);
        using var expected = new Icon(Path.Combine(Root, "resources/branding/app.ico"), native.Size);
        using var actualPixels = native.ToBitmap();
        using var expectedPixels = expected.ToBitmap();
        Assert.Equal(expectedPixels.Size, actualPixels.Size);
        for (var y = 0; y < actualPixels.Height; y++)
            for (var x = 0; x < actualPixels.Width; x++)
                Assert.Equal(expectedPixels.GetPixel(x, y).ToArgb(), actualPixels.GetPixel(x, y).ToArgb());
        actualPixels.Save(Path.Combine(workspace, "executable-icon.png"));
        using var window = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(window, new Rectangle(Point.Empty, window.Size));
        window.Save(Path.Combine(workspace, "window-with-icon.png"));
        return Task.CompletedTask;
    });

    private MainForm CreateForm(SettingsStore? store = null)
    {
        Directory.CreateDirectory(workspace);
        var form = new MainForm(Resources, store ?? new SettingsStore(workspace)) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        form.Show();
        return form;
    }
    private void Configure(MainForm form)
    {
        Control<ComboBox>(form, "templatePicker").SelectedIndex = 0;
        Control<ComboBox>(form, "outputMode").SelectedIndex = 1;
        Control<TextBox>(form, "outputDirectory").Text = Path.Combine(workspace, "output");
    }
    private static T Control<T>(MainForm form, string name) where T : Control => Assert.IsType<T>(Assert.Single(form.Controls.Find(name, true)));
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static bool Shortcut(MainForm form, Keys keys) => (bool)typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [new Message(), keys])!;
    private static void KeyDown(FileRow row, Keys keys) => typeof(FileRow).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(row, [new KeyEventArgs(keys)]);
    private static void InvokeControlEvent(Control control, string name, EventArgs argument) => typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [argument]);
    private static string Fixture(string file) => Path.Combine(Root, "tests/fixtures", file);
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Md2Word.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("找不到项目根目录。");
    }
    private static void Capture(MainForm form, string name)
    {
        var directory = Path.Combine(Root, "artifacts/stage3/preview");
        Directory.CreateDirectory(directory);
        var surface = form.Controls[0];
        using var bitmap = new Bitmap(surface.Width, surface.Height);
        surface.DrawToBitmap(bitmap, surface.ClientRectangle);
        bitmap.Save(Path.Combine(directory, name + ".png"));
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            form.DeviceDpi,
            fontSize = form.Font.Size,
            width = form.ClientSize.Width,
            height = form.ClientSize.Height,
            controls = Descendants(form).Where(control => control.Name.Length > 0).Select(control => new { control.Name, control.Text, control.Visible, control.Enabled, x = form.PointToClient(control.PointToScreen(Point.Empty)).X, y = form.PointToClient(control.PointToScreen(Point.Empty)).Y, control.Width, control.Height })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Task OnUiThread(Func<Task> action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ThreadExceptionEventHandler fail = (_, error) => { completed.TrySetException(error.Exception); Application.ExitThread(); };
            Application.ThreadException += fail;
            using var context = new ApplicationContext();
            using var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                try { await action(); completed.TrySetResult(); }
                catch (Exception error) { completed.TrySetException(error); }
                finally { Application.ExitThread(); }
            };
            timer.Start();
            Application.Run(context);
            Application.ThreadException -= fail;
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }
}
