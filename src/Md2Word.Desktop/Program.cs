namespace Md2Word.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // 安装程序只检查存在性；允许多个窗口，不强制终止正在转换的实例。
        using var runningMarker = new Mutex(false, "Local\\md2word-desktop-running");
        string? pandoc = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] != "--pandoc") continue;
            if (++index >= args.Length) { MessageBox.Show("请指定Pandoc路径。", "启动失败"); return; }
            try { pandoc = Path.GetFullPath(args[index]); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            { MessageBox.Show("Pandoc路径无效。", "启动失败"); return; }
        }
        using var form = new MainForm(Md2Word.Core.ConversionResources.FromApplicationDirectory(pandoc));
        if (args.Contains("--smoke-test", StringComparer.Ordinal))
            form.Shown += (_, _) => form.BeginInvoke(new Action(() =>
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                process.Refresh();
                Environment.ExitCode = process.MainWindowHandle == form.Handle ? 0 : 3;
                form.Close();
            }));
        Application.Run(form);
    }
}
