using System.Text.Json.Nodes;

namespace Md2Word.Core;

internal static class MarkdownResources
{
    internal static void Prepare(JsonNode document, string directory, List<string> warnings)
    {
        Visit(document, directory, warnings);
    }

    private static void Visit(JsonNode node, string directory, List<string> warnings)
    {
        if (node is JsonObject element)
        {
            var type = element["t"]?.GetValue<string>();
            if (type == "Image")
            {
                var content = element["c"]!.AsArray();
                var target = content[2]![0]!.GetValue<string>();
                if (target.StartsWith("//", StringComparison.Ordinal) ||
                    (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile))
                {
                    warnings.Add($"未下载网络或内嵌图片：{target}。请改用本地PNG/JPEG图片。");
                    var alternative = content[1]!.DeepClone().AsArray();
                    alternative.Add(new JsonObject { ["t"] = "Str", ["c"] = $"[图片未嵌入：{target}]" });
                    element.Clear();
                    element["t"] = "Span";
                    element["c"] = new JsonArray(EmptyAttributes(), alternative);
                    return;
                }
                var decoded = Uri.UnescapeDataString(target);
                var path = Path.GetFullPath(Uri.TryCreate(decoded, UriKind.Absolute, out var localUri) && localUri.IsFile
                    ? localUri.LocalPath : decoded, directory);
                if (path.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException($"离线转换不读取网络共享图片：{target}");
                if (!File.Exists(path)) throw new FileNotFoundException($"找不到图片：{target}。请确认图片存在且路径正确。", path);
                if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    throw new IOException($"暂不支持此图片格式：{target}。请改用PNG或JPEG。");
                ValidateImage(path, target);
                content[2]![0] = path;
            }
            else if (type is "RawBlock" or "RawInline")
            {
                var content = element["c"]!.AsArray();
                var format = content[0]!.GetValue<string>();
                var text = content[1]!.GetValue<string>();
                warnings.Add($"暂不渲染{format}布局，已保留原始文本。");
                element.Clear();
                element["t"] = type == "RawBlock" ? "CodeBlock" : "Code";
                element["c"] = new JsonArray(EmptyAttributes(), JsonValue.Create(text));
                return;
            }
            else if (type == "CodeBlock" && element["c"]?[0]?[1] is JsonArray classes &&
                     classes.Any(value => value?.GetValue<string>() == "mermaid"))
                warnings.Add("暂不渲染Mermaid图，已保留代码。");

            foreach (var value in element.Select(item => item.Value).OfType<JsonNode>().ToArray())
                Visit(value, directory, warnings);
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>().ToArray()) Visit(child, directory, warnings);
    }

    private static JsonArray EmptyAttributes() => new(JsonValue.Create(""), new JsonArray(), new JsonArray());

    private static void ValidateImage(string path, string target)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new IOException("本地图片检查需要Windows10或更高版本。");
        try
        {
            using var stream = File.OpenRead(path);
            using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            var png = image.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Png.Guid;
            var jpeg = image.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Jpeg.Guid;
            if (!png && !jpeg) throw new IOException($"图片内容不是PNG或JPEG：{target}。请重新另存图片，不要只修改扩展名。");
            if (png != string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"图片扩展名与实际格式不符：{target}。请重新另存为正确格式。");
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        {
            throw new IOException($"无法解码图片：{target}。图片可能损坏或内存不足，请用图片工具重新另存为PNG/JPEG后重试。", ex);
        }
    }

}
