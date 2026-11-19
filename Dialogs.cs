using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MacDock;

internal static class Dialogs
{
    static GlassWindow WindowShell(string title, double width, double height, bool modal)
    {
        var w = new GlassWindow(interactive:true)
        {
            Title = title, Width = width, Height = height, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(27, 33, 45)), Topmost = true, ShowInTaskbar = false,
            FontFamily = Theme.Ui, UseLayoutRounding = true
        };
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
        w.KeyDown += (_, a) => { if (a.Key == Key.Escape && !a.Handled) {a.Handled=true;w.Close();} };
        return w;
    }

    static Border Card(UIElement child) => new()
    {
        Child = child, CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 5, 14, 7),
        Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)), BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 0, 0, 5)
    };

    static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 11, FontFamily = Theme.Ui, Foreground = Theme.Faint,
        Margin = new Thickness(2, 8, 0, 4)
    };

    public static Window Settings(Controller c)
    {
        var area=c.WorkArea;
        var w=WindowShell("MacDock 设置",Math.Min(458,Math.Max(270,area.Width/c.Scale-24)),Math.Min(720,Math.Max(270,area.Height/c.Scale-28)),true);
        w.WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var body=new StackPanel{Margin=new Thickness(20,0,20,12)};
        var data=c.Store.Data;
        bool correcting=false;
        void Group(string title,params UIElement[] rows)
        {
            body.Children.Add(Section(title));
            var panel=new StackPanel();
            foreach(var row in rows)panel.Children.Add(row);
            body.Children.Add(Card(panel));
        }
        Button ButtonFor(string label,Action click)
        {
            var button=new Button{Style=Theme.Style("Pill"),Content=label,MinHeight=30,Padding=new Thickness(11,3,11,3)};
            button.Click+=(_,_)=>click();
            return button;
        }
        UIElement Row(string label,FrameworkElement control)
        {
            var row=new DockPanel{Margin=new Thickness(0,2,0,2),MinHeight=30};
            control.VerticalAlignment=VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(control,label);
            DockPanel.SetDock(control,Dock.Right);
            row.Children.Add(control);
            var text=Theme.Text(label,13);
            text.VerticalAlignment=VerticalAlignment.Center;text.TextWrapping=TextWrapping.Wrap;
            text.Margin=new Thickness(0,0,8,0);row.Children.Add(text);
            return row;
        }
        CheckBox Toggle(bool value,Func<bool,bool> save)
        {
            var toggle=new CheckBox{Style=Theme.Style("Switch"),IsChecked=value};
            toggle.Click+=(_,_)=>{bool next=toggle.IsChecked==true;if(!save(next))toggle.IsChecked=!next;};
            return toggle;
        }
        ComboBox Choice(IEnumerable<string> values,int selected)
        {
            return new ComboBox{Style=Theme.Style("Combo"),Width=142,ItemsSource=values,SelectedIndex=selected};
        }
        var startup=Toggle(Controller.StartupEnabled(),value=>
        {
            try{Controller.SetStartup(value);return true;}
            catch(Exception error){DiagnosticsLog.Error(error,"settings.startup");c.Notify("无法保存开机启动设置。");return false;}
        });
        var motion=Toggle(data.ReducedMotion,value=>c.SaveSetting(()=>data.ReducedMotion,v=>data.ReducedMotion=v,value));
        var openMode=Choice(new[]{"单击打开","双击打开"},data.DoubleClickToOpen?1:0);
        openMode.SelectionChanged+=(_,_)=>{if(correcting||openMode.SelectedIndex<0)return;if(!c.SaveSetting(()=>data.DoubleClickToOpen,v=>data.DoubleClickToOpen=v,openMode.SelectedIndex==1)){correcting=true;openMode.SelectedIndex=data.DoubleClickToOpen?1:0;correcting=false;}else { c.Dock.ClearDoubleClickState(); c.Drawer?.ApplySelectionVisuals(); }};
        bool recordingKey=false;
        var held=new HashSet<int>();
        string? recordingError=null;
        var bindKey=ButtonFor(c.WakeShortcutDisplay,()=>{});
        bindKey.MinWidth=88;bindKey.Margin=new Thickness(0);bindKey.Effect=null;bindKey.FocusVisualStyle=null;
        var clearKey=ButtonFor("清除",()=>{});
        clearKey.Margin=new Thickness(6,0,0,0);clearKey.Effect=null;clearKey.FocusVisualStyle=null;
        void RefreshSuppressButtons()
        {
            bindKey.Content=recordingKey?(recordingError??"请按下按键…"):c.WakeShortcutDisplay;
            System.Windows.Automation.AutomationProperties.SetItemStatus(bindKey,(string)bindKey.Content);
            clearKey.IsEnabled=!recordingKey&&c.HasWakeShortcut;
        }
        void EndRecording()
        {
            recordingKey=false;
            held.Clear();
            recordingError=null;
            RefreshSuppressButtons();
        }
        RefreshSuppressButtons();
        bindKey.Click+=(_,_)=>{if(recordingKey){EndRecording();return;}recordingKey=true;RefreshSuppressButtons();bindKey.Focus();};
        clearKey.Click+=(_,_)=>{c.SetWakeShortcut(null);EndRecording();};
        var suppressPanel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Background=Brushes.Transparent,Margin=new Thickness(0)};
        suppressPanel.Children.Add(bindKey);suppressPanel.Children.Add(clearKey);
        w.PreviewKeyDown+=(_,a)=>
        {
            if(!recordingKey)return;
            a.Handled=true;
            var key=a.Key==Key.System?a.SystemKey:a.Key;
            if(key==Key.Escape){EndRecording();return;}
            if(a.IsRepeat)return;
            int vk=KeyInterop.VirtualKeyFromKey(key);
            if(held.Count==0)recordingError=null;
            held.Add(vk);
            var keys=held.Select(WakeShortcut.Normalize).Distinct().ToArray();
            if(recordingError==null)
            {
                if(keys.Length>WakeShortcut.MaxKeys)recordingError="最多支持四键，请重试";
                else if(keys.Any(member=>!WakeShortcut.CanRecord(member)))recordingError="不支持此键，请重试";
                else bindKey.Content=string.Join(" + ",keys.Select(Native.KeyName));
            }
            if(recordingError!=null)RefreshSuppressButtons();
        };
        w.PreviewKeyUp+=(_,a)=>
        {
            if(!recordingKey)return;
            a.Handled=true;
            var key=a.Key==Key.System?a.SystemKey:a.Key;
            int vk=KeyInterop.VirtualKeyFromKey(key);
            if(!held.Contains(vk))return;
            var shortcut=recordingError==null?WakeShortcut.Create(held):null;
            held.Remove(vk);
            if(recordingError!=null)return;
            if(shortcut==null)recordingError="请搭配其他键使用";
            else if(shortcut.UsesDrawerHotkey)recordingError="此组合键用于打开抽屉";
            else if(c.SetWakeShortcut(shortcut)){EndRecording();return;}
            else recordingError="绑定未保存，请重试";
            RefreshSuppressButtons();
        };
        w.Deactivated+=(_,_)=>{if(recordingKey)EndRecording();};
        var openModeRow=Row("抽屉图标打开方式",openMode);
        var suppressRow=Row("按键切换 Dock 唤出",suppressPanel);
        System.Windows.Automation.AutomationProperties.SetName(bindKey,"按键切换 Dock 唤出");
        Theme.Tip(bindKey,"同时按住一至四键，松开即保存。Esc 取消。修饰键可参与组合，Pause/Break、输入法键及特殊系统键不可绑定。");
        System.Windows.Automation.AutomationProperties.SetName(clearKey,"清除屏蔽唤醒按键");
        Group("常规",Row("开机自启动",startup),Row("减少动画效果",motion),openModeRow,suppressRow);

        string[] themes={"system","light","dark"};
        var appearance=Choice(new[]{"跟随 Windows","浅色","深色"},Math.Max(0,Array.IndexOf(themes,data.Appearance)));
        appearance.SelectionChanged+=(_,_)=>{if(correcting||appearance.SelectedIndex<0)return;if(!c.SetAppearance(themes[appearance.SelectedIndex])){correcting=true;appearance.SelectedIndex=Array.IndexOf(themes,data.Appearance);correcting=false;}};
        var monitors=Forms.Screen.AllScreens;
        var monitorIds=new[]{""}.Concat(monitors.Select(m=>m.DeviceName)).ToList();
        var labels=new[]{"自动选择"}.Concat(monitors.Select((m,i)=>$"{(m.Primary?"主显示器":$"显示器 {i+1}")} · {m.Bounds.Width}×{m.Bounds.Height}")).ToList();
        if(!monitorIds.Contains(data.Monitor)){monitorIds.Add(data.Monitor);labels.Add("先前选择的显示器（未连接）");}
        var display=Choice(labels,monitorIds.IndexOf(data.Monitor));
        display.SelectionChanged+=(_,_)=>{if(correcting||display.SelectedIndex<0)return;if(!c.SetMonitorPreference(monitorIds[display.SelectedIndex])){correcting=true;display.SelectedIndex=monitorIds.IndexOf(data.Monitor);correcting=false;}};
        Group("外观与显示",Row("外观模式",appearance),Row("显示器",display));

        ComboBox Numbers(int min,int max,int saved)=>Choice(new[]{"自动"}.Concat(Enumerable.Range(min,max-min+1).Select(n=>n.ToString())),saved<min?0:saved-min+1);
        var capacity=Numbers(4,18,data.DockCapacity);
        var rows=Numbers(2,8,data.DrawerRows);
        var columns=Numbers(3,12,data.DrawerColumns);
        int Number(ComboBox box)=>int.TryParse(box.SelectedItem as string,out var n)?n:0;
        (int,int,int) Draft()=>(Number(capacity),Number(rows),Number(columns));
        (int,int,int) Saved()=>(data.DockCapacity,data.DrawerRows,data.DrawerColumns);
        var errorText=Theme.Text("",12,true);
        errorText.TextWrapping=TextWrapping.Wrap;
        errorText.Visibility=Visibility.Collapsed;
        bool ApplyLayout()
        {
            bool ok=Draft()==Saved()||c.SetLayoutPreference(Draft());
            errorText.Text=ok?"":"布局未保存，正在使用上次保存的布局。";
            errorText.Visibility=ok?Visibility.Collapsed:Visibility.Visible;
            return ok;
        }
        var reset=ButtonFor("重置",()=>{capacity.SelectedIndex=rows.SelectedIndex=columns.SelectedIndex=0;});
        var save=ButtonFor("保存布局",()=>ApplyLayout());
        void SetLayoutButtons(){save.IsEnabled=Draft()!=Saved();reset.IsEnabled=Draft()!=(0,0,0);}
        foreach(var box in new[]{capacity,rows,columns})box.SelectionChanged+=(_,_)=>SetLayoutButtons();
        save.Click+=(_,_)=>SetLayoutButtons();SetLayoutButtons();
        var layoutActions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,5,0,0)};
        reset.Margin=new Thickness(0,0,7,0);layoutActions.Children.Add(reset);layoutActions.Children.Add(save);
        Group("布局",Row("Dock 显示数量",capacity),Row("抽屉可见行数",rows),Row("抽屉列数",columns),layoutActions,errorText);
        w.Closing+=(_,eventArgs)=>
        {
            if(Draft()==Saved())return;
            var answer=MessageBox.Show(w,"布局尚未保存。选择「是」保存，「否」放弃，或「取消」继续编辑。","未保存的布局",MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
            eventArgs.Cancel=answer==MessageBoxResult.Cancel||answer==MessageBoxResult.Yes&&!ApplyLayout();
        };

        var export=ButtonFor("导出",()=>
        {
            var dialog=new SaveFileDialog{Title="导出 MacDock 诊断",Filter="ZIP 文件|*.zip",FileName=$"MacDock-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",OverwritePrompt=false};
            if(dialog.ShowDialog(w)==true)c.Safe(()=>{DiagnosticsLog.Export(dialog.FileName);c.Notify("诊断包已导出。",false);});
        });
        var diagnosticRows=new List<UIElement>{Row("本地诊断日志",ButtonFor("打开位置",()=>c.Safe(DiagnosticsLog.OpenFolder))),Row("诊断包",export)};
        if(data.Entries.Any(e=>e.Source is "legacy" or "retired-auto"))
        {
            var restore=ButtonFor("选择入口…",()=>{});
            restore.Click+=(_,_)=>{var choices=c.RecoveryMenu();if(choices==null)return;var menu=new ContextMenu{PlacementTarget=restore};foreach(var item in choices.Items.Cast<object>().ToArray()){choices.Items.Remove(item);menu.Items.Add(item);}restore.ContextMenu=menu;menu.IsOpen=true;};
            diagnosticRows.Add(Row("恢复旧版手动入口",restore));
        }
        Group("诊断与恢复",diagnosticRows.ToArray());

        var update=ButtonFor("检查更新",()=>_ = c.CheckUpdates(true,anchor:w));
        var updateText=Theme.Text("",12,true);
        updateText.TextWrapping=TextWrapping.Wrap;
        void RefreshUpdate()
        {
            var status=c.Updates;
            update.Content=c.UpdateProgress??(status.Version==null?"检查更新":"立即更新");
            var last=status.Checked?.ToLocalTime().ToString("g")??"尚未检查";
            var note=status.Notes is string details&&details.Length>260?details[..260]+"…":status.Notes;
            updateText.Text=(status.Version==null?"":(status.IsRevision?$"发现 v{status.Version} 修订更新":"发现新版本 · v"+status.Version)+"\n"+note+"\n")+
                (c.UpdateProgress??c.UpdateProblem??(status.Version==null&&status.Checked!=null?"当前已是最新正式版本。":""))+"\n上次成功检查："+last;
        }
        RefreshUpdate();
        c.UpdateStatusChanged+=RefreshUpdate;
        w.Closed+=(_,_)=>c.UpdateStatusChanged-=RefreshUpdate;
        Group("关于与更新",Row("MacDock "+ReleaseUpdates.CurrentVersion,update),Row("自动检查更新",Toggle(data.AutoCheckUpdates,c.SetAutoUpdates)),updateText);

        var frame=new DockPanel();
        var title=new DockPanel{Margin=new Thickness(20,12,20,0)};
        var close=ButtonFor("关闭",w.Close);
        DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Theme.Text("设置",20,false,FontWeights.SemiBold));
        DockPanel.SetDock(title,Dock.Top);frame.Children.Add(title);
        frame.Children.Add(new SmoothScrollViewer{Content=body,Style=Theme.Style("Overlay"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var root=new Grid();root.Children.Add(frame);root.Children.Add(Theme.Frame(w.MaterialRadius));
        w.SetContentSurface(Theme.Surface(root,w.MaterialRadius));
        title.MouseLeftButtonDown+=(_,a)=>{if(a.LeftButton==MouseButtonState.Pressed)try{w.DragMove();}catch(InvalidOperationException){}};
        return w;
    }

    internal static Window Update(Controller c,Window? anchor)
    {
        var w=WindowShell("MacDock 更新",368,224,false);
        if(anchor?.IsVisible==true)w.Owner=anchor;
        w.WindowStartupLocation=WindowStartupLocation.CenterScreen;
        w.Topmost=false;
        var body=new DockPanel{Margin=new Thickness(20,16,20,16),LastChildFill=true};
        var title=Theme.Text("软件更新",17,false,FontWeights.SemiBold);
        DockPanel.SetDock(title,Dock.Top);body.Children.Add(title);
        var version=Theme.Text("MacDock v"+ReleaseUpdates.CurrentVersion,12,true);
        version.Margin=new Thickness(0,4,0,13);
        DockPanel.SetDock(version,Dock.Top);body.Children.Add(version);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var close=new Button{Style=Theme.Style("Pill"),Content="关闭",MinWidth=68};
        close.Click+=(_,_)=>w.Close();
        var action=new Button{Style=Theme.Style("Pill"),MinWidth=90,Margin=new Thickness(8,0,0,0)};
        action.Click+=async (_,_)=>{if(c.UpdateProblem!=null)await c.CheckUpdates(true,anchor:anchor);else await c.InstallUpdate();};
        buttons.Children.Add(close);buttons.Children.Add(action);
        DockPanel.SetDock(buttons,Dock.Bottom);body.Children.Add(buttons);
        var message=Theme.Text("",13,false,FontWeights.SemiBold);
        message.TextWrapping=TextWrapping.Wrap;
        DockPanel.SetDock(message,Dock.Top);body.Children.Add(message);
        var detail=Theme.Text("",12,true);
        detail.TextWrapping=TextWrapping.Wrap;detail.Margin=new Thickness(0,6,6,0);
        var scroll=new ScrollViewer{Content=detail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(scroll);
        void Refresh()
        {
            var progress=c.UpdateProgress;
            var error=c.UpdateProblem;
            var result=c.Updates;
            message.Text=progress??(error!=null?"更新检查未完成":result.Version is string next?(result.IsRevision?$"发现 v{next} 修订更新":$"发现新版本 · v{next}"):"当前已是最新版本");
            bool install=c.CanInstallUpdate;
            detail.Text=progress!=null?"":error??(result.Version==null?"你正在使用最新的正式版本。":result.Notes+"\n\n"+(install?"确认后下载并验证安装包，再打开升级向导。":"便携版可从发布页下载并替换。"));
            action.Content=error!=null?"重试":install?"下载并更新":"打开发​​布页".Replace("​","");
            action.Visibility=progress==null&&(error!=null||result.Version!=null)?Visibility.Visible:Visibility.Collapsed;
        }
        Refresh();c.UpdateStatusChanged+=Refresh;
        w.Closed+=(_,_)=>c.UpdateStatusChanged-=Refresh;
        w.Loaded+=(_,_)=>close.Focus();
        var root=new Grid();root.Children.Add(body);root.Children.Add(Theme.Frame(w.MaterialRadius));
        w.SetContentSurface(Theme.Surface(root,w.MaterialRadius));
        return w;
    }

    static string? Prompt(Controller c,string title,string hint,string value,int selectLength)
    {
        var w = WindowShell(title, 372, 176, true);
        if(c.Drawer is { IsVisible:true }) w.Owner=c.Drawer;
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        panel.Children.Add(Theme.Text(title, 15, false, FontWeights.SemiBold));
        var sub = Theme.Text(hint, 11, true); sub.Margin = new Thickness(0, 4, 0, 12);
        panel.Children.Add(sub);
        var input = new TextBox { Style = Theme.Style("Field"), Text = value, Height = 32 };
        panel.Children.Add(input);
        string? result = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Style = Theme.Style("Pill"), Content = "取消", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        cancel.Click += (_, _) => w.Close();
        var save = new Button { Style = Theme.Style("Pill"), Content = "确定", IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        void Commit() { if(string.IsNullOrWhiteSpace(input.Text)) return; result=input.Text.Trim(); w.Close(); }
        save.Click += (_, _) => Commit();
        input.KeyDown += (_, a) => { if(a.Key==Key.Enter) { Commit(); a.Handled=true; } };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        panel.Children.Add(buttons);
        var root = new Grid();
        root.Children.Add(panel);
        root.Children.Add(Theme.Frame(w.MaterialRadius));
        w.SetContentSurface(Theme.Surface(root,w.MaterialRadius));
        w.MouseLeftButtonDown += (_, a) => { if (a.ButtonState == MouseButtonState.Pressed && a.OriginalSource is not TextBox) { try { w.DragMove(); } catch { } } };
        w.Loaded += (_, _) => { input.Focus(); input.Select(0, Math.Clamp(selectLength,0,input.Text.Length)); };
        w.ShowDialog();
        return result;
    }

    public static void RenameLabel(Controller c, Entry entry)
    {
        var name = Prompt(c, "重命名显示名称", "只改变抽屉中显示的名称，原文件不受影响", entry.Name, entry.Name.Length);
        if(name==null) return;
        entry.Name = name; entry.AutoName = false; c.Changed();
    }

    // Renames the real file through the shell; the extension stays selected-out, like Explorer.
    public static string? RenameFile(Controller c, Entry entry)
    {
        var current = Path.GetFileName(entry.Path);
        var extension = Path.GetExtension(current);
        var stem = extension.Length > 0 && extension.Length < current.Length ? current[..^extension.Length] : current;
        var name = Prompt(c, "重命名", "将重命名桌面上的真实文件", current, stem.Length);
        if(name==null || name==current) return null;
        return name;
    }
}
