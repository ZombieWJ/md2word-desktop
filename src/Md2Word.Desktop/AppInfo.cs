using System.Reflection;
using System.Runtime.InteropServices;

namespace Md2Word.Desktop;

internal static class AppInfo
{
    internal static string Version => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    internal const string Repository = "https://github.com/ZombieWJ/md2word-desktop";

    internal static string Diagnostics(IEnumerable<QueueEntry> entries) => string.Join(Environment.NewLine,
        $"md2word：{Version}", $"系统：{RuntimeInformation.OSDescription}",
        $"架构：{RuntimeInformation.ProcessArchitecture}", $"运行时：{RuntimeInformation.FrameworkDescription}",
        "转换引擎要求：Pandoc3.10",
        $"队列文件数：{entries.Count()}",
        "问题类别：" + string.Join("、", entries.Where(e => e.Result?.Status == Core.ConversionStatus.Failed)
            .Select(e => ErrorHelp.Category(e.Result!.Error)).Distinct().DefaultIfEmpty("无")));
}

internal static class ErrorHelp
{
    internal static string Category(string? error) => error switch
    {
        null or "" => "未知",
        _ when error.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) => "文本编码",
        _ when error.Contains("权限", StringComparison.Ordinal) => "访问权限",
        _ when error.Contains("图片", StringComparison.Ordinal) => "图片资源",
        _ when error.Contains("模板", StringComparison.Ordinal) => "模板资源",
        _ when error.Contains("Pandoc", StringComparison.OrdinalIgnoreCase) => "转换引擎",
        _ when error.Contains("找不到Markdown", StringComparison.Ordinal) => "源文件",
        _ => "其他转换问题"
    };

    internal static string Guidance(string? error) => Category(error) switch
    {
        "文本编码" => "请将Markdown保存为UTF-8编码后重试。",
        "访问权限" => "无法读取文件或保存结果。请检查文件权限，或选择可写入的输出文件夹。",
        "图片资源" => "请按下方原因检查图片是否存在、路径及图片格式，修复后重试。",
        "模板资源" => "请检查模板文件；安装资源缺失或损坏时，重新安装后重试。",
        "转换引擎" => "转换引擎未能完成操作。请查看下方详情；程序资源缺失时重新安装。",
        "源文件" => "找不到源文件。请确认文件仍在原位置，或移除后重新添加。",
        _ => "转换未完成，请查看下方详情。无法解决时可复制原因并附上脱敏的最小样例反馈。"
    };
}
