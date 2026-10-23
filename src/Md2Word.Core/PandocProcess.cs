using System.Diagnostics;
using System.Text;

namespace Md2Word.Core;

internal sealed record ProcessOutput(int ExitCode, string Output, string Diagnostic);

internal static class PandocProcess
{
    internal static async Task<ProcessOutput> RunAsync(
        string executable, IEnumerable<string> arguments, string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动Pandoc。");
        var output = process.StandardOutput.ReadToEndAsync();
        var diagnostic = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 仅终止本次启动的进程；检查与终止之间允许进程正常退出。
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, diagnostic).ConfigureAwait(false);
            throw;
        }
        return new(process.ExitCode, await output.ConfigureAwait(false), await diagnostic.ConfigureAwait(false));
    }
}
