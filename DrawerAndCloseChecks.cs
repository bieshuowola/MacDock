using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacDock;

internal static class DrawerAndCloseChecks
{
    internal static int Fixture(string[] args)
    {
        int idx = Array.IndexOf(args, "--close-fixture");
        string token = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : "default";
        int count = idx >= 0 && idx + 2 < args.Length && int.TryParse(args[idx + 2], out int n) ? n : 1;

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var openWindows = new List<Window>();
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        guard.Tick += (_, _) => { guard.Stop(); app.Shutdown(0); };

        app.Startup += (_, _) =>
        {
            guard.Start();
            if (count <= 0)
            {
                // Running process with no visible application window (simulates background/tray-only app)
                var hidden = new Window
                {
                    Title = $"MacDock CloseFixture Hidden {token}",
                    Width = 220,
                    Height = 120,
                    ShowInTaskbar = false,
                    Visibility = Visibility.Hidden
                };
                openWindows.Add(hidden);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var w = new Window
                {
                    Title = $"MacDock CloseFixture {token} #{i + 1}",
                    Width = 300,
                    Height = 180,
                    Left = 80 + i * 40,
                    Top = 80 + i * 40,
                    ShowActivated = false,
                    Background = Brushes.WhiteSmoke,
                    Content = new TextBlock
                    {
                        Text = $"Fixture: {token} (#{i + 1})",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                w.Closed += (_, _) =>
                {
                    openWindows.Remove(w);
                    if (openWindows.Count == 0)
                    {
                        guard.Stop();
                        app.Shutdown(0);
                    }
                };
                openWindows.Add(w);
                w.Show();
            }
        };
        return app.Run();
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

    static MenuItem? FindMenuItem(ContextMenu menu, string label) =>
        menu.Items.OfType<MenuItem>().FirstOrDefault(i =>
            string.Equals(System.Windows.Automation.AutomationProperties.GetName(i), label, StringComparison.Ordinal));

    static async Task<bool> WaitUntil(Func<bool> predicate, int timeoutMs = 6000, int stepMs = 80)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate()) return true;
            await Task.Delay(stepMs);
        }
        return predicate();
    }

    internal static async Task Run(Controller c)
    {
        string output = Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS") ?? Path.Combine(AppContext.BaseDirectory, "test-results");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        var spawned = new List<Process>();
        bool failed = false;

        void Check(string name, bool pass, object? detail = null)
        {
            results.Add(new { name, pass, detail });
            if (!pass) failed = true;
        }

        Process StartFixture(string token, int count)
        {
            var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--close-fixture {token} {count}")
            {
                UseShellExecute = false
            })!;
            spawned.Add(p);
            return p;
        }

        try
        {
            string fixtureRoot = Path.Combine(output, "drawer-close-fixtures");
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true);
            Directory.CreateDirectory(fixtureRoot);
            string exe = Environment.ProcessPath!;

            // ------------------------------------------------------------------
            // Part 1: Cancel "手动添加 · 快捷入口" separate group & unify grid
            // ------------------------------------------------------------------
            c.Store.Data.Entries.Clear();
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Mode = "自定义";
            c.Store.Data.ListView = false;

            var desktopItems = new List<DesktopItem>();
            for (int i = 1; i <= 8; i++)
            {
                string f = Path.Combine(fixtureRoot, $"DesktopApp{i}.txt");
                File.WriteAllText(f, $"desktop-{i}");
                desktopItems.Add(new DesktopItem(f, $"桌面项目 {i}", "desktop", null));
                c.Store.Data.Entries.Add(new Entry { Path = f, Name = $"桌面项目 {i}", Source = "desktop", Pinned = i <= 2 });
            }

            string workBuddyLnk = Path.Combine(fixtureRoot, "WorkBuddy.lnk");
            CreateShortcut(workBuddyLnk, exe, "--close-fixture workbuddy-manual 1");
            string manualToolPath = Path.Combine(fixtureRoot, "ManualTool.txt");
            File.WriteAllText(manualToolPath, "manual-tool-original");

            c.Store.Add(workBuddyLnk, pin: true, name: "WorkBuddy", source: "manual");
            c.Store.Add(manualToolPath, pin: false, name: "ManualTool", source: "manual");
            var workBuddy = c.Store.Data.Entries.Single(e => e.Path == workBuddyLnk);
            var manualTool = c.Store.Data.Entries.Single(e => e.Path == manualToolPath);

            // Missing manual shortcut fixture (mirrors pinned-desktop-unavailable WorkBuddy.lnk case)
            string missingManualLnk = Path.Combine(fixtureRoot, "MissingManual.lnk");
            var missingManual = new Entry { Path = missingManualLnk, Name = "MissingManual", Source = "manual", Origin = "pinned-desktop-unavailable", Pinned = false };
            c.Store.Data.Entries.Add(missingManual);

            // Add app shortcuts for Part 2
            string singlePinnedLnk = Path.Combine(fixtureRoot, "SinglePinned.lnk");
            string multiPinnedLnk = Path.Combine(fixtureRoot, "MultiPinned.lnk");
            string unpinnedDrawerLnk = Path.Combine(fixtureRoot, "UnpinnedDrawer.lnk");
            string notRunningLnk = Path.Combine(fixtureRoot, "NotRunning.lnk");
            string hiddenOnlyLnk = Path.Combine(fixtureRoot, "HiddenOnly.lnk");
            string bystanderLnk = Path.Combine(fixtureRoot, "Bystander.lnk");

            CreateShortcut(singlePinnedLnk, exe, "--close-fixture single-pinned 1");
            CreateShortcut(multiPinnedLnk, exe, "--close-fixture multi-pinned 2");
            CreateShortcut(unpinnedDrawerLnk, exe, "--close-fixture unpinned-drawer 1");
            CreateShortcut(notRunningLnk, exe, "--close-fixture not-running 1");
            CreateShortcut(hiddenOnlyLnk, exe, "--close-fixture hidden-only 0");
            CreateShortcut(bystanderLnk, exe, "--close-fixture bystander 1");

            c.Store.Add(singlePinnedLnk, pin: true, name: "SingleApp", source: "manual");
            c.Store.Add(multiPinnedLnk, pin: true, name: "MultiApp", source: "manual");
            c.Store.Add(unpinnedDrawerLnk, pin: false, name: "UnpinnedApp", source: "manual");
            c.Store.Add(notRunningLnk, pin: true, name: "IdleApp", source: "manual");
            c.Store.Add(hiddenOnlyLnk, pin: true, name: "TrayApp", source: "manual");
            c.Store.Add(bystanderLnk, pin: true, name: "BystanderApp", source: "manual");

            var singlePinned = c.Store.Data.Entries.Single(e => e.Path == singlePinnedLnk);
            var multiPinned = c.Store.Data.Entries.Single(e => e.Path == multiPinnedLnk);
            var unpinnedDrawer = c.Store.Data.Entries.Single(e => e.Path == unpinnedDrawerLnk);
            var notRunning = c.Store.Data.Entries.Single(e => e.Path == notRunningLnk);
            var hiddenOnly = c.Store.Data.Entries.Single(e => e.Path == hiddenOnlyLnk);
            var bystander = c.Store.Data.Entries.Single(e => e.Path == bystanderLnk);

            c.Changed();
            c.Expand();
            await Task.Delay(300);
            c.ToggleDrawer();
            await Task.Delay(600);

            var drawer = c.Drawer!;
            Check("custom mode has no manual group title",
                drawer.RenderedGroupTitles.All(t => !t.Contains("手动添加") && !t.Contains("快捷入口")),
                drawer.RenderedGroupTitles);
            Check("custom mode renders single unified WrapPanel",
                drawer.RenderedGroups == 1,
                new { groups = drawer.RenderedGroups, titles = drawer.RenderedGroupTitles });

            var unifiedTiles = drawer.SortTiles();
            Check("WorkBuddy and ManualTool included in unified SortTiles",
                unifiedTiles.Any(t => ((Entry)t.Tag).Id == workBuddy.Id) &&
                unifiedTiles.Any(t => ((Entry)t.Tag).Id == manualTool.Id) &&
                unifiedTiles.Any(t => ((Entry)t.Tag).Id == missingManual.Id) &&
                unifiedTiles.Count == c.Store.Data.Entries.Count(e => e.InDrawer),
                new { tileCount = unifiedTiles.Count, entryCount = c.Store.Data.Entries.Count(e => e.InDrawer) });

            var iconDiag = drawer.EnsureIconsReady(loadMissingSynchronously: true);
            Check("all drawer icons including manual and missing-target shortcuts render without blank placeholders",
                iconDiag.BlankPlaceholders == 0 && iconDiag.Ready == iconDiag.Total && iconDiag.UnarrangedImages == 0,
                iconDiag);

            CaptureScreen(c.Screen, Path.Combine(output, "drawer-unified-grid.png"));

            // Check other sort modes ("按类型", "首字母") also never emit manual group
            foreach (string mode in new[] { "按类型", "首字母" })
            {
                c.Store.Data.Mode = mode;
                drawer.Render();
                await Task.Delay(120);
                Check($"mode {mode} has no manual group title",
                    drawer.RenderedGroupTitles.All(t => !t.Contains("手动添加") && !t.Contains("快捷入口")),
                    new { mode, titles = drawer.RenderedGroupTitles });
                Check($"mode {mode} includes WorkBuddy in normal groups",
                    drawer.SortTiles().Any(t => ((Entry)t.Tag).Id == workBuddy.Id));
            }

            // Back to custom mode: reorder manual WorkBuddy to slot 0 among desktop items
            c.Store.Data.Mode = "自定义";
            drawer.Render();
            await Task.Delay(120);
            var scope = drawer.SortTiles().Select(t => ((Entry)t.Tag).Id).ToArray();
            c.CommitSort(scope, workBuddy.Id, 0, dock: false);
            await Task.Delay(150);
            Check("manual WorkBuddy can be sorted into slot 0 of unified grid",
                ((Entry)drawer.SortTiles()[0].Tag).Id == workBuddy.Id &&
                c.Store.Data.Entries[0].Id == workBuddy.Id);

            // Simulate desktop rescan + config reload from disk
            c.Store.Sync(desktopItems);
            c.Store.Save();
            drawer.Render();
            await Task.Delay(120);
            var reloaded = new Store(Path.GetDirectoryName(c.Store.FilePath));
            var reloadedWorkBuddy = reloaded.Data.Entries.SingleOrDefault(e => string.Equals(e.Path, workBuddyLnk, StringComparison.OrdinalIgnoreCase));
            Check("sync and reload preserve WorkBuddy without duplicates or manual group",
                reloadedWorkBuddy != null &&
                reloadedWorkBuddy.Pinned &&
                reloaded.Data.Entries[0].Id == workBuddy.Id &&
                reloaded.Data.Entries.Count(e => string.Equals(e.Path, workBuddyLnk, StringComparison.OrdinalIgnoreCase)) == 1 &&
                File.Exists(manualToolPath) && File.ReadAllText(manualToolPath) == "manual-tool-original" &&
                drawer.RenderedGroupTitles.All(t => !t.Contains("手动添加") && !t.Contains("快捷入口")),
                new { firstId = reloaded.Data.Entries.FirstOrDefault()?.Id, pinned = reloadedWorkBuddy?.Pinned });

            // ------------------------------------------------------------------
            // Part 2: Dock & Drawer right-click "关闭"
            // ------------------------------------------------------------------
            c.CloseDrawer(keepDock: true);
            await Task.Delay(450);

            var pSingle = StartFixture("single-pinned", 1);
            var pMulti = StartFixture("multi-pinned", 2);
            var pUnpinned = StartFixture("unpinned-drawer", 1);
            var pHidden = StartFixture("hidden-only", 0);
            var pBystander = StartFixture("bystander", 1);

            bool fixturesReady = await WaitUntil(() =>
                c.Dock.ClosableWindows(singlePinned).Count == 1 &&
                c.Dock.ClosableWindows(multiPinned).Count == 2 &&
                c.Dock.ClosableWindows(unpinnedDrawer).Count == 1 &&
                c.Dock.ClosableWindows(bystander).Count == 1, timeoutMs: 8000);
            Check("all visible fixture windows detected accurately", fixturesReady, new
            {
                single = c.Dock.ClosableWindows(singlePinned).Count,
                multi = c.Dock.ClosableWindows(multiPinned).Count,
                unpinned = c.Dock.ClosableWindows(unpinnedDrawer).Count,
                bystander = c.Dock.ClosableWindows(bystander).Count,
                notRunning = c.Dock.ClosableWindows(notRunning).Count,
                hiddenOnly = c.Dock.ClosableWindows(hiddenOnly).Count
            });

            c.Dock.UpdateDots(force: true);
            bool dotsShown = await WaitUntil(() =>
                c.Dock.DotVisible(singlePinned.Id) &&
                c.Dock.DotVisible(multiPinned.Id) &&
                c.Dock.DotVisible(bystander.Id) &&
                !c.Dock.DotVisible(notRunning.Id), timeoutMs: 5000);
            Check("Dock running dots reflect running fixtures", dotsShown);

            // Check notRunning and hiddenOnly do NOT get "关闭"
            var idleMenu = new ContextMenu();
            EntryMenu.Fill(idleMenu, c, new[] { notRunning });
            Check("non-running app menu does not show 关闭", FindMenuItem(idleMenu, "关闭") == null);

            var hiddenMenu = new ContextMenu();
            EntryMenu.Fill(hiddenMenu, c, new[] { hiddenOnly });
            Check("hidden-only background app menu does not show 关闭", FindMenuItem(hiddenMenu, "关闭") == null);

            // Open actual Dock ContextMenu for singlePinned and capture visual proof
            var singleTile = c.Dock.TileControlForId(singlePinned.Id);
            Check("singlePinned Dock tile exists with ContextMenu", singleTile?.ContextMenu != null);
            var dockMenu = singleTile!.ContextMenu!;
            EntryMenu.Fill(dockMenu, c, new[] { singlePinned });
            var closeItem = FindMenuItem(dockMenu, "关闭");
            Check("running pinned Dock app menu shows enabled 关闭", closeItem != null && closeItem.IsEnabled);

            dockMenu.PlacementTarget = singleTile;
            dockMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            dockMenu.IsOpen = true;
            await Task.Delay(250);
            CaptureScreen(c.Screen, Path.Combine(output, "dock-menu-close.png"));

            // Click "关闭" via menu item
            closeItem!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            dockMenu.IsOpen = false;
            await Task.Delay(150);

            bool singleClosed = await WaitUntil(() => pSingle.HasExited, timeoutMs: 6000);
            bool singleDotCleared = await WaitUntil(() => !c.Dock.DotVisible(singlePinned.Id), timeoutMs: 5000);
            Check("singlePinned closes gracefully via WM_CLOSE and clears Dock dot",
                singleClosed && pSingle.ExitCode == 0 && singleDotCleared,
                new { exited = pSingle.HasExited, exitCode = pSingle.HasExited ? pSingle.ExitCode : -1, dotVisible = c.Dock.DotVisible(singlePinned.Id) });
            Check("closing pinned app preserves pin, entry, shortcut file, and bystander",
                singlePinned.Pinned &&
                c.Store.Data.Entries.Contains(singlePinned) &&
                File.Exists(singlePinnedLnk) &&
                !pBystander.HasExited);

            // Re-open menu for now-closed singlePinned: "关闭" must disappear
            EntryMenu.Fill(dockMenu, c, new[] { singlePinned });
            Check("closed app menu no longer shows 关闭", FindMenuItem(dockMenu, "关闭") == null);

            // Multi-window + already-closed-window race test:
            // Open menu while 2 windows exist, close 1 window externally, then click "关闭"
            var multiMenu = new ContextMenu();
            EntryMenu.Fill(multiMenu, c, new[] { multiPinned });
            var multiCloseItem = FindMenuItem(multiMenu, "关闭");
            Check("multi-window app menu shows 关闭", multiCloseItem != null);
            var multiWindows = c.Dock.ClosableWindows(multiPinned);
            Native.PostCloseWindow(multiWindows[0]);
            await WaitUntil(() => c.Dock.ClosableWindows(multiPinned).Count == 1, timeoutMs: 4000);
            multiCloseItem!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            bool multiClosed = await WaitUntil(() => pMulti.HasExited, timeoutMs: 6000);
            bool multiDotCleared = await WaitUntil(() => !c.Dock.DotVisible(multiPinned.Id), timeoutMs: 5000);
            Check("multi-window app closes remaining window cleanly and clears dot",
                multiClosed && pMulti.ExitCode == 0 && multiDotCleared && multiPinned.Pinned && !pBystander.HasExited,
                new { exited = pMulti.HasExited, dotVisible = c.Dock.DotVisible(multiPinned.Id) });

            // Unpinned running app in Drawer
            var unpinnedMenu = new ContextMenu();
            EntryMenu.Fill(unpinnedMenu, c, new[] { unpinnedDrawer });
            var unpinnedCloseItem = FindMenuItem(unpinnedMenu, "关闭");
            Check("unpinned running app menu shows 关闭", unpinnedCloseItem != null && !unpinnedDrawer.Pinned);
            unpinnedCloseItem!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            bool unpinnedClosed = await WaitUntil(() => pUnpinned.HasExited, timeoutMs: 6000);
            Check("unpinned app closes cleanly without pinning or deleting shortcut",
                unpinnedClosed && pUnpinned.ExitCode == 0 && !unpinnedDrawer.Pinned && File.Exists(unpinnedDrawerLnk) && !pBystander.HasExited);
        }
        catch (Exception ex)
        {
            failed = true;
            results.Add(new { name = "exception", pass = false, detail = ex.ToString() });
        }
        finally
        {
            foreach (var p in spawned)
            {
                try { if (!p.HasExited) p.Kill(true); } catch { }
                try { p.Dispose(); } catch { }
            }
            File.WriteAllText(Path.Combine(output, "drawer-close-test.json"),
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = failed ? 1 : 0;
            System.Windows.Application.Current.Shutdown(failed ? 1 : 0);
        }
    }
}
