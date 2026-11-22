using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacDock;

// Curated macOS-style menu for a drawer/dock item. Every file command is executed by the shell
// (IFileOperation / OLE clipboard / property sheet); the full Explorer menu is one click away.
internal static class EntryMenu
{
    public static void Fill(ContextMenu menu, Controller c, IReadOnlyList<Entry> entries, bool includeRunningActions = true)
    {
        menu.Items.Clear();
        if (entries.Count == 0) return;
        var many = entries.Count > 1;
        var real = entries.Where(e => !e.IsVirtual && (File.Exists(e.Path) || Directory.Exists(e.Path))).ToList();
        var mirrored = real.Where(e => e.Source=="desktop"&&Desktop.IsDesktopPath(e.Path)).ToList();
        var manual = entries.Where(e => e.IsAlias).ToList();
        var single = entries.Count == 1 ? entries[0] : null;

        void Add(string label, string? hint, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Header(label, hint), IsEnabled = enabled };
            System.Windows.Automation.AutomationProperties.SetName(item,label);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        void Gap() => menu.Items.Add(new Separator());

        Add(single == null ? $"打开 {entries.Count} 个项目" : "打开", "Enter", () => c.OpenEntries(entries));
        if (single != null && !single.IsVirtual && File.Exists(single.Path) && !Directory.Exists(single.Path))
            Add("打开方式…", null, () => c.OpenWithEntry(single));
        if (single != null)
            Add("在资源管理器中显示", null, () => c.RevealEntry(single));
        if (single != null || real.Count > 0)
            Add("Windows 更多操作…", null, () => c.ShowNativeMenu(entries));

        Gap();
        if (real.Count > 0)
        {
            Add(single?.IsAlias==true?"复制原文件":"复制", "Ctrl+C", () => c.CopyEntries(entries, false));
            Add("剪切", "Ctrl+X", () => c.CopyEntries(entries, true),entries.All(e=>!e.IsAlias));
        }
        Add("粘贴到桌面", "Ctrl+V", c.PasteToDesktop, c.CanPaste);
        if (real.Count > 0)
        {
            Gap();
            Add("压缩为 ZIP…", null, () => c.CompressEntries(real));
        }
        if (single != null && !single.IsVirtual && ShellOps.CanExtract(single.Path))
            Add("解压到文件夹…", null, () => c.ExtractEntry(single));

        Gap();
        if (single != null && !single.IsVirtual)
            Add(single.IsAlias ? "重命名显示名称…" : "重命名…", "F2", () => c.RenameEntry(single));
        if (mirrored.Count > 0)
        {
            Add(many ? $"将 {mirrored.Count} 个项目移到回收站" : "移到回收站", "Delete", () => c.DeleteEntries(mirrored, false));
            Add("永久删除…", "Shift+Del", () => c.DeleteEntries(mirrored, true));
        }
        if (real.Count > 0)
            Add("属性", "Alt+Enter", () => c.ShowProperties(entries));
        if (manual.Count > 0)
            Add(many ? $"移除 {manual.Count} 个快捷入口（保留原文件）" : "移除快捷入口（保留原文件）", null, () => c.RemoveEntries(manual));

        Gap();
        var allPinned = entries.All(e => e.Pinned);
        Add(allPinned ? "从 Dock 取消固定" : "固定到 Dock", null, () => c.SetPinned(entries, !allPinned));
        if (includeRunningActions && single != null && c.CanCloseEntry(single))
        {
            Gap();
            Add("关闭", null, () => c.CloseEntry(single));
        }
    }

    static Grid Header(string label, string? hint)
    {
        var grid = new Grid { Width = 218, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Theme.Text(label, 12.5);
        grid.Children.Add(text);
        if (hint != null)
        {
            var right = Theme.Text(hint, 11, true);
            right.Opacity = .62;
            right.Margin = new Thickness(18, 0, 0, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        return grid;
    }
}
