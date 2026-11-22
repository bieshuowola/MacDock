using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacDock;

internal static class DrawerDismissChecks
{
    [DllImport("user32.dll")] static extern bool GetMenuItemRect(nint window, nint menu, uint index, out Native.Rect rect);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    static void ActivateForeignWindowForTest(nint hwnd)
    {
        var fg = Native.GetForegroundWindow();
        uint curTid = GetCurrentThreadId();
        uint fgTid = fg != 0 ? GetWindowThreadProcessId(fg, out _) : 0;
        bool attached = fgTid != 0 && fgTid != curTid && AttachThreadInput(curTid, fgTid, true);
        try
        {
            Native.ActivateWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(curTid, fgTid, false);
        }
    }

    static Thread StartNativeMenuClickDriver(nint menu, int pos, Action onClicked)
    {
        var t = new Thread(() =>
        {
            Thread.Sleep(180);
            Native.Rect itemRect = default;
            bool gotRect = false;
            if (pos >= 0)
            {
                for (int i = 0; i < 20 && ShellMenu.ActivePopup == menu; i++)
                {
                    if (GetMenuItemRect(0, menu, (uint)pos, out itemRect) && itemRect.Right > itemRect.Left && itemRect.Bottom > itemRect.Top)
                    {
                        gotRect = true;
                        break;
                    }
                    Thread.Sleep(40);
                }
            }
            if (gotRect)
            {
                Native.MovePointer((itemRect.Left + itemRect.Right) / 2, (itemRect.Top + itemRect.Bottom) / 2);
                Thread.Sleep(60);
                Native.TestButton(true);
                Thread.Sleep(35);
                Native.TestButton(false);
                onClicked();
            }
            else
            {
                Native.TestKey(0x1B, true);
                Thread.Sleep(30);
                Native.TestKey(0x1B, false);
            }
        }) { IsBackground = true };
        t.Start();
        return t;
    }

    static void CreateShortcut(string path, string target, string arguments)
    {
        object? shell = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            item = ((dynamic)shell).CreateShortcut(path);
            ((dynamic)item).TargetPath = target;
            ((dynamic)item).Arguments = arguments;
            ((dynamic)item).Save();
        }
        finally
        {
            if (item != null) Marshal.ReleaseComObject(item);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    static void CaptureScreen(System.Windows.Forms.Screen screen, string file)
    {
        using var b = new System.Drawing.Bitmap(screen.Bounds.Width, screen.Bounds.Height);
        using var g = System.Drawing.Graphics.FromImage(b);
        g.CopyFromScreen(screen.Bounds.Location, System.Drawing.Point.Empty, b.Size);
        b.Save(file);
    }

    static async Task<bool> WaitUntil(Func<bool> predicate, int timeoutMs = 6000, int stepMs = 40)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate()) return true;
            await Task.Delay(stepMs);
        }
        return predicate();
    }

    static async Task ClickAt(int x, int y, int holdMs = 30)
    {
        Native.MovePointer(x, y);
        await Task.Delay(45);
        Native.TestButton(true);
        await Task.Delay(holdMs);
        Native.TestButton(false);
        await Task.Delay(50);
    }

    static async Task RightClickAt(int x, int y, int holdMs = 30)
    {
        Native.MovePointer(x, y);
        await Task.Delay(45);
        Native.TestRightButton(true);
        await Task.Delay(holdMs);
        Native.TestRightButton(false);
        await Task.Delay(50);
    }

    static async Task PressKey(byte vk, params byte[] modifiers)
    {
        foreach (var m in modifiers) Native.TestKey(m, true);
        bool ext = vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5D;
        Native.TestKey(vk, true, ext);
        await Task.Delay(35);
        Native.TestKey(vk, false, ext);
        foreach (var m in modifiers.Reverse()) Native.TestKey(m, false);
        await Task.Delay(80);
    }

    static HashSet<nint> SnapshotDialogs() =>
        Native.VisibleTopLevelWindows()
            .Where(h => Native.Class(h) is "#32770" or "OperationStatusWindow")
            .ToHashSet();

    static nint FindNewDialog(HashSet<nint> before) =>
        Native.VisibleTopLevelWindows()
            .FirstOrDefault(h => !before.Contains(h) && Native.Class(h) is "#32770" or "OperationStatusWindow");

    static void SafeCloseDialogs(IEnumerable<nint> dialogs)
    {
        foreach (var h in dialogs)
        {
            if (h != 0 && Native.IsWindow(h)) Native.PostCloseWindow(h);
        }
    }

    static void CloseExplorerFoldersMatching(string titleFragment)
    {
        foreach (var h in Native.VisibleTopLevelWindows())
        {
            if (Native.Class(h) == "CabinetWClass" && Native.Title(h).Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
                Native.PostCloseWindow(h);
        }
    }

    internal static async Task Run(Controller c)
    {
        string output = Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS") ?? Path.Combine(AppContext.BaseDirectory, "test-results");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        var spawned = new List<Process>();
        var openedDialogs = new List<nint>();
        bool failed = false;

        void Check(string name, bool pass, object? detail = null)
        {
            results.Add(new { name, pass, detail });
            if (!pass) failed = true;
        }

        Native.GetCursorPos(out var originalCursor);
        var originalForeground = Native.GetForegroundWindow();
        PointerHook.NonInjectedInputs = 0;
        PointerHook.AuditInput = true;
        var traceLog = new List<string>();
        GestureChecks.Trace = (tag, data) =>
        {
            traceLog.Add($"[{DateTime.Now:HH:mm:ss.fff}] {tag} {(data == null ? "" : JsonSerializer.Serialize(data))}");
        };

        int sentinelClicks = 0;
        var sentinel = new Window
        {
            Title = "MacDock Dismiss Sentinel",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Left = c.WorkArea.Left / c.Scale,
            Top = c.WorkArea.Top / c.Scale,
            Width = c.WorkArea.Width / c.Scale,
            Height = c.WorkArea.Height / c.Scale,
            Background = new SolidColorBrush(Color.FromRgb(28, 44, 68))
        };
        sentinel.PreviewMouseDown += (_, _) => sentinelClicks++;
        sentinel.Show();
        var sentinelHandle = new WindowInteropHelper(sentinel).Handle;
        var outsidePt = new Native.Point(c.WorkArea.Left + 80, c.WorkArea.Top + 120);

        try
        {
            string fixtureRoot = Path.Combine(output, "drawer-dismiss-fixtures");
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true);
            Directory.CreateDirectory(fixtureRoot);
            string revealFolder = Path.Combine(fixtureRoot, "MacDockRevealProbeDir");
            Directory.CreateDirectory(revealFolder);

            string readmePath = Path.Combine(fixtureRoot, "readme.txt");
            string secondPath = Path.Combine(fixtureRoot, "second.txt");
            string manualPath = Path.Combine(fixtureRoot, "manual-item.txt");
            string appLnk = Path.Combine(fixtureRoot, "dismiss-app.lnk");
            string openLnk = Path.Combine(fixtureRoot, "open-app.lnk");
            File.WriteAllText(readmePath, "drawer dismiss test readme");
            File.WriteAllText(secondPath, "drawer dismiss test second");
            File.WriteAllText(manualPath, "drawer dismiss test manual");
            CreateShortcut(appLnk, Environment.ProcessPath!, "--close-fixture dismiss-app 1");
            CreateShortcut(openLnk, Environment.ProcessPath!, "--close-fixture open-app 1");

            c.Store.Data.Entries.Clear();
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Mode = "自定义";
            c.Store.Data.ListView = false;
            c.Store.Data.DoubleClickToOpen = true;
            c.Store.Data.ReducedMotion = false;

            var readme = new Entry { Path = readmePath, Name = "readme", Source = "desktop", Pinned = true };
            var second = new Entry { Path = secondPath, Name = "second", Source = "desktop", Pinned = false };
            var folderEntry = new Entry { Path = revealFolder, Name = "MacDockRevealProbeDir", Source = "desktop", Pinned = false };
            c.Store.Data.Entries.AddRange(new[] { readme, second, folderEntry });
            c.Store.Add(manualPath, pin: false, name: "ManualItem", source: "manual");
            c.Store.Add(appLnk, pin: true, name: "DismissApp", source: "manual");
            c.Store.Add(openLnk, pin: false, name: "OpenApp", source: "desktop");
            var manualEntry = c.Store.Data.Entries.Single(e => e.Path == manualPath);
            var appEntry = c.Store.Data.Entries.Single(e => e.Path == appLnk);
            var openEntry = c.Store.Data.Entries.Single(e => e.Path == openLnk);

            c.Changed();
            c.Expand();
            await WaitUntil(() => c.Dock.IsVisible && !c.DockMotionRunning);

            async Task OpenDrawerSettled()
            {
                if (c.Drawer?.IsVisible != true) c.ToggleDrawer();
                await WaitUntil(() => c.Drawer is { IsVisible: true, IsDeforming: false } && c.DrawerProgress == 1 && !c.DrawerMotionRunning);
                await Task.Delay(120);
            }

            // ------------------------------------------------------------------
            // 1. Native ShellMenu -> Properties (#32770) -> interact -> close props
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            var drawer = c.Drawer!;
            drawer.EnsureVisible(readme.Id);
            await Task.Delay(80);
            var readmePt = drawer.TileCenter(readme.Id);
            var beforeProps1 = SnapshotDialogs();

            bool clickedNativeProps = false;
            bool nativePropsMenuShown = false;
            Thread? nativeMenuDriver = null;
            ShellMenu.MenuDisplayed = () =>
            {
                nativePropsMenuShown = true;
                var menu = ShellMenu.ActivePopup;
                int propIdx = ShellMenu.LastVerbs.FindIndex(v => string.Equals(v, "properties", StringComparison.OrdinalIgnoreCase));
                if (propIdx < 0)
                    propIdx = ShellMenu.LastLabels.FindIndex(l => l.Contains("属性", StringComparison.Ordinal) || l.Contains("Properties", StringComparison.OrdinalIgnoreCase));
                int pos = propIdx >= 0 ? ShellMenu.LastPositions[propIdx] : -1;
                nativeMenuDriver = StartNativeMenuClickDriver(menu, pos, () => clickedNativeProps = true);
            };

            await RightClickAt(readmePt.X, readmePt.Y);
            await WaitUntil(() => nativePropsMenuShown && !c.MenuOpen, timeoutMs: 5000);
            nativeMenuDriver?.Join(2000);
            ShellMenu.MenuDisplayed = null;

            nint props1 = 0;
            await WaitUntil(() => (props1 = FindNewDialog(beforeProps1)) != 0, timeoutMs: 5000);
            if (props1 != 0) openedDialogs.Add(props1);
            await Task.Delay(260);

            Check("native ShellMenu Properties opens #32770 and drawer stays open",
                clickedNativeProps && props1 != 0 && drawer.IsVisible && !c.DrawerClosing && c.TrackedContextWindows.Contains(props1) && !Native.IsWindowAbove(drawer.Handle, props1),
                new { clickedNativeProps, props1 = (long)props1, drawerVisible = drawer.IsVisible, closing = c.DrawerClosing, tracked = c.TrackedContextWindows.Count, fg = Native.Class(Native.GetForegroundWindow()), aboveDrawer = !Native.IsWindowAbove(drawer.Handle, props1) });

            if (props1 != 0)
            {
                Native.GetWindowRect(props1, out var pr);
                var safePropClick = new Native.Point(pr.Left + Math.Min(120, Math.Max(40, (pr.Right - pr.Left) / 2)), pr.Top + 14);
                await ClickAt(safePropClick.X, safePropClick.Y);
                await Task.Delay(200);
                Check("clicking inside native #32770 Properties keeps drawer open",
                    drawer.IsVisible && !c.DrawerClosing && Native.IsWindow(props1),
                    new { drawerVisible = drawer.IsVisible, propsAlive = Native.IsWindow(props1) });

                CaptureScreen(c.Screen, Path.Combine(output, "drawer-dismiss-properties-native.png"));

                var secondPt = drawer.TileCenter(second.Id);
                await ClickAt(secondPt.X, secondPt.Y);
                await Task.Delay(180);
                Check("clicking drawer tile while Properties is open selects tile and keeps both alive",
                    drawer.IsVisible && !c.DrawerClosing && Native.IsWindow(props1) && drawer.Selection.Any(x => x.Id == second.Id),
                    new { drawerVisible = drawer.IsVisible, selected = drawer.Selection.Select(x => x.Name).ToArray() });

                Native.ActivateWindow(props1);
                await Task.Delay(120);
                await PressKey(0x1B);
                bool props1Closed = await WaitUntil(() => !Native.IsWindow(props1) || !Native.IsWindowVisible(props1), timeoutMs: 4000);
                await WaitUntil(() => Native.GetForegroundWindow() == drawer.Handle && c.TrackedContextWindows.Count == 0, timeoutMs: 3000);
                Check("closing native Properties keeps drawer open and restores drawer focus",
                    props1Closed && drawer.IsVisible && !c.DrawerClosing && Native.GetForegroundWindow() == drawer.Handle && c.TrackedContextWindows.Count == 0,
                    new { props1Closed, drawerVisible = drawer.IsVisible, fg = (long)Native.GetForegroundWindow(), drawer = (long)drawer.Handle, tracked = c.TrackedContextWindows.Count });
            }

            // ------------------------------------------------------------------
            // 2. WPF EntryMenu -> Properties -> outside click dismisses drawer
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            drawer.EnsureVisible(manualEntry.Id);
            await Task.Delay(80);
            var manualPt = drawer.TileCenter(manualEntry.Id);
            var manualTile = drawer.TileControlForPath(manualPath)!;
            var beforeProps2 = SnapshotDialogs();

            await RightClickAt(manualPt.X, manualPt.Y);
            await WaitUntil(() => c.MenuOpen && manualTile.ContextMenu?.IsOpen == true, timeoutMs: 4000);
            await Task.Delay(140);
            manualTile.ContextMenu!.UpdateLayout();
            var wpfMenu = manualTile.ContextMenu!;
            var propMenuItem = wpfMenu.Items.OfType<MenuItem>()
                .FirstOrDefault(i => string.Equals(AutomationProperties.GetName(i), "属性", StringComparison.Ordinal));
            if (propMenuItem != null)
                await WaitUntil(() => propMenuItem.ActualWidth > 10 && propMenuItem.IsLoaded, timeoutMs: 1500);
            Check("WPF EntryMenu opens for manual item with 属性", propMenuItem != null && propMenuItem.ActualWidth > 10,
                new { drawerVisible = drawer.IsVisible, c.MenuOpen, menuIsOpen = wpfMenu.IsOpen, itemCount = wpfMenu.Items.Count, width = propMenuItem?.ActualWidth ?? 0 });
            if (propMenuItem != null)
            {
                var itemCenter = propMenuItem.PointToScreen(new Point(propMenuItem.ActualWidth / 2, propMenuItem.ActualHeight / 2));
                await ClickAt((int)itemCenter.X, (int)itemCenter.Y);
            }

            nint props2 = 0;
            await WaitUntil(() => (props2 = FindNewDialog(beforeProps2)) != 0, timeoutMs: 5000);
            if (props2 != 0) openedDialogs.Add(props2);
            await Task.Delay(260);

            Check("WPF menu 属性 opens #32770 and drawer stays open",
                props2 != 0 && drawer.IsVisible && !c.DrawerClosing && !c.MenuOpen && c.TrackedContextWindows.Contains(props2) && !Native.IsWindowAbove(drawer.Handle, props2),
                new { props2 = (long)props2, drawerVisible = drawer.IsVisible, c.MenuOpen, tracked = c.TrackedContextWindows.Count, aboveDrawer = !Native.IsWindowAbove(drawer.Handle, props2) });

            CaptureScreen(c.Screen, Path.Combine(output, "drawer-dismiss-properties-wpf.png"));

            int clicksBefore = sentinelClicks;
            Check("outside test point hits sentinel", Native.WindowFromPoint(outsidePt) == sentinelHandle);
            await ClickAt(outsidePt.X, outsidePt.Y);
            bool dismissedWithProps = await WaitUntil(() => !drawer.IsVisible && !c.Dock.IsVisible, timeoutMs: 4000);
            Check("outside click while Properties is open closes drawer cleanly without pass-through",
                dismissedWithProps && sentinelClicks == clicksBefore && c.TrackedContextWindows.Count == 0,
                new { dismissedWithProps, clickDelta = sentinelClicks - clicksBefore, tracked = c.TrackedContextWindows.Count });
            SafeCloseDialogs(new[] { props2 });
            await WaitUntil(() => props2 == 0 || !Native.IsWindow(props2), timeoutMs: 3000);

            // ------------------------------------------------------------------
            // 3. Alt+Enter Properties + foreign window activation closes drawer
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            readmePt = drawer.TileCenter(readme.Id);
            await ClickAt(readmePt.X, readmePt.Y);
            await WaitUntil(() => drawer.Selection.Any(x => x.Id == readme.Id));
            var beforeProps3 = SnapshotDialogs();
            await PressKey(0x0D, 0x12); // Alt+Enter

            nint props3 = 0;
            await WaitUntil(() => (props3 = FindNewDialog(beforeProps3)) != 0, timeoutMs: 5000);
            if (props3 != 0) openedDialogs.Add(props3);
            await Task.Delay(220);
            Check("Alt+Enter opens #32770 and keeps drawer open",
                props3 != 0 && drawer.IsVisible && !c.DrawerClosing && c.TrackedContextWindows.Contains(props3),
                new { props3 = (long)props3, drawerVisible = drawer.IsVisible, closing = c.DrawerClosing, tracked = c.TrackedContextWindows.Count, selected = drawer.Selection.Select(x => x.Name).ToArray(), fg = Native.Class(Native.GetForegroundWindow()) });

            ActivateForeignWindowForTest(sentinelHandle);
            bool dismissedOnSwitch = await WaitUntil(() => !drawer.IsVisible && !c.Dock.IsVisible, timeoutMs: 4000);
            Check("activating foreign window while Properties is open closes drawer",
                dismissedOnSwitch && c.TrackedContextWindows.Count == 0,
                new { dismissedOnSwitch, tracked = c.TrackedContextWindows.Count, fg = (long)Native.GetForegroundWindow(), sentinel = (long)sentinelHandle });
            SafeCloseDialogs(new[] { props3 });
            await WaitUntil(() => props3 == 0 || !Native.IsWindow(props3), timeoutMs: 3000);

            // ------------------------------------------------------------------
            // 4. Rename dialog (Dialogs.Prompt) & Settings window interaction
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            manualPt = drawer.TileCenter(manualEntry.Id);
            await ClickAt(manualPt.X, manualPt.Y);
            await WaitUntil(() => drawer.Selection.Any(x => x.Id == manualEntry.Id));
            var renameTask = Task.Run(async () =>
            {
                await Task.Delay(120);
                Native.TestKey(0x71, true); // F2
                await Task.Delay(35);
                Native.TestKey(0x71, false);
            });
            await renameTask;

            Window? renameWindow = null;
            await WaitUntil(() =>
                (renameWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.Title.Contains("重命名"))) != null,
                timeoutMs: 4000);
            if (renameWindow != null)
            {
                var rwPt = renameWindow.PointToScreen(new Point(renameWindow.ActualWidth / 2, 20));
                await ClickAt((int)rwPt.X, (int)rwPt.Y);
                await Task.Delay(150);
                Check("clicking inside Rename dialog keeps drawer open", drawer.IsVisible && !c.DrawerClosing && renameWindow.IsVisible);
                await PressKey(0x1B);
                await WaitUntil(() => !renameWindow.IsVisible, timeoutMs: 3000);
            }
            else
            {
                Check("Rename dialog opened via F2", false);
            }
            await Task.Delay(180);
            Check("closing Rename dialog preserves open drawer and grid focus",
                drawer.IsVisible && !c.DrawerClosing && drawer.Scroller.IsKeyboardFocused,
                new { drawerVisible = drawer.IsVisible, focused = drawer.Scroller.IsKeyboardFocused });

            c.Settings();
            Window? settingsWin = null;
            await WaitUntil(() =>
                (settingsWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.Title == "MacDock 设置")) != null,
                timeoutMs: 4000);
            if (settingsWin != null)
            {
                var swPt = settingsWin.PointToScreen(new Point(80, 24));
                await ClickAt((int)swPt.X, (int)swPt.Y);
                await Task.Delay(150);
                Check("interacting with Settings window keeps drawer open", drawer.IsVisible && !c.DrawerClosing && settingsWin.IsVisible);
                settingsWin.Close();
                await WaitUntil(() => !settingsWin.IsVisible, timeoutMs: 3000);
                await Task.Delay(150);
                Check("closing Settings window keeps drawer open", drawer.IsVisible && !c.DrawerClosing);
            }
            else
            {
                Check("Settings window opened", false);
            }

            await ClickAt(outsidePt.X, outsidePt.Y);
            await WaitUntil(() => !drawer.IsVisible, timeoutMs: 3500);

            // ------------------------------------------------------------------
            // 5. WPF -> "Windows 更多操作…", blank click cancel vs outside cancel
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            manualTile = drawer.TileControlForPath(manualPath)!;
            manualPt = drawer.TileCenter(manualEntry.Id);
            await RightClickAt(manualPt.X, manualPt.Y);
            await WaitUntil(() => c.MenuOpen && manualTile.ContextMenu?.IsOpen == true, timeoutMs: 4000);
            await Task.Delay(140);
            manualTile.ContextMenu!.UpdateLayout();
            var moreItem = manualTile.ContextMenu!.Items.OfType<MenuItem>()
                .FirstOrDefault(i => string.Equals(AutomationProperties.GetName(i), "Windows 更多操作…", StringComparison.Ordinal));
            if (moreItem != null)
                await WaitUntil(() => moreItem.ActualWidth > 10 && moreItem.IsLoaded, timeoutMs: 1500);
            bool nativeFromWpfShown = false;
            Thread? escNativeThread = null;
            ShellMenu.MenuDisplayed = () =>
            {
                nativeFromWpfShown = true;
                escNativeThread = new Thread(() =>
                {
                    Thread.Sleep(160);
                    Native.TestKey(0x1B, true);
                    Thread.Sleep(35);
                    Native.TestKey(0x1B, false);
                }) { IsBackground = true };
                escNativeThread.Start();
            };
            if (moreItem != null)
            {
                var pt = moreItem.PointToScreen(new Point(moreItem.ActualWidth / 2, moreItem.ActualHeight / 2));
                await ClickAt((int)pt.X, (int)pt.Y);
            }
            await WaitUntil(() => nativeFromWpfShown && !c.MenuOpen, timeoutMs: 5000);
            escNativeThread?.Join(2000);
            ShellMenu.MenuDisplayed = null;
            await Task.Delay(150);
            Check("WPF menu -> Windows 更多操作… -> Esc keeps drawer open and resets MenuOpen",
                nativeFromWpfShown && !c.MenuOpen && drawer.IsVisible && !c.DrawerClosing,
                new { moreFound = moreItem != null, manualMenuOpen = manualTile.ContextMenu?.IsOpen, nativeFromWpfShown, c.MenuOpen, drawerVisible = drawer.IsVisible, closing = c.DrawerClosing });

            // Native menu cancelled by clicking inside Drawer blank area -> stays open
            readmePt = drawer.TileCenter(readme.Id);
            var drawerBlankPt = new Native.Point(drawer.BodyBounds.Right - 45, drawer.BodyBounds.Bottom - 80);
            bool blankCancelShown = false;
            Thread? blankCancelDriver = null;
            ShellMenu.MenuDisplayed = () =>
            {
                blankCancelShown = true;
                blankCancelDriver = new Thread(() =>
                {
                    Thread.Sleep(150);
                    Native.MovePointer(drawerBlankPt.X, drawerBlankPt.Y);
                    Thread.Sleep(40);
                    Native.TestButton(true);
                    Thread.Sleep(30);
                    Native.TestButton(false);
                }) { IsBackground = true };
                blankCancelDriver.Start();
            };
            await RightClickAt(readmePt.X, readmePt.Y);
            await WaitUntil(() => blankCancelShown && !c.MenuOpen, timeoutMs: 4000);
            blankCancelDriver?.Join(2000);
            ShellMenu.MenuDisplayed = null;
            await Task.Delay(180);
            Check("native menu cancelled inside Drawer blank keeps drawer open",
                blankCancelShown && !c.MenuOpen && drawer.IsVisible && !c.DrawerClosing);

            // Native menu cancelled by clicking outside on sentinel -> dismisses drawer
            bool outsideCancelShown = false;
            Thread? outsideCancelDriver = null;
            ShellMenu.MenuDisplayed = () =>
            {
                outsideCancelShown = true;
                outsideCancelDriver = new Thread(() =>
                {
                    Thread.Sleep(150);
                    Native.MovePointer(outsidePt.X, outsidePt.Y);
                    Thread.Sleep(40);
                    Native.TestButton(true);
                    Thread.Sleep(30);
                    Native.TestButton(false);
                }) { IsBackground = true };
                outsideCancelDriver.Start();
            };
            await RightClickAt(readmePt.X, readmePt.Y);
            await WaitUntil(() => outsideCancelShown && !c.MenuOpen, timeoutMs: 4000);
            outsideCancelDriver?.Join(2000);
            ShellMenu.MenuDisplayed = null;
            bool dismissedAfterOutsideMenuCancel = await WaitUntil(() => !drawer.IsVisible && !c.Dock.IsVisible, timeoutMs: 4000);
            Check("native menu cancelled by outside click dismisses drawer",
                outsideCancelShown && !c.MenuOpen && dismissedAfterOutsideMenuCancel);

            // ------------------------------------------------------------------
            // 6. CloseEntry keeps drawer open; RevealEntry & OpenEntries close drawer
            // ------------------------------------------------------------------
            var closeTargetProc = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--close-fixture dismiss-app 1") { UseShellExecute = false })!;
            spawned.Add(closeTargetProc);
            await WaitUntil(() => c.Dock.ClosableWindows(appEntry).Count == 1, timeoutMs: 6000);

            await OpenDrawerSettled();
            bool closedSent = c.CloseEntry(appEntry);
            bool fixtureExited = await WaitUntil(() => closeTargetProc.HasExited, timeoutMs: 5000);
            await Task.Delay(250);
            Check("CloseEntry from open drawer closes target app without dismissing drawer",
                closedSent && fixtureExited && drawer.IsVisible && !c.DrawerClosing,
                new { closedSent, fixtureExited, drawerVisible = drawer.IsVisible });

            c.RevealEntry(folderEntry);
            bool dismissedAfterReveal = await WaitUntil(() => !drawer.IsVisible, timeoutMs: 4000);
            Check("RevealEntry dismisses unpinned drawer", dismissedAfterReveal);
            await Task.Delay(300);
            CloseExplorerFoldersMatching("MacDockRevealProbeDir");

            c.Store.Data.DoubleClickToOpen = false;
            await OpenDrawerSettled();
            var openPt = drawer.TileCenter(openEntry.Id);
            await ClickAt(openPt.X, openPt.Y);
            bool dismissedAfterSingleClickOpen = await WaitUntil(() => !drawer.IsVisible, timeoutMs: 4000);
            Check("single-click Open launches and dismisses drawer", dismissedAfterSingleClickOpen);
            foreach (var h in c.Dock.ClosableWindows(openEntry)) Native.PostCloseWindow(h);
            await Task.Delay(200);

            // ------------------------------------------------------------------
            // 7. Pin lock, tiered Escape, and 5 rapid wake/open/dismiss cycles
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            drawer.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(120);
            await ClickAt(outsidePt.X, outsidePt.Y);
            await Task.Delay(200);
            Check("pinned drawer stays open on outside click", c.DrawerPinned && drawer.IsVisible && !c.DrawerClosing);

            drawer.FocusSearch();
            await Task.Delay(80);
            await PressKey(0x1B); // 1st Esc: exit search to grid
            Check("1st Esc from search keeps drawer open", drawer.IsVisible && !drawer.TextInputActive);
            drawer.SelectOnly(readme);
            await Task.Delay(60);
            await PressKey(0x1B); // 2nd Esc: clear selection
            Check("2nd Esc clears selection and keeps drawer open", drawer.IsVisible && drawer.SelectedCount == 0);
            await PressKey(0x1B); // 3rd Esc: unlock and close drawer keeping Dock
            bool escClosedDrawer = await WaitUntil(() => !drawer.IsVisible && !c.DrawerClosing, timeoutMs: 3500);
            Check("3rd Esc unlocks and closes drawer while keeping Dock",
                escClosedDrawer && !c.DrawerPinned && c.Dock.IsVisible);

            await ClickAt(outsidePt.X, outsidePt.Y);
            await WaitUntil(() => !c.Dock.IsVisible, timeoutMs: 3500);

            // ------------------------------------------------------------------
            // 7b. Keyboard search IME gate, capture expiry, mid-close Settle, and native Rename cleanup
            // ------------------------------------------------------------------
            await OpenDrawerSettled();
            await PressKey(0x46, 0x11); // Ctrl+F
            bool imeActive = drawer.TextInputActive && c.TestModalRegions?.TextInput == true;
            await PressKey(0x1B);
            bool imeCleared = !drawer.TextInputActive && c.TestModalRegions?.TextInput == false;
            Check("Ctrl+F keyboard search syncs ModalInputGate TextInput for IME", imeActive && imeCleared,
                new { imeActive, imeCleared });

            c.BeginShellWindowCapture(180);
            bool pendingInitially = c.TestModalRegions?.ShellCapturePending == true;
            bool expiredClearsPending = await WaitUntil(() => c.TestModalRegions?.ShellCapturePending == false, timeoutMs: 1200);
            Check("ShellCapturePending expires cleanly from modal region after timeout",
                pendingInitially && expiredClearsPending,
                new { pendingInitially, expiredClearsPending });

            // Interrupt closing animation caused by foreign foreground loss via SettleDrawerMotion
            ActivateForeignWindowForTest(sentinelHandle);
            await WaitUntil(() => c.DrawerClosing, timeoutMs: 1500, stepMs: 10);
            c.SettleDrawerMotion();
            await Task.Delay(80);
            bool settledOpen = drawer.IsVisible && !c.DrawerClosing && c.TestModalRegions != null;
            await ClickAt(outsidePt.X, outsidePt.Y);
            bool dismissedAfterSettle = await WaitUntil(() => !drawer.IsVisible && !c.Dock.IsVisible, timeoutMs: 3500);
            Check("SettleDrawerMotion clears drawerLostForeground and restores outside-click dismissal",
                settledOpen && dismissedAfterSettle,
                new { settledOpen, dismissedAfterSettle });

            await OpenDrawerSettled();
            readmePt = drawer.TileCenter(readme.Id);
            bool nativeRenameTriggered = false;
            bool nativeRenameShown = false;
            Thread? renameMenuDriver = null;
            ShellMenu.MenuDisplayed = () =>
            {
                nativeRenameShown = true;
                var menu = ShellMenu.ActivePopup;
                int renIdx = ShellMenu.LastVerbs.FindIndex(v => string.Equals(v, "rename", StringComparison.OrdinalIgnoreCase));
                if (renIdx < 0)
                    renIdx = ShellMenu.LastLabels.FindIndex(l => l.Contains("重命名", StringComparison.Ordinal) || l.Contains("Rename", StringComparison.OrdinalIgnoreCase));
                int pos = renIdx >= 0 ? ShellMenu.LastPositions[renIdx] : -1;
                renameMenuDriver = StartNativeMenuClickDriver(menu, pos, () => nativeRenameTriggered = true);
            };
            await RightClickAt(readmePt.X, readmePt.Y);
            Window? nativeRenameWin = null;
            await WaitUntil(() =>
                nativeRenameShown &&
                (nativeRenameWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.Title.Contains("重命名"))) != null,
                timeoutMs: 4000);
            renameMenuDriver?.Join(2000);
            ShellMenu.MenuDisplayed = null;
            bool renameStateClean = nativeRenameTriggered && nativeRenameWin != null && !c.MenuOpen && ShellMenu.ActivePopup == 0 && !drawer.MenuTracking;
            if (nativeRenameWin != null)
            {
                await PressKey(0x1B);
                await WaitUntil(() => !nativeRenameWin.IsVisible, timeoutMs: 3000);
            }
            await Task.Delay(120);
            Check("native ShellMenu Rename opens after menu tracking reset and keeps drawer open",
                renameStateClean && drawer.IsVisible && !c.DrawerClosing,
                new { nativeRenameTriggered, foundWin = nativeRenameWin != null, c.MenuOpen, activePopup = (long)ShellMenu.ActivePopup, drawerMenuTracking = drawer.MenuTracking, drawerVisible = drawer.IsVisible });
            await ClickAt(outsidePt.X, outsidePt.Y);
            await WaitUntil(() => !drawer.IsVisible && !c.Dock.IsVisible, timeoutMs: 3500);

            bool cyclesOk = true;
            for (int cycle = 0; cycle < 5; cycle++)
            {
                await WaitUntil(() => !c.Dock.IsVisible && !drawer.IsVisible && !c.DockMotionRunning && !c.DrawerMotionRunning, timeoutMs: 3000);
                Native.GetWindowRect(c.IndicatorHandle, out var indRect);
                await ClickAt((indRect.Left + indRect.Right) / 2, (indRect.Top + indRect.Bottom) / 2);
                if (!await WaitUntil(() => c.Dock.IsVisible && !c.DockMotionRunning, timeoutMs: 3000)) { cyclesOk = false; break; }
                var gw = c.Dock.GatewayCenter;
                await ClickAt(gw.X, gw.Y);
                if (!await WaitUntil(() => drawer.IsVisible && !c.DrawerMotionRunning && c.DrawerProgress == 1, timeoutMs: 3500)) { cyclesOk = false; break; }
                if (cycle % 2 == 0)
                {
                    var pt = drawer.TileCenter(manualEntry.Id);
                    await RightClickAt(pt.X, pt.Y);
                    if (!await WaitUntil(() => c.MenuOpen, timeoutMs: 2500)) { cyclesOk = false; break; }
                    await PressKey(0x1B);
                    if (!await WaitUntil(() => !c.MenuOpen && drawer.IsVisible, timeoutMs: 2500)) { cyclesOk = false; break; }
                }
                await ClickAt(outsidePt.X, outsidePt.Y);
                if (!await WaitUntil(() => !drawer.IsVisible && !c.Dock.IsVisible && !c.DrawerMotionRunning, timeoutMs: 3500)) { cyclesOk = false; break; }
            }
            Check("5 consecutive wake-open-menu-dismiss cycles leave zero residual state",
                cyclesOk && !c.MenuOpen && !c.Busy && !c.InternalDragging && !c.DrawerInteractionActive &&
                c.TrackedContextWindows.Count == 0 && Anim.ActiveTimers == 0 && !drawer.IsVisible && !c.Dock.IsVisible,
                new { cyclesOk, c.MenuOpen, c.Busy, c.InternalDragging, c.DrawerInteractionActive, tracked = c.TrackedContextWindows.Count, Anim.ActiveTimers });

            CaptureScreen(c.Screen, Path.Combine(output, "drawer-dismiss-final-clean.png"));
            Check("no competing physical input during test", PointerHook.NonInjectedInputs == 0, PointerHook.NonInjectedInputs);
        }
        catch (Exception ex)
        {
            failed = true;
            results.Add(new { name = "exception", pass = false, detail = ex.ToString() });
        }
        finally
        {
            GestureChecks.Trace = null;
            ShellMenu.MenuDisplayed = null;
            File.WriteAllLines(Path.Combine(output, "dismiss-trace.log"), traceLog);
            PointerHook.AuditInput = false;
            Native.TestButton(false);
            Native.TestRightButton(false);
            Native.TestKey(0x12, false);
            SafeCloseDialogs(openedDialogs);
            CloseExplorerFoldersMatching("MacDockRevealProbeDir");
            foreach (var p in spawned)
            {
                try { if (!p.HasExited) p.Kill(true); } catch { }
                try { p.Dispose(); } catch { }
            }
            sentinel.Close();
            if (PointerHook.NonInjectedInputs == 0)
            {
                Native.MovePointer(originalCursor.X, originalCursor.Y);
                Native.SetForegroundWindow(originalForeground);
            }
            File.WriteAllText(Path.Combine(output, "drawer-dismiss-test.json"),
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = failed ? 1 : 0;
            Application.Current.Shutdown(failed ? 1 : 0);
        }
    }
}
