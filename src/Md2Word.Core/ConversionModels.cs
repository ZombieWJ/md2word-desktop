namespace Md2Word.Core;

public enum DocumentTemplate { Original, CnGeneral }
public enum ConversionStatus { Succeeded, Warning, Failed, Cancelled }

public sealed record ConversionRequest(
    string InputPath,
    string? OutputDirectory,
    DocumentTemplate Template,
    bool UseFirstHeadingAsTitle = true);

public sealed record ConversionProgress(string Stage);

public sealed record ConversionResult(
    ConversionStatus Status,
    string? OutputPath,
    IReadOnlyList<string> Warnings,
    string? Error = null);

public sealed record ConversionResources(string PandocPath, string FilterPath, string ReferencePath)
{
    public static ConversionResources FromApplicationDirectory(string? pandocPath = null)
    {
        var root = AppContext.BaseDirectory;
        return new(
            pandocPath ?? Path.Combine(root, "pandoc", "pandoc.exe"),
            Path.Combine(root, "resources", "convert.lua"),
            Path.Combine(root, "templates", "cn-general", "reference.docx"));
    }
}
