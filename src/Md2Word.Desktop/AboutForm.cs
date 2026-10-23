using System.Runtime.InteropServices;

namespace Md2Word.Desktop;

internal sealed class AboutForm : Form
{
    internal AboutForm(string diagnostics, Action<string> openLink)
    {
        Text = "关于与帮助";
        Icon = AppBranding.WindowIcon;
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(470, 370);
        MinimumSize = new Size(430, 350);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = $"md2word {AppInfo.Version}", AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        layout.Controls.Add(new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Text = "使用说明\r\n\r\n1．添加或拖入Markdown文件。\r\n2．选择模板和输出位置。\r\n3．点击转换，再打开生成的文档。\r\n\r\n中文通用会将顶层无序列表改为编号。\r\n重名时另存，不修改源文件。\r\n已完成文件可右键选择“重新转换选中”。\r\n\r\n转换无需联网或安装Word；查看和编辑DOCX需要相应软件。" });
        var links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 8) };
        foreach (var (text, url) in new[] { ("项目主页", AppInfo.Repository), ("反馈问题", AppInfo.Repository + "/issues") })
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 20, 0) };
            link.LinkClicked += (_, _) => openLink(url);
            links.Controls.Add(link);
        }
        layout.Controls.Add(links);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        var copy = new Button { Text = "复制诊断信息", AutoSize = true };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(diagnostics); copy.Text = "已复制"; }
            catch (ExternalException) { MessageBox.Show(this, "剪贴板暂时不可用，请稍后重试。", "复制失败"); }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        CancelButton = close;
    }
}
