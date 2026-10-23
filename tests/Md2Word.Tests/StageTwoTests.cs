using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Md2Word.Core;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Md2Word.Tests;

public sealed class StageTwoTests
{
    private static readonly string Root = FindRoot();
    private static readonly ConversionResources Resources = new(
        Environment.GetEnvironmentVariable("MD2WORD_TEST_PANDOC") ?? Path.Combine(Root, "pandoc-3.10", "pandoc.exe"), Path.Combine(Root, "resources", "convert.lua"),
        Path.Combine(Root, "templates", "cn-general", "reference.docx"));
    private readonly string output = Path.Combine(Root, "artifacts", "test-runs", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ChineseResourceDirectoryPreservesLocalImagesAndOutput()
    {
        var resourcesDirectory = Directory.CreateDirectory(Path.Combine(output, "中文 程序", "resources")).FullName;
        var filter = Path.Combine(resourcesDirectory, "convert.lua");
        File.Copy(Resources.FilterPath, filter);
        var converter = new ConversionService(Resources with { FilterPath = filter });
        var result = await converter.ConvertAsync(new(Fixture("local-images.md"), Path.Combine(output, "中文 结果"), DocumentTemplate.CnGeneral));
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var document = WordprocessingDocument.Open(result.OutputPath!, false);
        Assert.Equal(2, document.MainDocumentPart!.ImageParts.Count());
        Assert.Contains("图片之后的内容必须保留", document.MainDocumentPart.Document!.InnerText);
    }

    [Theory]
    [InlineData("local-images.md", 2)]
    [InlineData("large-image.md", 1)]
    [InlineData("long-text.md", 0)]
    [InlineData("wide-table.md", 0)]
    [InlineData("long-table.md", 0)]
    [InlineData("中文 空格.md", 1)]
    [InlineData("images-in-table.md", 1)]
    public async Task ExtendedFixturesHaveValidStructureAndEmbeddedResources(string fixture, int imageParts)
    {
        var input = Fixture(fixture);
        var before = SHA256.HashData(File.ReadAllBytes(input));
        var result = await Convert(input);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        using var document = WordprocessingDocument.Open(result.OutputPath!, false);
        Assert.Empty(new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2013).Validate(document));
        Assert.Equal(imageParts, document.MainDocumentPart!.ImageParts.Count());
        Assert.DoesNotContain(document.MainDocumentPart.ExternalRelationships, item => item.RelationshipType.EndsWith("/image", StringComparison.Ordinal));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(input)));
        Assert.Empty(Directory.GetFiles(output, ".md2word-*"));
    }

    [Theory]
    [InlineData("missing-image.md", "找不到图片")]
    [InlineData("unsupported-image.md", "暂不支持此图片格式")]
    [InlineData("corrupt-image.md", "无法解码图片")]
    public async Task BadImagesFailWithoutPublishingOutput(string fixture, string reason)
    {
        var result = await Convert(Fixture(fixture));
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains(reason, result.Error!);
        Assert.Null(result.OutputPath);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task ImagesPreserveBytesAndAspectRatioAndFitTheirContainers()
    {
        var local = await Convert(Fixture("local-images.md"));
        using (var document = WordprocessingDocument.Open(local.OutputPath!, false))
        {
            var hashes = document.MainDocumentPart!.ImageParts.Select(part =>
            { using var stream = part.GetStream(); return ConvertHash(stream); }).ToArray();
            foreach (var file in new[] { "diagram.png", "photo.jpg" })
                Assert.Contains(System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Fixture("assets/" + file)))), hashes);
        }
        foreach (var fixture in new[] { "large-image.md", "images-in-table.md" })
        {
            var result = await Convert(Fixture(fixture));
            using var document = WordprocessingDocument.Open(result.OutputPath!, false);
            var drawing = Assert.Single(document.MainDocumentPart!.Document!.Descendants<W.Drawing>());
            Assert.Equal(W.LineSpacingRuleValues.Auto, drawing.Ancestors<W.Paragraph>().First().ParagraphProperties!.SpacingBetweenLines!.LineRule!.Value);
            var extent = Assert.Single(drawing.Descendants<DW.Extent>());
            Assert.InRange(extent.Cx!.Value, 1, 9638L * 635);
            Assert.InRange(extent.Cy!.Value, 1, 12170L * 635);
            var ratio = (double)extent.Cx.Value / extent.Cy.Value;
            Assert.InRange(ratio, fixture == "large-image.md" ? 0.1499 : 2.3808, fixture == "large-image.md" ? 0.1501 : 2.3811);
            if (drawing.Ancestors<W.TableCell>().FirstOrDefault() is { } cell)
                Assert.True(extent.Cx.Value <= (long.Parse(cell.TableCellProperties!.TableCellWidth!.Width!.Value!) - 216) * 635);
        }
    }

    [Fact]
    public async Task WideAndLongTablesKeepAllColumnsRowsAndRepeatingHeaders()
    {
        foreach (var fixture in new[] { "wide-table.md", "long-table.md" })
        {
            var result = await Convert(Fixture(fixture));
            using var document = WordprocessingDocument.Open(result.OutputPath!, false);
            var table = Assert.Single(document.MainDocumentPart!.Document!.Descendants<W.Table>());
            var grid = table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().ToArray();
            Assert.Equal(fixture == "wide-table.md" ? 8 : 3, grid.Length);
            Assert.Equal(9638, grid.Sum(column => int.Parse(column.Width!.Value!)));
            Assert.NotNull(table.Elements<W.TableRow>().First().TableRowProperties!.GetFirstChild<W.TableHeader>());
            Assert.Empty(table.Descendants<W.TableRowHeight>());
            if (fixture == "long-table.md")
            {
                var rows = table.Elements<W.TableRow>().Skip(1).ToArray();
                Assert.Equal(40, rows.Length);
                for (var index = 0; index < rows.Length; index++) Assert.StartsWith($"{index + 1:000}合成项目{index + 1:000}", rows[index].InnerText);
                Assert.Contains("第040条之后的结束文字。", document.MainDocumentPart.Document.InnerText);
            }
        }
    }

    [Fact]
    public async Task UnsupportedLayoutsRetainAltTextAndSourceAndWarn()
    {
        var result = await Convert(Fixture("warnings.md"));
        Assert.Equal(ConversionStatus.Warning, result.Status);
        Assert.Contains(result.Warnings, value => value.Contains("未下载", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, value => value.Contains("html", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, value => value.Contains("Mermaid", StringComparison.Ordinal));
        using var document = WordprocessingDocument.Open(result.OutputPath!, false);
        var main = document.MainDocumentPart!;
        foreach (var text in new[] { "网络示意重点", "图片未嵌入", "<div style=\"display:grid\">", "A-->B", "警告之后的正文。" })
            Assert.Contains(text, main.Document!.InnerText);
        Assert.Empty(main.ImageParts);
    }

    [Fact]
    public async Task WrongImageExtensionFailsWithActionableReason()
    {
        Directory.CreateDirectory(output);
        File.Copy(Fixture("assets/photo.jpg"), Path.Combine(output, "wrong.png"));
        var input = Path.Combine(output, "wrong.md");
        await File.WriteAllTextAsync(input, "![错误扩展名](wrong.png)");
        var result = await Convert(input);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("扩展名与实际格式不符", result.Error!);
        Assert.Empty(Directory.GetFiles(output, "*.docx"));
    }

    [Fact]
    public async Task LockedOutputIsPreservedAndConcurrentConversionsChooseDistinctNames()
    {
        Directory.CreateDirectory(output);
        var occupied = Path.Combine(output, "basic.docx");
        await File.WriteAllTextAsync(occupied, "原有文件不能覆盖");
        using (var locked = new FileStream(occupied, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var results = await Task.WhenAll(Convert(Fixture("basic.md")), Convert(Fixture("basic.md")));
            Assert.All(results, result => Assert.Equal(ConversionStatus.Succeeded, result.Status));
            Assert.Equal(2, results.Select(result => result.OutputPath).Distinct().Count());
            Assert.Contains(results, result => result.OutputPath!.EndsWith("basic（2）.docx", StringComparison.Ordinal));
            Assert.Contains(results, result => result.OutputPath!.EndsWith("basic（3）.docx", StringComparison.Ordinal));
        }
        Assert.Equal("原有文件不能覆盖", await File.ReadAllTextAsync(occupied));
    }

    [Fact]
    public async Task DeniedOutputDirectoryFailsAndItsPermissionsAreRestored()
    {
        Assert.True(OperatingSystem.IsWindows(), "此项目的权限验收要求Windows。");
        if (!OperatingSystem.IsWindows()) return;
        var denied = Directory.CreateDirectory(Path.Combine(output, "denied"));
        var original = denied.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var restricted = denied.GetAccessControl();
        var identity = WindowsIdentity.GetCurrent().User!;
        restricted.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.Write, AccessControlType.Deny));
        try
        {
            denied.SetAccessControl(restricted);
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(denied.FullName, "probe"), "test"));
            var result = await new ConversionService(Resources).ConvertAsync(new(Fixture("basic.md"), denied.FullName, DocumentTemplate.CnGeneral));
            Assert.Equal(ConversionStatus.Failed, result.Status);
            Assert.Contains("权限", result.Error!);
            Assert.Empty(Directory.GetFiles(denied.FullName));
        }
        finally
        {
            // 从SDDL新建描述符才能将访问规则标记为已修改并实际写回。
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorSddlForm(original, AccessControlSections.Access);
            denied.SetAccessControl(restored);
        }
        await File.WriteAllTextAsync(Path.Combine(denied.FullName, "restored.txt"), "写入权限已恢复");
    }

    [Fact]
    public async Task BatchContinuesAfterFailureAndReportsActualFileCounts()
    {
        var updates = new List<BatchConversionProgress>();
        var requests = new[] { "basic.md", "missing-image.md", "warnings.md", "tables.md" }
            .Select(file => new ConversionRequest(Fixture(file), output, DocumentTemplate.CnGeneral)).ToArray();
        var results = await new BatchConversionService(new(Resources)).ConvertAsync(requests, new BatchProgress(updates.Add));
        Assert.Equal(new[] { ConversionStatus.Succeeded, ConversionStatus.Failed, ConversionStatus.Warning, ConversionStatus.Succeeded }, results.Select(result => result.Status));
        Assert.Equal(3, Directory.GetFiles(output, "*.docx").Length);
        Assert.All(updates, update => Assert.Equal(4, update.TotalFiles));
        Assert.Equal(new[] { 1, 2, 3, 4 }, updates.Where(update => update.Result is not null).Select(update => update.FileNumber));
    }

    [Fact]
    public async Task CleanupFailureIsReportedAsWarningEvenAfterSuccessfulSave()
    {
        FileStream? lockedTemporary = null;
        string? temporary = null;
        try
        {
            var progress = new StageProgress(value =>
            {
                if (value.Stage != "套用格式并检查文档") return;
                temporary = Assert.Single(Directory.GetFiles(output, ".md2word-*.json"));
                lockedTemporary = new(temporary, FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            var result = await new ConversionService(Resources).ConvertAsync(new(Fixture("basic.md"), output, DocumentTemplate.CnGeneral), progress);
            Assert.Equal(ConversionStatus.Warning, result.Status);
            Assert.True(File.Exists(result.OutputPath));
            Assert.Contains(result.Warnings, value => value.Contains("无法清理临时文件", StringComparison.Ordinal));
        }
        finally
        {
            lockedTemporary?.Dispose();
            if (temporary is not null) File.Delete(temporary);
        }
    }

    [Fact]
    public async Task BatchCancellationRemovesGeneratedTemporaryDocxAndPreservesCompletedOutput()
    {
        using var cancellation = new CancellationTokenSource();
        var sawTemporaryDocx = false;
        var progress = new BatchProgress(value =>
        {
            if (value.FileNumber == 2 && value.Stage == "套用格式并检查文档")
            {
                sawTemporaryDocx = Directory.GetFiles(output, ".md2word-*.docx").Length == 1;
                cancellation.Cancel();
            }
        });
        var requests = new[] { "basic.md", "local-images.md", "tables.md" }
            .Select(file => new ConversionRequest(Fixture(file), output, DocumentTemplate.CnGeneral)).ToArray();
        var results = await new BatchConversionService(new(Resources)).ConvertAsync(requests, progress, cancellation.Token);
        Assert.True(sawTemporaryDocx);
        Assert.Equal(new[] { ConversionStatus.Succeeded, ConversionStatus.Cancelled, ConversionStatus.Cancelled }, results.Select(result => result.Status));
        Assert.Single(Directory.GetFiles(output, "*.docx"));
        Assert.Empty(Directory.GetFiles(output, ".md2word-*"));
    }

    private Task<ConversionResult> Convert(string input) => new ConversionService(Resources).ConvertAsync(new(input, output, DocumentTemplate.CnGeneral));
    private static string Fixture(string file) => Path.Combine(Root, "tests", "fixtures", file);
    private static string ConvertHash(Stream stream) => System.Convert.ToHexString(SHA256.HashData(stream));
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Md2Word.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("找不到项目根目录。");
    }
    private sealed class BatchProgress(Action<BatchConversionProgress> callback) : IProgress<BatchConversionProgress>
    {
        public void Report(BatchConversionProgress value) => callback(value);
    }
    private sealed class StageProgress(Action<ConversionProgress> callback) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => callback(value);
    }
}
