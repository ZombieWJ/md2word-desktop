using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using W = DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace Md2Word.Core;

internal static class DocxFormatter
{
    private const string Font = "微软雅黑";

    internal static void Apply(string path, DocumentTemplate template, string referencePath)
    {
        using var document = WordprocessingDocument.Open(path, true);
        var main = document.MainDocumentPart ?? throw new IOException("生成的Word文档缺少正文。");
        var settingsPart = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings ??= new W.Settings();
        Set(settingsPart.Settings, new W.UpdateFieldsOnOpen { Val = false });
        settingsPart.Settings.RemoveAllChildren<W.AttachedTemplate>();
        foreach (var relationship in settingsPart.ExternalRelationships.ToArray()) settingsPart.DeleteExternalRelationship(relationship.Id);
        settingsPart.Settings.Save();
        if (template == DocumentTemplate.CnGeneral)
        {
            using var reference = WordprocessingDocument.Open(referencePath, false);
            ApplyStyles(main);
            ApplyNumbering(main);
            ApplyTables(main, reference.MainDocumentPart!);
        }
        BoundImages(main);
        var properties = document.PackageProperties;
        properties.Creator = null;
        properties.LastModifiedBy = null;
        properties.Created = null;
        properties.Modified = null;
        properties.LastPrinted = null;
        properties.Revision = null;
        properties.Subject = null;
        properties.Description = null;
        properties.Keywords = null;
        properties.Category = null;
        var extended = document.ExtendedFilePropertiesPart;
        if (extended is not null) document.DeletePart(extended);
        var custom = document.CustomFilePropertiesPart;
        if (custom is not null) document.DeletePart(custom);
        main.Document!.Save();
        var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(document).Take(5).ToArray();
        if (errors.Length > 0)
            throw new IOException("生成的Word文档结构检查失败：" + string.Join("；", errors.Select(error => $"{error.Path?.XPath}：{error.Description}")));
    }

    private static void ApplyStyles(MainDocumentPart main)
    {
        var styles = main.StyleDefinitionsPart?.Styles ?? throw new IOException("生成的文档缺少样式。");
        var specialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in styles.Elements<W.Style>())
        {
            var name = style.StyleName?.Val?.Value?.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant() ?? "";
            var isHeading = name.StartsWith("heading", StringComparison.Ordinal);
            if (name is "normal" or "bodytext" or "firstparagraph" or "listparagraph" or "listnumber" || isHeading || name is "title" or "subtitle" or "caption")
            {
                var run = style.StyleRunProperties ??= new W.StyleRunProperties();
                Set(run, new W.RunFonts { Ascii = Font, HighAnsi = Font, EastAsia = Font, ComplexScript = Font });
            }
            if (isHeading || name is "title" or "subtitle" or "caption")
            {
                DisablePagination(style.StyleParagraphProperties ??= new W.StyleParagraphProperties());
                if (style.StyleId?.Value is { } id) specialIds.Add(id);
            }
            if (name == "title")
            {
                if (style.StyleId?.Value is { } id) titleIds.Add(id);
                Set(style.StyleParagraphProperties ??= new(), new W.Justification { Val = W.JustificationValues.Center });
            }
            if (name == "tablegrid")
                foreach (var shading in style.Descendants<W.Shading>().ToArray()) shading.Remove();
        }
        var defaults = styles.DocDefaults ??= new W.DocDefaults();
        var defaultRun = defaults.RunPropertiesDefault ??= new W.RunPropertiesDefault();
        var defaultProperties = defaultRun.RunPropertiesBaseStyle ??= new W.RunPropertiesBaseStyle();
        Set(defaultProperties, new W.RunFonts { Ascii = Font, HighAnsi = Font, EastAsia = Font, ComplexScript = Font });
        foreach (var paragraph in main.Document!.Descendants<W.Paragraph>())
        {
            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (styleId is null || !specialIds.Contains(styleId)) continue;
            var properties = paragraph.ParagraphProperties ??= new W.ParagraphProperties();
            DisablePagination(properties);
            if (titleIds.Contains(styleId))
            {
                Set(properties, new W.Justification { Val = W.JustificationValues.Center });
                properties.RemoveAllChildren<W.NumberingProperties>();
                properties.RemoveAllChildren<W.ParagraphBorders>();
            }
        }
        // 参考文件中的Subtitle和Caption把pPr放在rPr之后，复制到输出时按OOXML顺序重排。
        foreach (var style in styles.Elements<W.Style>())
        {
            var children = style.Elements().ToArray();
            style.RemoveAllChildren();
            foreach (var child in children) style.AddChild(child, throwOnError: true);
        }
        styles.Save();
    }

    private static void DisablePagination(OpenXmlCompositeElement properties)
    {
        Set(properties, new W.KeepNext { Val = false });
        Set(properties, new W.KeepLines { Val = false });
        Set(properties, new W.PageBreakBefore { Val = false });
        Set(properties, new W.SuppressLineNumbers { Val = false });
    }

    private static void ApplyNumbering(MainDocumentPart main)
    {
        var numbering = main.NumberingDefinitionsPart?.Numbering;
        if (numbering is null) return;
        var decimalDefinitions = new HashSet<int>();
        foreach (var definition in numbering.Elements<W.AbstractNum>())
        {
            var level = definition.Elements<W.Level>().FirstOrDefault(item => item.LevelIndex?.Value == 0);
            if (level?.NumberingFormat?.Val?.Value != W.NumberFormatValues.Decimal) continue;
            decimalDefinitions.Add(definition.AbstractNumberId!.Value);
            Set(level, new W.StartNumberingValue { Val = 1 });
            Set(level, new W.LevelText { Val = "%1." });
            Set(level, new W.LevelSuffix { Val = W.LevelSuffixValues.Tab });
            Set(level, new W.LevelJustification { Val = W.LevelJustificationValues.Right });
            var paragraph = level.PreviousParagraphProperties ??= new W.PreviousParagraphProperties();
            Set(paragraph, new W.Tabs(new W.TabStop { Val = W.TabStopValues.Number, Position = 397 }));
            Set(paragraph, new W.Indentation { Left = "397", Hanging = "113" });
            var run = level.NumberingSymbolRunProperties ??= new W.NumberingSymbolRunProperties();
            Set(run, new W.RunFonts { Ascii = Font, HighAnsi = Font, EastAsia = Font });
        }
        var decimalNumbers = new HashSet<int>();
        foreach (var number in numbering.Elements<W.NumberingInstance>())
        {
            if (number.AbstractNumId?.Val?.Value is not { } id || !decimalDefinitions.Contains(id)) continue;
            decimalNumbers.Add(number.NumberID!.Value);
            var startOverride = number.Elements<W.LevelOverride>().FirstOrDefault(item => item.LevelIndex?.Value == 0);
            if (startOverride is not null) Set(startOverride, new W.StartOverrideNumberingValue { Val = 1 });
        }
        foreach (var paragraph in main.Document!.Descendants<W.Paragraph>())
        {
            var properties = paragraph.ParagraphProperties;
            var number = properties?.NumberingProperties;
            if (number?.NumberingId?.Val?.Value is not { } id || !decimalNumbers.Contains(id) ||
                (number.NumberingLevelReference?.Val?.Value ?? 0) != 0) continue;
            Set(properties!, new W.Indentation { Left = "397", Hanging = "113" });
            Set(properties!, new W.Tabs(new W.TabStop { Val = W.TabStopValues.Number, Position = 397 }));
        }
        numbering.Save();
    }

    private static void ApplyTables(MainDocumentPart main, MainDocumentPart reference)
    {
        var section = main.Document!.Body?.Elements<W.SectionProperties>().LastOrDefault();
        var page = section?.GetFirstChild<W.PageSize>();
        var margins = section?.GetFirstChild<W.PageMargin>();
        var available = checked((int)(page?.Width?.Value ?? 11906) - (margins?.Left?.Value ?? 1134) - (margins?.Right?.Value ?? 1134));
        var sourceTable = reference.Document!.Descendants<W.Table>().FirstOrDefault();
        var sourceProperties = sourceTable?.GetFirstChild<W.TableProperties>();
        var sourceMargins = sourceProperties?.GetFirstChild<W.TableCellMarginDefault>();
        var sourceCell = sourceTable?.Descendants<W.TableCell>().FirstOrDefault()?.TableCellProperties;
        foreach (var table in main.Document.Descendants<W.Table>().ToArray())
        {
            var rows = table.Elements<W.TableRow>().ToArray();
            var columns = table.GetFirstChild<W.TableGrid>()?.Elements<W.GridColumn>().Count() ?? 0;
            if (columns == 0) columns = rows.Select(row => row.Elements<W.TableCell>().Count()).DefaultIfEmpty(0).Max();
            if (columns == 0) continue;
            var weights = Enumerable.Range(0, columns).Select(index => (double)Math.Clamp(
                rows.Select(row => row.Elements<W.TableCell>().ElementAtOrDefault(index))
                    .Select(cell => Math.Max(cell?.InnerText.EnumerateRunes().Sum(rune => rune.Value > 255 ? 2 : 1) ?? 0,
                        cell?.Descendants<W.Drawing>().Any() == true ? 30 : 0)).DefaultIfEmpty(6).Max(), 6, 60)).ToArray();
            var minimum = Math.Min(720, available / columns);
            var remaining = available - minimum * columns;
            var widths = weights.Select(weight => minimum + (int)(remaining * weight / weights.Sum())).ToArray();
            widths[^1] += available - widths.Sum();
            var properties = table.GetFirstChild<W.TableProperties>() ?? table.PrependChild(new W.TableProperties());
            var gridStyle = main.StyleDefinitionsPart?.Styles?.Elements<W.Style>()
                .FirstOrDefault(style => string.Equals(style.StyleName?.Val?.Value, "Table Grid", StringComparison.OrdinalIgnoreCase))?.StyleId?.Value;
            if (gridStyle is not null) Set(properties, new W.TableStyle { Val = gridStyle });
            Set(properties, new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = available.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            Set(properties, new W.TableJustification { Val = W.TableRowAlignmentValues.Left });
            var indentation = sourceProperties?.GetFirstChild<W.TableIndentation>();
            Set(properties, indentation?.CloneNode(true) ?? new W.TableIndentation { Type = W.TableWidthUnitValues.Dxa, Width = 108 });
            Set(properties, new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" }));
            // Word2013导出时，白色直接底纹可能覆盖共享边框；无底纹配合白色页面保持网格完整。
            properties.RemoveAllChildren<W.Shading>();
            Set(properties, new W.TableLayout { Type = W.TableLayoutValues.Fixed });
            var cellMargins = new W.TableCellMarginDefault(
                new W.TopMargin { Width = sourceMargins?.TopMargin?.Width?.Value ?? "113", Type = W.TableWidthUnitValues.Dxa },
                new W.TableCellLeftMargin { Width = (short)(sourceMargins?.TableCellLeftMargin?.Width?.Value ?? 108), Type = W.TableWidthValues.Dxa },
                new W.BottomMargin { Width = sourceMargins?.BottomMargin?.Width?.Value ?? "113", Type = W.TableWidthUnitValues.Dxa },
                new W.TableCellRightMargin { Width = (short)(sourceMargins?.TableCellRightMargin?.Width?.Value ?? 108), Type = W.TableWidthValues.Dxa });
            Set(properties, cellMargins);
            Set(properties, new W.TableLook { Val = "0000", FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true });
            var grid = new W.TableGrid(widths.Select(width => new W.GridColumn { Width = width.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            Set(table, grid);
            foreach (var row in rows)
            {
                var header = row.TableRowProperties?.GetFirstChild<W.TableHeader>();
                var isHeader = header is not null && header.Val?.Value != W.OnOffOnlyValues.Off;
                if (isHeader) Set(row.TableRowProperties!, new W.TableHeader { Val = W.OnOffOnlyValues.On });
                var index = 0;
                foreach (var cell in row.Elements<W.TableCell>())
                {
                    var cellProperties = cell.TableCellProperties ??= new W.TableCellProperties();
                    Set(cellProperties, new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = widths[Math.Min(index++, widths.Length - 1)].ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    cellProperties.RemoveAllChildren<W.Shading>();
                    var borders = sourceCell?.GetFirstChild<W.TableCellBorders>();
                    Set(cellProperties, borders?.CloneNode(true) ?? new W.TableCellBorders(
                        new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                        new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                        new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" },
                        new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = "000000" }));
                    Set(cellProperties, new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });
                    if (!isHeader) continue;
                    foreach (var paragraph in cell.Elements<W.Paragraph>())
                        Set(paragraph.ParagraphProperties ??= new(), new W.Justification { Val = W.JustificationValues.Center });
                    foreach (var run in cell.Descendants<W.Run>()) Set(run.RunProperties ??= new(), new W.Bold { Val = true });
                }
            }
            if (table.NextSibling() is W.Paragraph following)
            {
                var paragraphProperties = following.ParagraphProperties ?? following.PrependChild(new W.ParagraphProperties());
                var spacing = (W.SpacingBetweenLines?)paragraphProperties.SpacingBetweenLines?.CloneNode(true) ?? new W.SpacingBetweenLines();
                spacing.Before = "120";
                Set(paragraphProperties, spacing);
            }
        }
    }

    private static void BoundImages(MainDocumentPart main)
    {
        var section = main.Document!.Body!.Elements<W.SectionProperties>().LastOrDefault();
        var page = section?.GetFirstChild<W.PageSize>();
        var margins = section?.GetFirstChild<W.PageMargin>();
        var width = ((long)(page?.Width?.Value ?? 11906) - (margins?.Left?.Value ?? 1134) - (margins?.Right?.Value ?? 1134)) * 635;
        // 在整页正文高度之外预留120磅给标题、说明和行距，避免短说明加长图形成大片空页。
        var height = ((long)(page?.Height?.Value ?? 16838) - (margins?.Top?.Value ?? 1134) - (margins?.Bottom?.Value ?? 1134) - 2400) * 635;
        foreach (var drawing in main.Document.Descendants<W.Drawing>())
        {
            var paragraph = drawing.Ancestors<W.Paragraph>().First();
            var properties = paragraph.ParagraphProperties ??= new W.ParagraphProperties();
            var spacing = (W.SpacingBetweenLines?)properties.SpacingBetweenLines?.CloneNode(true) ?? new W.SpacingBetweenLines();
            // 模板正文使用固定行高，直接继承会只显示行内图片的底部。
            spacing.Line = "240";
            spacing.LineRule = W.LineSpacingRuleValues.Auto;
            Set(properties, spacing);
            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            if (extent?.Cx?.Value is not > 0 || extent.Cy?.Value is not > 0) continue;
            var availableWidth = width;
            if (drawing.Ancestors<W.TableCell>().FirstOrDefault() is { } cell &&
                long.TryParse(cell.TableCellProperties?.TableCellWidth?.Width?.Value, out var cellWidth))
                availableWidth = Math.Min(width, Math.Max(1, cellWidth - 216) * 635);
            var scale = Math.Min(1, Math.Min((double)availableWidth / extent.Cx.Value, (double)height / extent.Cy.Value));
            if (scale >= 1) continue;
            var cx = Math.Max(1, (long)(extent.Cx.Value * scale));
            var cy = Math.Max(1, (long)(extent.Cy.Value * scale));
            extent.Cx = cx;
            extent.Cy = cy;
            foreach (var size in drawing.Descendants<A.Extents>()) { size.Cx = cx; size.Cy = cy; }
        }
    }

    private static void Set(OpenXmlCompositeElement parent, OpenXmlElement child)
    {
        foreach (var existing in parent.Elements().Where(element => element.LocalName == child.LocalName && element.NamespaceUri == child.NamespaceUri).ToArray()) existing.Remove();
        parent.AddChild(child, throwOnError: true);
    }
}
