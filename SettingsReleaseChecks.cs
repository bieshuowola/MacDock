using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Text.Json;

namespace MacDock;

internal static class SettingsReleaseChecks
{
    sealed class Feed(byte[] manifest,byte[] signature,string version):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;
            byte[] bytes=uri.IdnHost=="api.github.com"?JsonSerializer.SerializeToUtf8Bytes(new{tag_name=$"v{version}",draft=false,prerelease=false}):
                uri.AbsolutePath.EndsWith(".sig")?signature:manifest;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }
    }
    static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach(var value in Walk(child))yield return value;
    }

    internal static async Task Run(Controller c)
    {
        string output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var checks=new List<object>();
        void Check(string name,bool pass)=>checks.Add(new{name,pass});
        try
        {
            c.ShowTrayMenu();
            await Task.Delay(180);
            var tray=c.TestTrayMenu!;
            Check("real tray popup has only the three product actions",tray.IsOpen&&tray.Items.OfType<MenuItem>().Select(m=>m.Header as string).SequenceEqual(new[]{"设置","检查更新","退出 MacDock"}));
            tray.IsOpen=false;

            c.ToggleDrawer();
            await Task.Delay(450);
            await c.ShowSettings(async w=>
            {
                try
                {
                    await Task.Delay(250);
                    var nodes=Walk(w).ToArray();
                    T Named<T>(string name) where T:FrameworkElement=>nodes.OfType<T>().Single(n=>System.Windows.Automation.AutomationProperties.GetName(n)==name);
                    Button Button(string name)=>nodes.OfType<Button>().Single(b=>b.Content as string==name);
                    void Press(ButtonBase b)=>b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,b));
                    Check("opening settings keeps the existing drawer and input enabled",
                        c.Drawer is {IsVisible:true,IsEnabled:true}&&c.Dock.IsEnabled&&w.IsActive);
                    var gateway=c.Dock.GatewayCenter;
                    Native.MovePointer(gateway.X,gateway.Y);
                    Native.TestButton(true);
                    await Task.Delay(35);
                    Native.TestButton(false);
                    await Task.Delay(550);
                    Check("Dock button closes the drawer while settings stays open",c.Drawer?.IsVisible==false&&w.IsVisible);
                    c.ToggleDrawer();await Task.Delay(500);
                    w.Activate();
                    await Task.Delay(170);
                    Check("activating settings keeps the open drawer available",c.Drawer?.IsVisible==true&&w.IsActive);
                    Native.MovePointer(c.WorkArea.Left+20,c.WorkArea.Top+100);
                    Native.TestButton(true);
                    await Task.Delay(35);
                    Native.TestButton(false);
                    await Task.Delay(550);
                    Check("outside click dismisses the drawer while settings stays usable",c.Drawer?.IsVisible==false&&w.IsVisible&&w.IsEnabled);
                    w.Activate();
                    var column=Named<ComboBox>("抽屉列数");
                    var save=Button("保存布局");
                    int original=c.Store.Data.DrawerColumns;
                    Check("save starts disabled",!save.IsEnabled);
                    int changed=original==6?7:6;
                    var profile=File.ReadAllBytes(c.Store.FilePath);
                    column.SelectedItem=changed.ToString();
                    Check("layout draft does not alter runtime or disk",save.IsEnabled&&c.Store.Data.DrawerColumns==original&&profile.SequenceEqual(File.ReadAllBytes(c.Store.FilePath)));
                    using(var blocked=new FileStream(c.Store.FilePath+".settings-tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                    {
                        Press(save);
                        Check("write failure preserves draft and previous config",save.IsEnabled&&c.Store.Data.DrawerColumns==original&&profile.SequenceEqual(File.ReadAllBytes(c.Store.FilePath)));
                    }
                    Press(save);
                    Check("save applies and persists draft",!save.IsEnabled&&c.Store.Data.DrawerColumns==changed&&new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.DrawerColumns==changed);
                    Press(Button("重置"));
                    Check("reset is a draft only",save.IsEnabled&&c.Store.Data.DrawerColumns==changed&&column.SelectedIndex==0);
                    Press(save);
                    Check("reset saved as automatic",c.Store.Data.DrawerColumns==0&&!save.IsEnabled);
                    var automatic=Named<CheckBox>("自动检查更新");
                    automatic.IsChecked=false;Press(automatic);
                    Check("auto checks disable immediately and persist",!new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.AutoCheckUpdates);
                    automatic.IsChecked=true;Press(automatic);
                    Check("auto checks re-enable and persist",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.AutoCheckUpdates);
                    var appearance=Named<ComboBox>("外观模式");
                    appearance.SelectedIndex=1;
                    Check("appearance persists immediately",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.Appearance=="light");
                    var openMode=Named<ComboBox>("抽屉图标打开方式");
                    Check("open mode defaults to single click",openMode.SelectedIndex==0&&!c.Store.Data.DoubleClickToOpen);
                    openMode.SelectedIndex=1;
                    Check("open mode double-click persists immediately",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.DoubleClickToOpen);
                    openMode.SelectedIndex=0;
                    Check("open mode single-click restores immediately",!new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.DoubleClickToOpen);
                    var bindKey=Named<Button>("临时关闭 MacDock");
                    var clearKey=Named<Button>("清除临时关闭快捷键");
                    Check("temporary disable description visible",nodes.OfType<TextBlock>().Any(t=>t.Text=="设置一个快捷键，按下后临时关闭 MacDock 的自动弹出功能，再次按下即可恢复。MacDock 仍在后台运行，底部唤醒小横条保持可用。"));
                    Check("suppress wake key starts unbound",!clearKey.IsEnabled&&c.Store.Data.SuppressWakeKey==0&&(string?)bindKey.Content=="未设置快捷键"&&!c.WakeRecording);
                    Press(bindKey);
                    Check("suppress wake key enters recording prompt",(bindKey.Content as string)?.Contains("请按")==true&&c.WakeRecording);
                    if(PresentationSource.FromVisual(w) is PresentationSource ps)
                    {
                        void Record(System.Windows.Input.Key key,bool down=true)=>w.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                            System.Windows.Input.Keyboard.PrimaryDevice,ps,0,key){RoutedEvent=down?
                                System.Windows.Input.Keyboard.PreviewKeyDownEvent:System.Windows.Input.Keyboard.PreviewKeyUpEvent});
                        Record(System.Windows.Input.Key.Pause);Record(System.Windows.Input.Key.Pause,false);
                        Check("Pause is rejected without setting a binding",c.Store.Data.SuppressWakeKey==0&&(string?)bindKey.Content=="不支持此键，请重试");
                        Record(System.Windows.Input.Key.F8);Record(System.Windows.Input.Key.F8,false);
                        Check("suppress wake key records and persists",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.SuppressWakeKey==0x77&&clearKey.IsEnabled&&!c.WakeRecording);
                        Press(bindKey);
                        var keys=new[]{System.Windows.Input.Key.RightCtrl,System.Windows.Input.Key.LeftAlt,System.Windows.Input.Key.LeftShift,System.Windows.Input.Key.F9};
                        foreach(var key in keys)Record(key);
                        Record(System.Windows.Input.Key.A);
                        Record(System.Windows.Input.Key.A,false);
                        foreach(var key in keys)Record(key,false);
                        Check("five keys leave the existing shortcut intact",c.Store.Data.SuppressWakeKey==0x77&&
                            (string?)bindKey.Content=="最多支持四键，请重试");
                        foreach(var key in keys)Record(key);
                        foreach(var key in keys.Reverse())Record(key,false);
                        Check("four keys replace the shortcut and persist",
                            new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.WakeShortcutKeys?.SequenceEqual(new[]{0x11,0x12,0x10,0x78})==true&&
                            (string?)bindKey.Content=="Ctrl + Alt + Shift + F9");
                        Press(bindKey);
                        Check("recording isolates active hook while capturing Ctrl+Alt+Space",c.WakeRecording);
                        var chord=new[]{System.Windows.Input.Key.LeftCtrl,System.Windows.Input.Key.LeftAlt,System.Windows.Input.Key.Space};
                        foreach(var key in chord)Record(key);
                        foreach(var key in chord.Reverse())Record(key,false);
                        Check("Ctrl+Alt+Space binds without opening drawer or toggling wake",
                            new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.WakeShortcutKeys?.SequenceEqual(new[]{0x11,0x12,0x20})==true&&
                            (string?)bindKey.Content=="Ctrl + Alt + Space"&&!c.WakeRecording&&c.Drawer?.IsVisible!=true&&!c.WakeSuppressedByKey);
                        void AssertSettingsLayout(string stateTag)
                        {
                            w.UpdateLayout();
                            double RightEdge(FrameworkElement el)=>el.TranslatePoint(new Point(el.ActualWidth,0),w).X;
                            double LeftEdge(FrameworkElement el)=>el.TranslatePoint(new Point(0,0),w).X;
                            double TopEdge(FrameworkElement el)=>el.TranslatePoint(new Point(0,0),w).Y;
                            double BottomEdge(FrameworkElement el)=>el.TranslatePoint(new Point(0,el.ActualHeight),w).Y;
                            var startupToggle=Named<CheckBox>("开机自启动");
                            var motionToggle=Named<CheckBox>("减少动画效果");
                            var suppressLabel=nodes.OfType<TextBlock>().Single(t=>t.Text=="临时关闭 MacDock");
                            var suppressDesc=nodes.OfType<TextBlock>().Single(t=>t.Text.StartsWith("设置一个快捷键，按下后临时关闭"));
                            double refRight=RightEdge(openMode);
                            Check($"[{stateTag}] right edges align (ref={refRight:F2} startup={RightEdge(startupToggle):F2} motion={RightEdge(motionToggle):F2} clear={RightEdge(clearKey):F2} app={RightEdge(appearance):F2} save={RightEdge(save):F2})",
                                Math.Abs(RightEdge(startupToggle)-refRight)<=1.0&&
                                Math.Abs(RightEdge(motionToggle)-refRight)<=1.0&&
                                Math.Abs(RightEdge(clearKey)-refRight)<=1.0&&
                                Math.Abs(RightEdge(appearance)-refRight)<=1.0&&
                                Math.Abs(RightEdge(save)-refRight)<=1.0);
                            Check($"[{stateTag}] control heights consistent at 30 DIP",
                                Math.Abs(bindKey.ActualHeight-30)<=1.0&&
                                Math.Abs(clearKey.ActualHeight-30)<=1.0&&
                                Math.Abs(openMode.ActualHeight-30)<=1.0);
                            double gap=LeftEdge(clearKey)-RightEdge(bindKey);
                            double labelGap=LeftEdge(bindKey)-RightEdge(suppressLabel);
                            double descGap=TopEdge(suppressDesc)-BottomEdge(bindKey);
                            Check($"[{stateTag}] hotkey buttons gap and zero label/desc overlap (btnGap={gap:F2} labelGap={labelGap:F2} descGap={descGap:F2})",
                                gap>=4.5&&gap<=7.5&&labelGap>=8.0&&descGap>=4.0);
                        }
                        AssertSettingsLayout("bound-light-458");
                        using(var alignedBmp=new System.Drawing.Bitmap((int)Math.Ceiling(w.ActualWidth*c.Scale),(int)Math.Ceiling(w.ActualHeight*c.Scale)))
                        {
                            var pt=w.PointToScreen(new Point());
                            using(var graphics=System.Drawing.Graphics.FromImage(alignedBmp))graphics.CopyFromScreen((int)pt.X,(int)pt.Y,0,0,alignedBmp.Size);
                            alignedBmp.Save(Path.Combine(output,"settings-light-aligned.png"));
                        }
                        double origW=w.Width;
                        w.Width=400;
                        AssertSettingsLayout("bound-light-400");
                        w.Width=origW;
                        w.UpdateLayout();
                        Press(clearKey);
                        var cleared=new Store(Path.GetDirectoryName(c.Store.FilePath)).Data;
                        Check("suppress wake key clears and persists",cleared.SuppressWakeKey==0&&cleared.WakeShortcutKeys==null&&!clearKey.IsEnabled&&(string?)bindKey.Content=="未设置快捷键");
                        AssertSettingsLayout("unbound-light-458");
                    }
                    using var bitmap=new System.Drawing.Bitmap((int)Math.Ceiling(w.ActualWidth*c.Scale),(int)Math.Ceiling(w.ActualHeight*c.Scale));
                    var point=w.PointToScreen(new Point());
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen((int)point.X,(int)point.Y,0,0,bitmap.Size);
                    bitmap.Save(Path.Combine(output,"settings-release.png"));
                    using var signingKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    var current=Version.Parse(ReleaseUpdates.CurrentVersion);
                    string next=new Version(current.Major,current.Minor,current.Build+1).ToString(3);
                    byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new ReleaseUpdates.Manifest(next,$"MacDock-{next}-win-x64-Setup.exe",1,new string('a',64),"测试版更新说明",BuildSequence:long.MaxValue,BuildFingerprint:Guid.NewGuid().ToString("N"),PayloadSha256:new string('b',64)));
                    byte[] signature=signingKey.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                    using var http=new HttpClient(new Feed(manifest,signature,next));
                    await c.CheckUpdates(false,new ReleaseUpdates("fixture/MacDock",signingKey.ExportSubjectPublicKeyInfo(),http));
                    Check("signed update changes both settings and tray state",Button("立即更新").IsEnabled&&c.UpdateMenuLabel==$"发现新版本 · v{next}"&&c.Updates.Notes=="测试版更新说明");
                    var persisted=JsonSerializer.Deserialize<Controller.UpdateStatus>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(c.Store.FilePath)!,"update-status.json")));
                    Check("successful detection persists a usable check time",persisted?.Checked!=null&&persisted.Version==next);
                }
                catch(Exception error){checks.Add(new{name="settings exception",pass=false,detail=error.ToString()});}
                finally{w.Close();}
            });

            var now=DateTimeOffset.UtcNow;
            Check("fresh and due checks start after startup delay",Controller.TimeUntilUpdateCheck(now,null)==TimeSpan.Zero&&Controller.TimeUntilUpdateCheck(now,now.AddHours(-25))==TimeSpan.Zero);
            Check("a recent success survives restart scheduling",Controller.TimeUntilUpdateCheck(now,now.AddHours(-2))==TimeSpan.FromHours(22));
            DiagnosticsLog.Error(new IOException("DO-NOT-EXPORT-CREDENTIAL"),"diagnostic.fixture");
            await Task.Delay(180);
            string package=Path.Combine(output,"test-diagnostics.zip");
            DiagnosticsLog.Export(package);
            using var zip=ZipFile.OpenRead(package);
            using var log=new StreamReader(zip.GetEntry("events.jsonl")!.Open());
            string contents=log.ReadToEnd();
            Check("export contains diagnostic category and excludes exception message",contents.Contains("diagnostic.fixture")&&!contents.Contains("DO-NOT-EXPORT-CREDENTIAL")&&zip.GetEntry("environment.json")!=null);
        }
        catch(Exception error){checks.Add(new{name="exception",pass=false,detail=error.ToString()});}
        finally
        {
            File.WriteAllText(Path.Combine(output,"settings-release.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
