using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Md2Word.Core;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Md2Word.Tests;

public sealed class ConversionTests
{
    private static readonly string Root = FindRoot();
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly ConversionResources Resources = new(
        Environment.GetEnvironmentVariable("MD2WORD_TEST_PANDOC") ?? Path.Combine(Root, "pandoc-3.10", "pandoc.exe"),
        Path.Combine(Root, "resources", "convert.lua"),
        Path.Combine(Root, "templates", "cn-general", "reference.docx"));
    private readonly string output = Path.Combine(Root, "artifacts", "test-runs", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("basic.md", "中文转换测试")]
    [InlineData("headings.md", "标题层级测试")]
    [InlineData("lists.md", "编号和嵌套测试")]
    [InlineData("tables.md", "表格排版测试")]
    [InlineData("code-links.md", "代码与链接测试")]
    public async Task FiveFixturesGenerateValidEditableDocx(string fixture, string title)
    {
        var referenceHash = SHA256.HashData(File.ReadAllBytes(Resources.ReferencePath));
        var source = Fixture(fixture);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
        var result = await Convert(source);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Empty(result.Warnings);
        using var document = WordprocessingDocument.Open(result.OutputPath!, false);
        Assert.Empty(new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2013).Validate(document));
        var main = document.MainDocumentPart!;
        var paragraphs = main.Document!.Body!.Elements<W.Paragraph>().ToArray();
        Assert.Equal(title, paragraphs[0].InnerText);
        Assert.Equal("title", StyleName(main, paragraphs[0]));
        Assert.Equal(W.JustificationValues.Center, paragraphs[0].ParagraphProperties!.Justification!.Val!.Value);
        Assert.False(main.DocumentSettingsPart!.Settings!.GetFirstChild<W.UpdateFieldsOnOpen>()!.Val!.Value);
        Assert.True(string.IsNullOrEmpty(document.PackageProperties.Creator));
        Assert.True(string.IsNullOrEmpty(document.PackageProperties.LastModifiedBy));
        Assert.Equal(referenceHash, SHA256.HashData(File.ReadAllBytes(Resources.ReferencePath)));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Empty(Directory.GetFiles(output, ".md2word-*"));
    }

    [Fact]
    public async Task PromotesOnlyLeadingTitleAndKeepsLaterHeading()
    {
        var result = await Convert(Fixture("headings.md"));
        using var document = WordprocessingDocument.Open(Succeeded(result), false);
        var main = document.MainDocumentPart!;
        var paragraphs = main.Document!.Descendants<W.Paragraph>().ToArray();
        Assert.Equal("heading1", StyleName(main, Assert.Single(paragraphs, p => p.InnerText == "1 项目范围")));
        Assert.Equal("heading2", StyleName(main, Assert.Single(paragraphs, p => p.InnerText == "1.1 输入说明")));
        Assert.Equal("heading3", StyleName(main, Assert.Single(paragraphs, p => p.InnerText == "深层标题")));
        Assert.Equal("heading1", StyleName(main, Assert.Single(paragraphs, p => p.InnerText == "后续一级标题")));
        foreach (var paragraph in paragraphs.Where(p => StyleName(main, p).StartsWith("heading", StringComparison.Ordinal) || StyleName(main, p) == "title"))
        {
            var properties = paragraph.ParagraphProperties!;
            Assert.False(properties.KeepNext!.Val!.Value);
            Assert.False(properties.KeepLines!.Val!.Value);
            Assert.False(properties.PageBreakBefore!.Val!.Value);
            Assert.False(properties.SuppressLineNumbers!.Val!.Value);
        }
        Assert.Contains("80µm、0x0A、≤5和0.60 L/min", main.Document.InnerText);
    }

    [Theory]
    [InlineData("basic.md")]
    [InlineData("headings.md")]
    [InlineData("lists.md")]
    [InlineData("tables.md")]
    [InlineData("code-links.md")]
    [InlineData("long-text.md")]
    [InlineData("wide-table.md")]
    [InlineData("long-table.md")]
    public async Task AllSourceTextRemainsInDocumentOrder(string fixture)
    {
        var input = Fixture(fixture);
        var parsed = await PandocProcess.RunAsync(Resources.PandocPath, ["--from=gfm", "--to=json", input], Root, CancellationToken.None);
        Assert.Equal(0, parsed.ExitCode);
        var tokens = new List<string>();
        CollectText(JsonNode.Parse(parsed.Output)!["blocks"]!, tokens);
        var result = await Convert(input);
        using var document = WordprocessingDocument.Open(Succeeded(result), false);
        var text = Compact(document.MainDocumentPart!.Document!.InnerText);
        var position = 0;
        foreach (var token in tokens.Select(Compact).Where(token => token.Length > 0))
        {
            var found = text.IndexOf(token, position, StringComparison.Ordinal);
            Assert.True(found >= position, $"未按顺序保留源文内容：{token}");
            position = found + token.Length;
        }
    }

    [Fact]
    public async Task NumberingRestartsWithoutFlatteningNestedListsOrRemovingOtherEmphasis()
    {
        var result = await Convert(Fixture("lists.md"));
        using var document = WordprocessingDocument.Open(Succeeded(result), false);
        var main = document.MainDocumentPart!;
        var paragraphs = main.Document!.Descendants<W.Paragraph>().ToArray();
        var number = Assert.Single(paragraphs, p => p.InnerText.StartsWith("第十项", StringComparison.Ordinal));
        Assert.Equal("397", number.ParagraphProperties!.Indentation!.Left!.Value);
        Assert.Equal("113", number.ParagraphProperties.Indentation.Hanging!.Value);
        var numbering = Xml(main.NumberingDefinitionsPart!);
        Assert.Contains(numbering.Descendants(Wn + "lvl"), level => (string?)level.Element(Wn + "numFmt")?.Attribute(Wn + "val") == "bullet");
        foreach (var level in numbering.Descendants(Wn + "lvl").Where(level => (string?)level.Attribute(Wn + "ilvl") == "0" && (string?)level.Element(Wn + "numFmt")?.Attribute(Wn + "val") == "decimal"))
        {
            Assert.Equal("1", (string?)level.Element(Wn + "start")?.Attribute(Wn + "val"));
            Assert.Equal("%1.", (string?)level.Element(Wn + "lvlText")?.Attribute(Wn + "val"));
            Assert.Equal("right", (string?)level.Element(Wn + "lvlJc")?.Attribute(Wn + "val"));
        }
        var lead = Assert.Single(paragraphs, p => p.InnerText.StartsWith("说明：", StringComparison.Ordinal));
        Assert.DoesNotContain(lead.Descendants<W.Run>(), run => run.InnerText.Contains("说明", StringComparison.Ordinal) && run.RunProperties?.Bold is not null);
        Assert.Contains(paragraphs.SelectMany(p => p.Descendants<W.Run>()), run => run.InnerText == "重要内容" && run.RunProperties?.Bold is not null);
        Assert.Contains("嵌套无序事项保留类型。", main.Document.InnerText);
    }

    [Fact]
    public async Task OriginalKeepsOrderedStartBulletsAndLeadBold()
    {
        var result = await Convert(Fixture("lists.md"), DocumentTemplate.Original);
        using var document = WordprocessingDocument.Open(Succeeded(result), false);
        var main = document.MainDocumentPart!;
        var numbering = Xml(main.NumberingDefinitionsPart!);
        Assert.Contains(numbering.Descendants(), item => item.Name == Wn + "startOverride" && (string?)item.Attribute(Wn + "val") == "7" || item.Name == Wn + "start" && (string?)item.Attribute(Wn + "val") == "7");
        Assert.Contains(main.Document!.Descendants<W.Run>(), run => run.InnerText.Contains("检查项目", StringComparison.Ordinal) && run.RunProperties?.Bold is not null);
    }

    [Fact]
    public async Task TablesHaveWhiteCellsBlackBordersRepeatingHeadersAndBoundedWidth()
    {
        var result = await Convert(Fixture("tables.md"));
        using var document = WordprocessingDocument.Open(Succeeded(result), false);
        var main = document.MainDocumentPart!;
        var tables = main.Document!.Descendants<W.Table>().ToArray();
        Assert.Equal(2, tables.Length);
        foreach (var table in tables)
        {
            Assert.Equal(9638, table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().Sum(column => int.Parse(column.Width!.Value!)));
            var properties = table.GetFirstChild<W.TableProperties>()!;
            Assert.Equal("113", properties.GetFirstChild<W.TableCellMarginDefault>()!.TopMargin!.Width!.Value);
            foreach (var border in properties.GetFirstChild<W.TableBorders>()!.Elements())
            {
                Assert.Equal("000000", border.GetAttribute("color", Wn.NamespaceName).Value);
                Assert.Equal("4", border.GetAttribute("sz", Wn.NamespaceName).Value);
            }
            var header = table.Elements<W.TableRow>().First();
            Assert.NotNull(header.TableRowProperties!.GetFirstChild<W.TableHeader>());
            Assert.All(header.Descendants<W.Paragraph>(), paragraph => Assert.Equal(W.JustificationValues.Center, paragraph.ParagraphProperties!.Justification!.Val!.Value));
            Assert.All(header.Descendants<W.Run>().Where(run => run.InnerText.Length > 0), run => Assert.True(run.RunProperties!.Bold!.Val!.Value));
            Assert.DoesNotContain(table.Descendants<W.Shading>(), shading => shading.Fill?.Value is not (null or "auto" or "FFFFFF"));
            Assert.All(table.Descendants<W.TableCell>(), cell => Assert.NotNull(cell.TableCellProperties!.GetFirstChild<W.TableCellBorders>()));
        }
    }

    [Fact]
    public async Task CodeAndHyperlinkTargetsArePreserved()
    {
        var result = await Convert(Fixture("code-links.md"));
        using var document = WordprocessingDocument.Open(Succeeded(result), false);
        var main = document.MainDocumentPart!;
        Assert.Contains("**不要删除星号**", main.Document!.InnerText);
        Assert.Contains("**保留星号**", main.Document.InnerText);
        Assert.Contains(@"D:\中文目录\file.md", main.Document.InnerText);
        Assert.Contains(main.HyperlinkRelationships, link => link.Uri.AbsoluteUri == "https://pandoc.org/");
        Assert.Contains(main.HyperlinkRelationships, link => link.Uri.AbsoluteUri == "https://example.org/?a=1&b=2");
    }

    [Fact]
    public async Task TitleCanBeDisabledAndFallbackDoesNotRemoveLaterHeading()
    {
        var disabled = await new ConversionService(Resources).ConvertAsync(new(Fixture("headings.md"), output, DocumentTemplate.CnGeneral, false));
        using (var document = WordprocessingDocument.Open(Succeeded(disabled), false))
        {
            var main = document.MainDocumentPart!;
            Assert.Equal("heading1", StyleName(main, main.Document!.Body!.Elements<W.Paragraph>().First()));
        }
        Directory.CreateDirectory(output);
        var input = Path.Combine(output, "没有文首标题.md");
        await File.WriteAllTextAsync(input, "正文开头。\n\n# 后续标题\n\n保留后续内容。", Encoding.UTF8);
        var fallback = await Convert(input);
        using var fallbackDocument = WordprocessingDocument.Open(Succeeded(fallback), false);
        Assert.StartsWith("没有文首标题", fallbackDocument.MainDocumentPart!.Document!.InnerText);
        Assert.Contains("后续标题", fallbackDocument.MainDocumentPart.Document.InnerText);
    }

    [Fact]
    public async Task RepeatedConversionDoesNotOverwriteExistingOutput()
    {
        var first = await Convert(Fixture("basic.md"));
        var firstPath = Succeeded(first);
        var firstHash = SHA256.HashData(File.ReadAllBytes(firstPath));
        var second = await Convert(Fixture("basic.md"));
        Assert.EndsWith("basic（2）.docx", Succeeded(second));
        Assert.Equal(firstHash, SHA256.HashData(File.ReadAllBytes(firstPath)));
    }

    [Fact]
    public async Task MissingTemplateAndMalformedTemplateFailWithoutFinalFiles()
    {
        var missing = await new ConversionService(Resources with { ReferencePath = Path.Combine(output, "missing.docx") })
            .ConvertAsync(new(Fixture("basic.md"), output, DocumentTemplate.CnGeneral));
        Assert.Equal(ConversionStatus.Failed, missing.Status);
        Assert.Null(missing.OutputPath);
        Directory.CreateDirectory(output);
        var broken = Path.Combine(output, "broken.docx");
        await File.WriteAllTextAsync(broken, "not a document");
        var invalid = await new ConversionService(Resources with { ReferencePath = broken })
            .ConvertAsync(new(Fixture("basic.md"), output, DocumentTemplate.CnGeneral));
        Assert.Equal(ConversionStatus.Failed, invalid.Status);
        Assert.Null(invalid.OutputPath);
        Assert.Single(Directory.GetFiles(output, "*.docx"));
        Assert.Empty(Directory.GetFiles(output, ".md2word-*"));
    }

    [Fact]
    public async Task MissingLocalImageFailsAndRemoteImageWarnsWithoutFetching()
    {
        Directory.CreateDirectory(output);
        var input = Path.Combine(output, "资源检查.md");
        await File.WriteAllTextAsync(input, "# 图片检查\n\n![本地图](missing.png)");
        var missing = await Convert(input);
        Assert.Equal(ConversionStatus.Failed, missing.Status);
        Assert.Contains("找不到图片", missing.Error!);
        Assert.Null(missing.OutputPath);
        await File.WriteAllTextAsync(input, "# 图片检查\n\n![网络图](https://127.0.0.1:1/never.png)");
        var remote = await Convert(input);
        Assert.Equal(ConversionStatus.Warning, remote.Status);
        using var document = WordprocessingDocument.Open(remote.OutputPath!, false);
        Assert.Empty(document.MainDocumentPart!.ImageParts);
        Assert.Contains("图片未嵌入", document.MainDocumentPart.Document!.InnerText);
        Assert.DoesNotContain(document.MainDocumentPart.ExternalRelationships, relation => relation.RelationshipType.Contains("image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChineseAndSpacePathsAndUtf8ValidationWork()
    {
        Directory.CreateDirectory(output);
        var input = Path.Combine(output, "中文 空格.md");
        await File.WriteAllTextAsync(input, "# 中文标题\n\n80µm与0.60 L/min。", new UTF8Encoding(false));
        var result = await Convert(input);
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        await File.WriteAllBytesAsync(input, [0xff, 0xff, 0xff]);
        var invalid = await Convert(input);
        Assert.Equal(ConversionStatus.Failed, invalid.Status);
        Assert.Contains("UTF-8", invalid.Error!);
        await File.WriteAllBytesAsync(input, [0xff, 0xfe, 0x41, 0x00]);
        var utf16 = await Convert(input);
        Assert.Equal(ConversionStatus.Failed, utf16.Status);
        Assert.Contains("UTF-8", utf16.Error!);
    }

    [Fact]
    public async Task CancellationDoesNotPublishPartialOutput()
    {
        using var cancel = new CancellationTokenSource();
        var progress = new InlineProgress(value => { if (value.Stage == "生成Word") cancel.Cancel(); });
        var result = await new ConversionService(Resources).ConvertAsync(new(Fixture("basic.md"), output, DocumentTemplate.CnGeneral), progress, cancel.Token);
        Assert.Equal(ConversionStatus.Cancelled, result.Status);
        Assert.Null(result.OutputPath);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task ProcessCancellationStopsOwnedProcess()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PandocProcess.RunAsync(executable, ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"], Root, cancel.Token));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10));
    }

    private Task<ConversionResult> Convert(string input, DocumentTemplate template = DocumentTemplate.CnGeneral) =>
        new ConversionService(Resources).ConvertAsync(new(input, output, template));
    private static string Fixture(string name) => Path.Combine(Root, "tests", "fixtures", name);
    private static string Succeeded(ConversionResult result)
    {
        Assert.True(result.Status == ConversionStatus.Succeeded, result.Error ?? string.Join(";", result.Warnings));
        return Assert.IsType<string>(result.OutputPath);
    }
    private static string StyleName(MainDocumentPart main, W.Paragraph paragraph)
    {
        var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return main.StyleDefinitionsPart!.Styles!.Elements<W.Style>().FirstOrDefault(style => style.StyleId?.Value == id)?.StyleName?.Val?.Value?.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant() ?? "";
    }
    private static XDocument Xml(OpenXmlPart part) { using var stream = part.GetStream(); return XDocument.Load(stream); }
    private static string Compact(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    private static void CollectText(JsonNode node, List<string> tokens)
    {
        if (node is JsonObject element)
        {
            var type = element["t"]?.GetValue<string>();
            if (type == "Str") { tokens.Add(element["c"]!.GetValue<string>()); return; }
            if (type is "Code" or "CodeBlock") { tokens.Add(element["c"]![1]!.GetValue<string>()); return; }
            foreach (var value in element.Select(item => item.Value).OfType<JsonNode>()) CollectText(value, tokens);
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>()) CollectText(child, tokens);
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Md2Word.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("找不到测试项目根目录。");
    }
    private sealed class InlineProgress(Action<ConversionProgress> callback) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => callback(value);
    }
}
