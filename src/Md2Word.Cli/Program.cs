using System.Text;
using Md2Word.Core;

Console.OutputEncoding = Encoding.UTF8;
using var runningMarker = OperatingSystem.IsWindows() ? new Mutex(false, "Local\\md2word-desktop-running") : null;
var inputs = new List<string>();
string? output = null, pandoc = null, templateName = null;
var title = true;
for (var i = 0; i < args.Length; i++)
{
    var argument = args[i];
    if (argument is "--help" or "-h") { ShowHelp(); return 0; }
    if (argument == "--keep-heading-levels") { title = false; continue; }
    if (argument is "--output" or "--template" or "--pandoc")
    {
        if (++i == args.Length) { Console.Error.WriteLine($"缺少参数值：{argument}"); return 2; }
        switch (argument)
        {
            case "--output": output = args[i]; break;
            case "--template": templateName = args[i]; break;
            case "--pandoc": pandoc = args[i]; break;
        }
    }
    else if (argument.StartsWith("-", StringComparison.Ordinal)) { Console.Error.WriteLine($"未知选项：{argument}"); return 2; }
    else inputs.Add(argument);
}
if (inputs.Count == 0 || templateName is not ("cn-general" or "original"))
{
    Console.Error.WriteLine("请指定输入文件，并通过--template选择cn-general或original。");
    ShowHelp(); return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
if (pandoc is not null)
{
    try { pandoc = Path.GetFullPath(pandoc); }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
    { Console.Error.WriteLine($"Pandoc路径无效：{ex.Message}"); return 2; }
}
var converter = new ConversionService(ConversionResources.FromApplicationDirectory(pandoc));
var template = templateName == "cn-general" ? DocumentTemplate.CnGeneral : DocumentTemplate.Original;
var progress = new BatchProgress(value =>
{
    if (value.Result is not { } result || result.Status == ConversionStatus.Cancelled) return;
    if (result.Status == ConversionStatus.Failed) Console.Error.WriteLine($"失败：{value.InputPath}\n{result.Error}");
    else Console.WriteLine($"{(result.Status == ConversionStatus.Warning ? "已完成，请检查" : "已完成")}：{result.OutputPath}");
    foreach (var warning in result.Warnings) Console.Error.WriteLine($"提示：{warning}");
});
var results = await new BatchConversionService(converter).ConvertAsync(
    inputs.Select(input => new ConversionRequest(input, output, template, title)).ToArray(), progress, cancellation.Token);
if (results.Any(result => result.Status == ConversionStatus.Cancelled))
{ Console.Error.WriteLine("已取消，完成的文件已保留。"); return 130; }
return results.Any(result => result.Status == ConversionStatus.Failed) ? 1 : 0;

static void ShowHelp() => Console.WriteLine("""
Markdown转Word
用法：Md2Word.Cli <文件.md> [更多文件.md] --template <cn-general|original> [选项]
  --output <目录>          默认保存到各源文件目录；重名时自动另存
  --keep-heading-levels    保留标题层级，不提取文档标题
  --pandoc <pandoc.exe>    指定Pandoc3.10路径
  --help                  查看帮助
""");

internal sealed class BatchProgress(Action<BatchConversionProgress> callback) : IProgress<BatchConversionProgress>
{
    public void Report(BatchConversionProgress value) => callback(value);
}
