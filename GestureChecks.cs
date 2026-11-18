using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacDock;

internal static class GestureChecks
{
    internal static volatile Action<string, object?>? Trace;
    sealed record Event(double Ms, string Batch, string Name, object? Detail);

    internal static async Task Run(Controller owner)
    {
        string output = Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var clock = Stopwatch.StartNew();
        var events = new ConcurrentQueue<Event>();
        var results = new List<object>();
        string batch = "setup";
        void Mark(string name, object? detail = null) =>
            events.Enqueue(new Event(clock.Elapsed.TotalMilliseconds, Volatile.Read(ref batch), name, detail));
        void Save()
        {
            File.WriteAllText(Path.Combine(output, "gesture-events.json"), JsonSerializer.Serialize(events.ToArray()));
            File.WriteAllText(Path.Combine(output, "gesture-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }

        Trace = Mark;
        InteractionChecks.Record = Mark;
        var background = new Window
        {
            Title = "MacDock real pointer test", WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Left = owner.WorkArea.Left / owner.Scale, Top = owner.WorkArea.Top / owner.Scale,
            Width = owner.WorkArea.Width / owner.Scale, Height = owner.WorkArea.Height / owner.Scale,
            Background = new SolidColorBrush(Color.FromRgb(30, 58, 120))
        };
        Process? fixture = null;
        string fixtureState = Path.Combine(output, "foreground-fixture");
        Native.GetCursorPos(out var original);
        PointerHook.NonInjectedInputs = 0;
        PointerHook.AuditInput = true;
        try
        {
            if (Environment.GetEnvironmentVariable("MACDOCK_GESTURE_FOREIGN") == "1")
            {
                File.WriteAllText(fixtureState, "black");
                var launch = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                launch.ArgumentList.Add("--external-fixture");
                launch.ArgumentList.Add(fixtureState);
                fixture = Process.Start(launch);
                for (int i = 0; i < 50 && !File.Exists(fixtureState + ".ready"); i++) await Task.Delay(25);
                var handle = (nint)long.Parse(File.ReadAllText(fixtureState + ".ready").Split('|')[0]);
                Native.ActivateWindow(handle);
                Mark("fixture-foreground", new { hwnd = (long)handle, foreground = (long)Native.GetForegroundWindow() });
            }
            else background.Show();
            owner.Busy = true;
            owner.Expand();
            await Task.Delay(400);
            var center = owner.Dock.GatewayCenter;
            var dock = owner.Dock.Handle;
            Native.GetWindowRect(dock, out var rect);
            Native.MovePointer(center.X, center.Y);
            await Task.Delay(110);
            owner.Busy = false;

            HwndSource.FromHwnd(dock).AddHook((nint h, int m, nint w, nint l, ref bool handled) =>
            {
                if (m is 0x201 or 0x202 or 0x203 or 0x215)
                    Mark("dock-message", new { message = m, phase = owner.DrawerProgress });
                return 0;
            });
            async Task Clicks(string label, int count, int[] spacing, int offset = 0, bool alternate = false, int vertical = 0, int hold = 22)
            {
                Volatile.Write(ref batch, label);
                bool initial = owner.Drawer?.IsVisible == true;
                Native.MovePointer(center.X + offset, center.Y + vertical);
                await Task.Delay(90);
                await Task.Run(() =>
                {
                    var schedule = Stopwatch.StartNew();
                    int next = 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (alternate) Native.MovePointer(center.X + (i % 2 == 0 ? offset : -offset), center.Y + vertical);
                        while (schedule.Elapsed.TotalMilliseconds < next) Thread.Sleep(1);
                        Native.GetCursorPos(out var p);
                        Mark("input-down", new { index = i, p.X, p.Y, hit = (long)Native.WindowFromPoint(p), dock = (long)dock });
                        Native.TestButton(true);
                        Thread.Sleep(hold);
                        Mark("input-up", i);
                        Native.TestButton(false);
                        next += spacing[i % spacing.Length];
                    }
                });
                await Task.Delay(650);
                var all = events.Where(e => e.Batch == label).ToArray();
                bool final = owner.Drawer?.IsVisible == true;
                var downs = all.Count(e => e.Name == "hook-down");
                var ups = all.Count(e => e.Name == "hook-up");
                var released = all.Count(e => e.Name == "gateway-click");
                var targets = all.Where(e => e.Name == "drawer-target")
                    .Select(e => JsonSerializer.SerializeToElement(e.Detail).GetProperty("open").GetBoolean()).ToArray();
                var expected = Enumerable.Range(0, count).Select(i => initial == (i % 2 != 0)).ToArray();
                var times = all.Where(e => e.Name == "input-down").Select(e => e.Ms).ToArray();
                var gaps = times.Skip(1).Zip(times, (x, y) => Math.Round(x - y, 2)).ToArray();
                results.Add(new { name = label, count, downs, ups, released, targets, gaps, initial, final,
                    expectedFinal = count % 2 == 0 ? initial : !initial,
                    pass = downs == count && ups == count && released == count && targets.SequenceEqual(expected)
                        && final == (count % 2 == 0 ? initial : !initial) && !owner.DrawerMotionRunning });
                Save();
            }

            if (Environment.GetEnvironmentVariable("MACDOCK_GESTURE_EDGES") == "1")
            {
                Volatile.Write(ref batch, "within Dock and repeated exit");
                int y = center.Y;
                var outsideEdge = new Native.Point(center.X, rect.Top - 62);
                int stays = 0;
                for (int x = rect.Left + 18; x < rect.Right - 16; x += 13)
                {
                    Native.MovePointer(x, y);
                    await Task.Delay(14);
                    if (owner.Dock.IsVisible && !owner.DockMotionRunning) stays++;
                }
                results.Add(new { name = "all icon slots and gaps preserve the settled Dock",
                    stays, pass = stays == (rect.Right - 16 - (rect.Left + 18) + 12) / 13 });

                owner.DockAnimationFrame = (phase, velocity) => Mark("dock-frame", new { phase, velocity });
                for (int i = 0; i < 6; i++)
                {
                    Volatile.Write(ref batch, "rapid edge " + i);
                    Native.MovePointer(outsideEdge.X, outsideEdge.Y);
                    await Task.Delay(47);
                    bool started = owner.DockMotionRunning && owner.DockProgress < 1;
                    double returnAt = clock.Elapsed.TotalMilliseconds;
                    Native.MovePointer(center.X, center.Y);
                    await Task.Delay(350);
                    var current = events.Where(e => e.Batch == batch).ToArray();
                    double reverse = current.FirstOrDefault(e => e.Name == "dock-show" && e.Ms >= returnAt)?.Ms - returnAt ?? -1;
                    results.Add(new { name = batch, started, reverse,
                        pass = started && reverse is >= 0 and < 65 && owner.Dock.IsVisible && !owner.DockMotionRunning });
                }
                owner.DockAnimationFrame = null;

                await Clicks("open before cancelled release", 1, new[] { 600 });
                Volatile.Write(ref batch, "cancel capture at drawer end");
                Native.TestButton(true);
                await Task.Delay(22);
                Native.TestButton(false);
                await Task.Delay(100);
                var lowerButton = new Native.Point(center.X, center.Y + 17);
                Native.MovePointer(lowerButton.X, lowerButton.Y);
                Native.TestButton(true);
                for (int i = 0; i < 50 && !events.Any(e => e.Batch == batch && e.Name == "drawer-hide-deferred"); i++)
                    await Task.Delay(8);
                bool deferred = events.Any(e => e.Batch == batch && e.Name == "drawer-hide-deferred");
                Native.MovePointer(outsideEdge.X, outsideEdge.Y);
                await Task.Delay(40);
                Native.TestButton(false);
                await Task.Delay(520);
                var issued = events.Count(e => e.Batch == batch && e.Name == "gateway-click");
                results.Add(new { name = "release outside cancels gateway and finishes zero-pose teardown",
                    deferred, issued, pass = deferred && issued == 1 && owner.Drawer?.IsVisible == false && !owner.Dock.IsVisible });
                results.Add(new { name = "no competing hardware input", pass = PointerHook.NonInjectedInputs == 0, external = PointerHook.NonInjectedInputs });
                return;
            }

            if (Environment.GetEnvironmentVariable("MACDOCK_GESTURE_STRESS") == "1")
            {
                owner.DrawerAnimationFrame = (phase, _, _, _) => Mark("drawer-frame", phase);
                await Clicks("cold center 120", 9, new[] { 120 });
                await Clicks("warm upper 100", 12, new[] { 100 }, 0, false, -19);
                await Clicks("warm upper 145", 12, new[] { 145 }, 0, false, -19, 55);
                await Clicks("warm right 170", 12, new[] { 170 }, 19, false, 0, 66);
                await Clicks("warm lower 190", 12, new[] { 190 }, 0, false, 18, 76);
                await Clicks("upper mixed near boundary", 12, new[] { 117, 160, 197, 262 }, 7, true, -20);
                await Clicks("warm upper 132", 12, new[] { 132 }, 0, false, -20, 32);
                await Clicks("hold through close 220", 12, new[] { 220 }, 0, false, 19, 120);
                await Clicks("hold through close 260", 12, new[] { 260 }, 0, false, 19, 185);
                owner.DrawerAnimationFrame = null;
            }
            else
            {
                await Clicks("cold single", 1, new[] { 700 });
                await Clicks("settled single", 1, new[] { 700 });
                foreach (int interval in new[] { 80, 100, 120, 140, 160, 180, 200, 240, 300 })
                    await Clicks("center " + interval, 8, new[] { interval });
                await Clicks("same offset 150", 9, new[] { 150 }, 11);
                await Clicks("two positions 150", 8, new[] { 150 }, 11, true);
                await Clicks("mixed intervals", 9, new[] { 82, 296, 126, 203, 91, 243, 154 }, -9);
            }
            if (owner.Drawer?.IsVisible == true)
            {
                Native.TestKey(27, true);
                await Task.Delay(20);
                Native.TestKey(27, false);
                await Task.Delay(450);
            }

            var outside = new Native.Point(center.X, rect.Top - 84);
            for (int i = 0; i < 10; i++)
            {
                Volatile.Write(ref batch, "leave " + i);
                owner.Busy = true;
                owner.Expand();
                Native.MovePointer(center.X, center.Y);
                await Task.Delay(420);
                owner.Busy = false;
                double leaveAt = clock.Elapsed.TotalMilliseconds;
                Native.MovePointer(outside.X, outside.Y);
                for (int n = 0; n < 50 && !owner.DockMotionRunning; n++) await Task.Delay(6);
                double targetAt = events.LastOrDefault(e => e.Batch == batch && e.Name == "dock-hide")?.Ms ?? -1;
                await Task.Delay(42);
                double before = owner.DockProgress;
                double returnAt = clock.Elapsed.TotalMilliseconds;
                Native.MovePointer(center.X, center.Y);
                await Task.Delay(420);
                double reverseAt = events.FirstOrDefault(e => e.Batch == batch && e.Name == "dock-show" && e.Ms >= returnAt)?.Ms ?? -1;
                results.Add(new { name = batch, hideDelay = targetAt < 0 ? -1 : targetAt - leaveAt, reverseDelay = reverseAt < 0 ? -1 : reverseAt - returnAt,
                    before, visible = owner.Dock.IsVisible, pass = targetAt >= leaveAt && targetAt - leaveAt < 65
                    && reverseAt >= returnAt && reverseAt - returnAt < 65 && before is > 0 and < 1 && owner.Dock.IsVisible });
            }
            results.Add(new { name = "no competing hardware input", pass = PointerHook.NonInjectedInputs == 0, external = PointerHook.NonInjectedInputs });
        }
        catch (Exception e) { results.Add(new { name = "exception", pass = false, detail = e.ToString() }); }
        finally
        {
            Trace = null;
            InteractionChecks.Record = null;
            PointerHook.AuditInput = false;
            Native.TestButton(false);
            if (PointerHook.NonInjectedInputs == 0) Native.MovePointer(original.X, original.Y);
            Save();
            if (fixture != null) { File.WriteAllText(fixtureState, "exit"); fixture.WaitForExit(1500); fixture.Dispose(); }
            else background.Close();
            Application.Current.Shutdown();
        }
    }
}
