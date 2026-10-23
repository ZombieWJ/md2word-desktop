using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;

namespace Md2Word.Core;

public sealed class ConversionService(ConversionResources resources)
{
    public async Task<ConversionResult> ConvertAsync(
        ConversionRequest request, IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        string? temporaryDocx = null, temporaryJson = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(request.Template)) throw new ArgumentException("请选择有效模板。");
            var input = Path.GetFullPath(request.InputPath);
            if (!File.Exists(input)) throw new FileNotFoundException("找不到Markdown文件。", input);
            if (!new[] { ".md", ".markdown" }.Contains(Path.GetExtension(input), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("请选择.md或.markdown文件。");
            _ = new UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(input, cancellationToken).ConfigureAwait(false));
            if (!File.Exists(resources.PandocPath)) throw new FileNotFoundException("找不到Pandoc，请检查程序资源或指定Pandoc路径。", resources.PandocPath);
            if (!File.Exists(resources.FilterPath)) throw new FileNotFoundException("找不到转换过滤器，请恢复程序资源。", resources.FilterPath);
            if (request.Template == DocumentTemplate.CnGeneral && !File.Exists(resources.ReferencePath))
                throw new FileNotFoundException("找不到CN-General模板，请恢复模板文件。", resources.ReferencePath);
            var sourceDirectory = Path.GetDirectoryName(input)!;
            var outputDirectory = Path.GetFullPath(request.OutputDirectory ?? sourceDirectory);
            Directory.CreateDirectory(outputDirectory);
            var version = await PandocProcess.RunAsync(resources.PandocPath, ["--version"], sourceDirectory, cancellationToken).ConfigureAwait(false);
            if (version.ExitCode != 0 || version.Output.Split('\n')[0].Trim() != "pandoc 3.10")
                throw new IOException("此版本工具需要Pandoc3.10，请检查转换引擎版本。");

            progress?.Report(new("读取Markdown"));
            var parsed = await PandocProcess.RunAsync(resources.PandocPath, ["--from=gfm", "--to=json", input], sourceDirectory, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(parsed);
            CollectDiagnostic(parsed, warnings);
            var ast = JsonNode.Parse(parsed.Output) ?? throw new IOException("Pandoc未返回有效的文档结构。");
            progress?.Report(new("检查资源"));
            MarkdownResources.Prepare(ast, sourceDirectory, warnings);
            var metadata = ast["meta"]!.AsObject();
            metadata["md2word-template"] = MetaString(request.Template == DocumentTemplate.CnGeneral ? "cn-general" : "original");
            metadata["md2word-title-mode"] = MetaString(request.UseFirstHeadingAsTitle ? "first-heading" : "keep-levels");
            metadata["md2word-filename"] = MetaString(Path.GetFileNameWithoutExtension(input));
            var token = Guid.NewGuid().ToString("N");
            temporaryDocx = Path.Combine(outputDirectory, $".md2word-{token}.docx");
            temporaryJson = Path.Combine(outputDirectory, $".md2word-{token}.json");
            await File.WriteAllTextAsync(temporaryJson, ast.ToJsonString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var filterPath = Path.GetFullPath(resources.FilterPath);
            var arguments = new List<string>
            {
                "--from=json", "--to=docx", "--standalone", "--lua-filter", Path.GetFileName(filterPath),
                "--resource-path", sourceDirectory, "--output", temporaryDocx, temporaryJson
            };
            if (request.Template == DocumentTemplate.CnGeneral) arguments.AddRange(["--reference-doc", resources.ReferencePath]);
            progress?.Report(new("生成Word"));
            // Lua加载器使用系统代码页处理文件名，规则文件以相对名称加载，兼容中文安装目录。
            var generated = await PandocProcess.RunAsync(resources.PandocPath, arguments, Path.GetDirectoryName(filterPath)!, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(generated);
            CollectDiagnostic(generated, warnings);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("套用格式并检查文档"));
            DocxFormatter.Apply(temporaryDocx, request.Template, resources.ReferencePath);
            cancellationToken.ThrowIfCancellationRequested();
            var destination = SaveWithoutOverwrite(temporaryDocx, outputDirectory, Path.GetFileNameWithoutExtension(input));
            temporaryDocx = null;
            CleanTemporary(ref temporaryJson, warnings);
            progress?.Report(new("转换完成"));
            return new(warnings.Count == 0 ? ConversionStatus.Succeeded : ConversionStatus.Warning, destination, warnings.AsReadOnly());
        }
        catch (OperationCanceledException) { return new(ConversionStatus.Cancelled, null, warnings.AsReadOnly(), "已取消转换。"); }
        catch (DecoderFallbackException) { return new(ConversionStatus.Failed, null, warnings.AsReadOnly(), "文件不是有效的UTF-8文本，请转换编码后重试。"); }
        catch (UnauthorizedAccessException ex) { return new(ConversionStatus.Failed, null, warnings.AsReadOnly(), $"没有读取或保存权限：{ex.Message}"); }
        catch (Exception ex) when (ex is IOException or ArgumentException or Win32Exception or System.Text.Json.JsonException or System.Xml.XmlException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
        {
            return new(ConversionStatus.Failed, null, warnings.AsReadOnly(), ex.Message);
        }
        finally
        {
            CleanTemporary(ref temporaryDocx, warnings);
            CleanTemporary(ref temporaryJson, warnings);
        }
    }

    private static void CleanTemporary(ref string? temporaryPath, List<string> warnings)
    {
        if (temporaryPath is not { } path) return;
        temporaryPath = null;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warnings.Add($"无法清理临时文件{path}：{ex.Message}"); }
    }

    private static JsonObject MetaString(string value) => new() { ["t"] = "MetaString", ["c"] = value };
    private static void EnsureSuccess(ProcessOutput output)
    {
        if (output.ExitCode != 0) throw new IOException($"Pandoc转换失败（退出码{output.ExitCode}）：{output.Diagnostic.Trim()}");
    }
    private static void CollectDiagnostic(ProcessOutput output, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(output.Diagnostic)) warnings.Add(output.Diagnostic.Trim());
    }
    private static string SaveWithoutOverwrite(string temporary, string directory, string stem)
    {
        for (var suffix = 1; ; suffix++)
        {
            var name = suffix == 1 ? $"{stem}.docx" : $"{stem}（{suffix}）.docx";
            var target = Path.Combine(directory, name);
            try { File.Move(temporary, target, overwrite: false); return target; }
            catch (IOException) when (File.Exists(target)) { /* 另一任务可能先保存同名文件，继续选择新名称。 */ }
        }
    }
}
