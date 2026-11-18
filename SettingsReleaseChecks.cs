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

            c.ShowSettings(async w=>
            {
                try
                {
                    await Task.Delay(250);
                    var nodes=Walk(w).ToArray();
                    T Named<T>(string name) where T:FrameworkElement=>nodes.OfType<T>().Single(n=>System.Windows.Automation.AutomationProperties.GetName(n)==name);
                    Button Button(string name)=>nodes.OfType<Button>().Single(b=>b.Content as string==name);
                    void Press(ButtonBase b)=>b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,b));
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
                    var bindKey=Named<Button>("按住屏蔽 Dock 唤出按键");
                    var clearKey=Named<Button>("清除屏蔽唤醒按键");
                    Check("suppress wake key starts unbound",!clearKey.IsEnabled&&c.Store.Data.SuppressWakeKey==0);
                    Press(bindKey);
                    Check("suppress wake key enters recording prompt",(bindKey.Content as string)?.Contains("请按")==true);
                    if(PresentationSource.FromVisual(w) is PresentationSource ps)
                    {
                        w.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,ps,0,System.Windows.Input.Key.F8){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
                        Check("suppress wake key records and persists",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.SuppressWakeKey==0x77&&clearKey.IsEnabled);
                        Press(clearKey);
                        Check("suppress wake key clears and persists",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.SuppressWakeKey==0&&!clearKey.IsEnabled);
                    }
                    using var bitmap=new System.Drawing.Bitmap((int)Math.Ceiling(w.ActualWidth*c.Scale),(int)Math.Ceiling(w.ActualHeight*c.Scale));
                    var point=w.PointToScreen(new Point());
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen((int)point.X,(int)point.Y,0,0,bitmap.Size);
                    bitmap.Save(Path.Combine(output,"settings-release.png"));
                    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    var current=Version.Parse(ReleaseUpdates.CurrentVersion);
                    string next=new Version(current.Major,current.Minor,current.Build+1).ToString(3);
                    byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new ReleaseUpdates.Manifest(next,$"MacDock-{next}-win-x64-Setup.exe",1,new string('a',64),"测试版更新说明",BuildSequence:long.MaxValue,BuildFingerprint:Guid.NewGuid().ToString("N"),PayloadSha256:new string('b',64)));
                    byte[] signature=key.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                    using var http=new HttpClient(new Feed(manifest,signature,next));
                    await c.CheckUpdates(false,new ReleaseUpdates("fixture/MacDock",key.ExportSubjectPublicKeyInfo(),http));
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
