using System.ComponentModel;
using Md2Word.Core;

namespace Md2Word.Desktop;

internal sealed class FileQueueView : UserControl
{
    private readonly FlowLayoutPanel rows = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White };
    private readonly Label empty = new() { Text = "拖入Markdown文件，或点击“添加文件”", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, AutoEllipsis = true };
    private readonly List<FileRow> fileRows = [];
    private readonly HashSet<QueueEntry> selected = [];
    private int selectionAnchor;
    private DragEventHandler? enterDrop;
    private DragEventHandler? acceptDrop;

    internal event Action? SelectionChanged;
    internal event Action<QueueEntry>? OpenRequested;
    internal event Action<QueueEntry>? RevealRequested;
    internal event Action? ReconvertRequested;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool CanReconvert { get; set; } = true;
    internal IReadOnlyList<QueueEntry> SelectedEntries => selected.ToArray();
    internal IReadOnlyList<FileRow> FileRows => fileRows;

    internal FileQueueView()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        Name = "fileQueue";
        AccessibleName = "文件列表";
        AccessibleRole = AccessibleRole.List;
        BorderStyle = BorderStyle.FixedSingle;
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(245, 246, 248), Padding = new Padding(12, 0, 12, 0), ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 144));
        header.Controls.Add(new Label { Text = "文件", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        header.Controls.Add(new Label { Text = "状态", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 1, 0);
        Controls.Add(rows);
        Controls.Add(empty);
        Controls.Add(header);
        rows.SizeChanged += (_, _) => ResizeRows();
        SizeChanged += (_, _) => ResizeRows();
        ResumeLayout(true);
    }

    internal void SetEntries(IReadOnlyList<QueueEntry> entries)
    {
        SuspendLayout();
        rows.SuspendLayout();
        foreach (var row in fileRows) row.Dispose();
        rows.Controls.Clear();
        fileRows.Clear();
        selected.RemoveWhere(entry => !entries.Contains(entry));
        foreach (var entry in entries)
        {
            var row = new FileRow(entry) { Margin = Padding.Empty };
            row.SelectRequested += modifiers => SelectEntry(row, modifiers);
            row.NavigateRequested += (delta, modifiers) => Navigate(row, delta, modifiers);
            row.OpenRequested += () => OpenRequested?.Invoke(entry);
            row.RevealRequested += () => RevealRequested?.Invoke(entry);
            var menu = new ContextMenuStrip();
            var reconvert = menu.Items.Add("重新转换选中");
            reconvert.Click += (_, _) => ReconvertRequested?.Invoke();
            menu.Opening += (_, args) =>
            {
                if (!selected.Contains(entry)) SelectEntry(row, Keys.None);
                reconvert.Enabled = CanReconvert && selected.Any(item => item.Result?.Status is ConversionStatus.Succeeded or ConversionStatus.Warning);
                args.Cancel = !reconvert.Enabled;
            };
            row.AttachContextMenu(menu);
            row.Selected = selected.Contains(entry);
            fileRows.Add(row);
            rows.Controls.Add(row);
            if (enterDrop is not null && acceptDrop is not null) BindDropTargets(row, enterDrop, acceptDrop);
        }
        rows.Visible = entries.Count > 0;
        empty.Visible = entries.Count == 0;
        rows.ResumeLayout();
        ResumeLayout();
        ResizeRows();
        SelectionChanged?.Invoke();
    }

    internal void RefreshEntries()
    {
        foreach (var row in fileRows) row.RefreshEntry();
        ResizeRows();
    }

    internal void SelectAllEntries()
    {
        foreach (var row in fileRows) { selected.Add(row.Entry); row.Selected = true; }
        SelectionChanged?.Invoke();
    }

    internal void AcceptFileDrops(DragEventHandler enter, DragEventHandler drop)
    {
        enterDrop = enter;
        acceptDrop = drop;
        BindDropTargets(this, enter, drop);
    }

    private static void BindDropTargets(Control control, DragEventHandler enter, DragEventHandler drop)
    {
        control.AllowDrop = true;
        control.DragEnter += enter;
        control.DragDrop += drop;
        foreach (Control child in control.Controls) BindDropTargets(child, enter, drop);
    }

    internal void SelectEntry(FileRow row, Keys modifiers)
    {
        var index = fileRows.IndexOf(row);
        if ((modifiers & Keys.Shift) != 0)
        {
            if ((modifiers & Keys.Control) == 0) selected.Clear();
            for (var i = Math.Min(selectionAnchor, index); i <= Math.Max(selectionAnchor, index) && i < fileRows.Count; i++) selected.Add(fileRows[i].Entry);
        }
        else if ((modifiers & Keys.Control) != 0)
        {
            if (!selected.Add(row.Entry)) selected.Remove(row.Entry);
            selectionAnchor = index;
        }
        else { selected.Clear(); selected.Add(row.Entry); selectionAnchor = index; }
        foreach (var item in fileRows) item.Selected = selected.Contains(item.Entry);
        SelectionChanged?.Invoke();
    }

    private void Navigate(FileRow row, int delta, Keys modifiers)
    {
        var index = Math.Clamp(fileRows.IndexOf(row) + delta, 0, fileRows.Count - 1);
        var next = fileRows[index];
        SelectEntry(next, modifiers);
        next.Focus();
        rows.ScrollControlIntoView(next);
    }

    private void ResizeRows()
    {
        var width = Math.Max(1, rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1);
        foreach (var row in fileRows) row.Width = width;
    }
}

internal sealed class FileRow : UserControl
{
    private readonly Label filename = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, Margin = Padding.Empty };
    private readonly LinkLabel open = new() { Text = "打开文档", AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
    private readonly LinkLabel reveal = new() { Text = "在文件夹中显示", AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
    private readonly LinkLabel reason = new() { Text = "查看原因", AutoSize = true, Margin = Padding.Empty };
    private readonly TextBox details = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(245, 246, 248), Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), AccessibleName = "转换原因" };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
    private readonly TableLayoutPanel content = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12, 6, 12, 7) };
    private readonly ToolTip tips = new();
    private bool expanded;
    private bool canOpen;
    private bool canExplain;
    private ContextMenuStrip? rowMenu;

    internal QueueEntry Entry { get; }
    internal event Action<Keys>? SelectRequested;
    internal event Action<int, Keys>? NavigateRequested;
    internal event Action? OpenRequested;
    internal event Action? RevealRequested;
    internal bool HasRevealAction => canOpen;
    internal bool IsExpanded => expanded;
    internal bool HasOpenAction => canOpen;
    internal bool HasReasonAction => canExplain;
    internal bool StatusTextFits => TextRenderer.MeasureText(status.Text, status.Font).Width <= status.ClientSize.Width;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => BackColor == Color.FromArgb(237, 243, 255);
        set { BackColor = value ? Color.FromArgb(237, 243, 255) : Color.White; Invalidate(); }
    }

    internal FileRow(QueueEntry entry)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Entry = entry;
        Name = "fileRow";
        AccessibleName = Path.GetFileName(entry.InputPath);
        AccessibleRole = AccessibleRole.ListItem;
        TabStop = true;
        BackColor = Color.White;
        Padding = new Padding(0, 0, 0, 1);
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.ColumnStyles.Add(new(SizeType.Absolute, 144));
        content.RowStyles.Add(new(SizeType.Absolute, 28));
        content.RowStyles.Add(new(SizeType.Absolute, 0));
        content.RowStyles.Add(new(SizeType.Absolute, 0));
        content.Controls.Add(filename, 0, 0);
        content.Controls.Add(status, 1, 0);
        actions.Controls.Add(open);
        actions.Controls.Add(reveal);
        actions.Controls.Add(reason);
        content.Controls.Add(actions, 0, 1);
        content.SetColumnSpan(actions, 2);
        content.Controls.Add(details, 0, 2);
        content.SetColumnSpan(details, 2);
        Controls.Add(content);
        filename.Text = Path.GetFileName(entry.InputPath);
        tips.SetToolTip(filename, entry.InputPath);
        foreach (var control in new Control[] { this, content, filename, status, actions })
            control.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) { SelectRequested?.Invoke(ModifierKeys); Focus(); } };
        open.LinkClicked += (_, _) => OpenRequested?.Invoke();
        reveal.LinkClicked += (_, _) => RevealRequested?.Invoke();
        reason.LinkClicked += (_, _) => ToggleReason();
        SizeChanged += (_, _) => ResizeDetails();
        RefreshEntry();
        ResumeLayout(true);
    }

    internal void ToggleReason()
    {
        if (Entry.Details.Length == 0) return;
        expanded = !expanded;
        RefreshEntry();
    }

    internal void RefreshEntry()
    {
        status.Text = Entry.Status;
        status.ForeColor = Entry.Result?.Status switch
        {
            ConversionStatus.Succeeded => Color.FromArgb(35, 100, 67),
            ConversionStatus.Warning => Color.FromArgb(139, 92, 10),
            ConversionStatus.Failed => Color.FromArgb(174, 52, 52),
            _ => SystemColors.ControlText
        };
        canOpen = !Entry.IsRunning && Entry.Result?.OutputPath is not null;
        canExplain = !Entry.IsRunning && Entry.Details.Length > 0;
        open.Visible = canOpen;
        reveal.Visible = canOpen;
        reason.Visible = canExplain;
        if (!canExplain) expanded = false;
        reason.Text = expanded ? "收起原因" : "查看原因";
        details.Text = Entry.Details;
        details.Visible = expanded;
        actions.Visible = canOpen || canExplain;
        AccessibleDescription = Entry.Status + (Entry.Result?.OutputPath is not null ? "；可打开文档" : "") + (Entry.Details.Length > 0 ? "；可查看原因" : "");
        ResizeDetails();
    }

    internal void AttachContextMenu(ContextMenuStrip menu)
    {
        rowMenu = menu;
        void Attach(Control control)
        {
            control.ContextMenuStrip = menu;
            foreach (Control child in control.Controls) Attach(child);
        }
        Attach(this);
    }

    private void ResizeDetails()
    {
        content.ColumnStyles[1].Width = Math.Max(LogicalToDeviceUnits(144), TextRenderer.MeasureText("已完成，请检查", Font).Width + content.Padding.Horizontal);
        var lineHeight = Math.Max(Font.Height, 16);
        var actionHeight = canOpen || canExplain ? lineHeight + 8 : 0;
        var detailHeight = expanded ? Math.Clamp(TextRenderer.MeasureText(details.Text, Font, new Size(Math.Max(100, Width - content.Padding.Horizontal - 24), 0), TextFormatFlags.WordBreak).Height + 24, lineHeight * 3, lineHeight * 8) : 0;
        content.RowStyles[0].Height = lineHeight + 12;
        content.RowStyles[1].Height = actionHeight;
        content.RowStyles[2].Height = detailHeight;
        Height = content.Padding.Vertical + lineHeight + 12 + actionHeight + detailHeight + 1;
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down)
        { NavigateRequested?.Invoke(e.KeyCode == Keys.Up ? -1 : 1, e.Modifiers); e.Handled = true; }
        else if (e.KeyCode == Keys.Space) { SelectRequested?.Invoke(Keys.Control); e.Handled = true; e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Enter && canOpen) { OpenRequested?.Invoke(); e.Handled = true; }
        else if (e.KeyCode == Keys.F4 && canExplain) { ToggleReason(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(225, 228, 233));
        e.Graphics.DrawLine(border, 0, Height - 1, Width, Height - 1);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { tips.Dispose(); rowMenu?.Dispose(); }
        base.Dispose(disposing);
    }
}
