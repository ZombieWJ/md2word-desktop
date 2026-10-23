#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'artifacts/showcase/capture'
New-Item -ItemType Directory -Path $work -Force | Out-Null
# 独立采帧程序只在artifacts中构建，不改变应用接口或测试数量。
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../../../src/Md2Word.Desktop/Md2Word.Desktop.csproj" /></ItemGroup>
</Project>
'@ | Set-Content (Join-Path $work 'Capture.csproj') -Encoding utf8
@'
using System.Reflection;
using System.Text.Json;
using Md2Word.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var root = args[0];
        var work = Path.Combine(root, "artifacts/showcase");
        var frames = Path.Combine(work, "frames");
        Directory.CreateDirectory(frames);
        var assembly = Assembly.Load("Md2Word.Desktop");
        var formType = assembly.GetType("Md2Word.Desktop.MainForm", true)!;
        var storeType = assembly.GetType("Md2Word.Desktop.SettingsStore", true)!;
        var store = Activator.CreateInstance(storeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [Path.Combine(work, "settings")], null);
        Directory.CreateDirectory(Path.Combine(work, "settings"));
        var resources = new ConversionResources(Path.Combine(root,"pandoc-3.10/pandoc.exe"), Path.Combine(root,"resources/convert.lua"), Path.Combine(root,"templates/cn-general/reference.docx"));
        using var form = (Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.NonPublic, null, [resources, store], null)!;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.ShowInTaskbar = false;
        var methods = BindingFlags.Instance | BindingFlags.NonPublic;
        var captures = new List<object>();
        void Capture(string name)
        {
            form.Refresh();
            var surface = form.Controls[0];
            using var image = new Bitmap(surface.Width, surface.Height);
            surface.DrawToBitmap(image, surface.ClientRectangle);
            image.Save(Path.Combine(frames, name + ".png"));
            captures.Add(new { frame = name, form.DeviceDpi, image.Width, image.Height });
        }
        Control Named(string name) => form.Controls.Find(name, true).Single();
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(150);
                Capture("01-empty");
                var inputs = new[] { "work-report.md", "table.md", "mixed-images.md" }.Select(file => Path.Combine(root,"examples/showcase", file)).ToArray();
                formType.GetMethod("AddFiles", methods)!.Invoke(form, [inputs]);
                Capture("02-added");
                ((ComboBox)Named("outputMode")).SelectedIndex = 1;
                ((TextBox)Named("outputDirectory")).Text = Path.Combine(work,"ui-output");
                ((Button)Named("startConversion")).PerformClick();
                Capture("04-running");
                var run = (Task)formType.GetProperty("CurrentRun", methods)!.GetValue(form)!;
                await run.WaitAsync(TimeSpan.FromSeconds(45));
                // 完成后显示简洁的输出模式，避免在公开图片中暴露本机绝对目录。
                ((ComboBox)Named("outputMode")).SelectedIndex = 0;
                Capture("05-complete");
                var entries = (System.Collections.IEnumerable)formType.GetProperty("Entries", methods)!.GetValue(form)!;
                var results = new List<object>();
                foreach (var entry in entries)
                {
                    var result = (ConversionResult?)entry.GetType().GetProperty("Result", methods | BindingFlags.Public)!.GetValue(entry);
                    if (result?.Status != ConversionStatus.Succeeded) throw new InvalidOperationException("展示样例未成功转换。");
                    results.Add(new { status = result.Status.ToString(), output = Path.GetFileName(result.OutputPath) });
                }
                File.WriteAllText(Path.Combine(work,"capture.json"), JsonSerializer.Serialize(new { method = "WinForms DrawToBitmap", actualConversion = true, captures, results }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(work,"capture-error.txt"), error.ToString()); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}
'@ | Set-Content (Join-Path $work 'Program.cs') -Encoding utf8
$env:DOTNET_ROOT = Join-Path $root '.tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
& $dotnet build (Join-Path $work 'Capture.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw '采帧程序构建失败。' }
& $dotnet (Join-Path $work 'bin/Release/net10.0-windows/Capture.dll') $root
if ($LASTEXITCODE -ne 0) { throw '采帧失败，请查看artifacts/showcase/capture-error.txt。' }
