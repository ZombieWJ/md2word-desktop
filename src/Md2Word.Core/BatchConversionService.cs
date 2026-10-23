namespace Md2Word.Core;

public sealed record BatchConversionProgress(int FileNumber, int TotalFiles, string InputPath, string Stage, ConversionResult? Result = null);

/// <summary>串行转换；取消后为未开始的文件返回取消状态，保留已完成结果。</summary>
public sealed class BatchConversionService(ConversionService converter)
{
    public async Task<IReadOnlyList<ConversionResult>> ConvertAsync(
        IReadOnlyList<ConversionRequest> requests, IProgress<BatchConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ConversionResult>(requests.Count);
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            ConversionResult result;
            if (cancellationToken.IsCancellationRequested)
                result = new(ConversionStatus.Cancelled, null, Array.Empty<string>(), "已取消转换。");
            else
            {
                var current = index + 1;
                var stages = new InlineProgress(stage => progress?.Report(new(current, requests.Count, request.InputPath, stage.Stage)));
                result = await converter.ConvertAsync(request, stages, cancellationToken).ConfigureAwait(false);
            }
            results.Add(result);
            progress?.Report(new(index + 1, requests.Count, request.InputPath,
                result.Status == ConversionStatus.Cancelled ? "已取消" : "文件处理结束", result));
        }
        return results.AsReadOnly();
    }

    private sealed class InlineProgress(Action<ConversionProgress> callback) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => callback(value);
    }
}
