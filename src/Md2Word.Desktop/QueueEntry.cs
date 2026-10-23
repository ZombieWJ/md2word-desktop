using Md2Word.Core;

namespace Md2Word.Desktop;

internal sealed class QueueEntry(string inputPath)
{
    internal string InputPath { get; } = inputPath;
    internal ConversionResult? Result { get; set; }
    internal bool IsRunning { get; set; }
    internal string Status => IsRunning ? "转换中" : Result?.Status switch
    {
        ConversionStatus.Succeeded => "已完成",
        ConversionStatus.Warning => "已完成，请检查",
        ConversionStatus.Failed => "失败",
        ConversionStatus.Cancelled => "已取消",
        _ => "待转换"
    };
    internal string Details => string.Join(Environment.NewLine, new[] { Result?.Status == ConversionStatus.Failed ? ErrorHelp.Guidance(Result.Error) : null, Result?.Error }
        .Concat(Result?.Warnings ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)));
}
