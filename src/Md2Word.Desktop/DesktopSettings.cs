using System.Text.Json;
using System.Text.Json.Serialization;
using Md2Word.Core;

namespace Md2Word.Desktop;

internal sealed record DesktopSettings(
    DocumentTemplate? Template = DocumentTemplate.CnGeneral,
    bool UseFirstHeadingAsTitle = true,
    bool UseCustomOutput = false,
    string OutputDirectory = "");

internal sealed class SettingsStore(string directory)
{
    internal static string ResolveDirectory(string applicationDirectory, string localApplicationData) =>
        File.Exists(Path.Combine(applicationDirectory, "portable.flag"))
            ? applicationDirectory : Path.Combine(localApplicationData, "md2word");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal (DesktopSettings Settings, string? Notice) Load()
    {
        var path = Path.Combine(directory, "settings.json");
        try
        {
            if (!File.Exists(path)) return (new(), null);
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path), JsonOptions);
            if (settings is null || settings.Template is { } template && !Enum.IsDefined(template) || settings?.OutputDirectory is null)
                return (new(), "设置无法读取，已使用默认设置。");
            return (settings!, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return (new(), "设置无法读取，已使用默认设置。"); }
    }

    internal string? Save(DesktopSettings settings)
    {
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, Path.Combine(directory, "settings.json"), overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "设置无法保存，本次选择仍可使用。"; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine($"无法清理设置临时文件：{ex.Message}"); }
        }
    }
}
