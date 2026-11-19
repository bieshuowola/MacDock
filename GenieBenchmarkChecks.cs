#if MACDOCK_EXPERIENCE_CHECKS
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace MacDock;

internal static class GenieBenchmarkChecks
{
    const nuint InputTag = 0x4D44424D;
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    [StructLayout(LayoutKind.Sequential)] struct LastInput { internal uint Size, Tick; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);

    static uint LastInputTick()
    {
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref input)) throw new InvalidOperationException("GetLastInputInfo failed");
        return input.Tick;
    }

    internal static async Task Run(Controller c)
    {
        string dir = Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        Directory.CreateDirectory(dir);
        string shotsDir = Path.Combine(dir, "shots");
        Directory.CreateDirectory(shotsDir);

        var clock = Stopwatch.StartNew();
        double freqMs = 1000.0 / Stopwatch.Frequency;
        var records = new List<object>();
        var failures = new List<string>();
        using var proc = Process.GetCurrentProcess();
        Window? bg = null;

        void Save()
        {
            var payload = new
            {
                engine = GenieEngine.Mode.ToString(),
                utc = DateTimeOffset.UtcNow,
                display = new
                {
                    device = c.Screen.DeviceName,
                    bounds = Native.MonitorArea(c.Screen, false).ToString(),
                    work = c.WorkArea.ToString(),
                    scale = c.Scale,
                    refreshHz = Native.GetDisplayRefreshRate(c.Screen.DeviceName),
                    tier = RenderCapability.Tier >> 16
                },
                runtime = RuntimeInformation.FrameworkDescription,
                records,
                failures
            };
            File.WriteAllText(Path.Combine(dir, "genie-benchmark.json"),
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task Until(Func<bool> cond, int timeoutMs = 4500)
        {
            var dl = Stopwatch.StartNew();
            while (!cond() && dl.ElapsedMilliseconds < timeoutMs) await Task.Delay(8);
            if (!cond()) throw new TimeoutException("Timeout waiting condition. " + c.Diagnostics);
        }

        async Task MovePointer(int x, int y)
        {
            if (!Native.SetCursorPos(x - 1, y)) throw new InvalidOperationException("SetCursorPos failed");
            mouse_event(1, 1, 0, 0, InputTag);
            await Task.Delay(18);
        }

        Task ParkPointer() => MovePointer(c.WorkArea.Left + 90, c.WorkArea.Top + 100);

        async Task EnsureDockVisible()
        {
            if (!c.Dock.IsVisible)
            {
                await ParkPointer();
                await Task.Delay(40);
                var wake = c.WakeBounds;
                await MovePointer((wake.Left + wake.Right) / 2, (wake.Top + wake.Bottom) / 2);
                var dl = Stopwatch.StartNew();
                while (!c.Dock.IsVisible && dl.ElapsedMilliseconds < 600) await Task.Delay(12);
                if (!c.Dock.IsVisible)
                {
                    c.Expand();
                    await Until(() => c.Dock.IsVisible);
                }
            }
            c.SettleDockMotion();
            await Until(() => Native.WindowFromPoint(c.Dock.GatewayCenter) == c.Dock.Handle);
        }

        async Task ClickGateway()
        {
            var pt = c.Dock.GatewayCenter;
            await MovePointer(pt.X, pt.Y);
            await Task.Delay(24);
            if (Native.WindowFromPoint(pt) != c.Dock.Handle)
                throw new InvalidOperationException($"Gateway point ({pt.X},{pt.Y}) not owned by Dock (hit={Native.WindowFromPoint(pt)})");
            mouse_event(2, 0, 0, 0, InputTag);
            await Task.Delay(16);
            mouse_event(4, 0, 0, 0, InputTag);
        }

        static double Percentile(double[] sorted, double pct)
        {
            if (sorted.Length == 0) return 0;
            int idx = Math.Clamp((int)Math.Ceiling(sorted.Length * pct) - 1, 0, sorted.Length - 1);
            return sorted[idx];
        }

        void CaptureArea(string name)
        {
            var a = c.WorkArea;
            int w = (int)Math.Min(a.Width, c.Layout.DrawerWidth * c.Scale + 80);
            int h = (int)Math.Min(a.Height, (c.Layout.DrawerHeight(0, 0) + c.Layout.DockHeight + 100) * c.Scale);
            int x = Math.Max(a.Left, a.Left + (a.Width - w) / 2);
            int y = Math.Max(a.Top, a.Bottom - h);
            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, bmp.Size);
            bmp.Save(Path.Combine(shotsDir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        async Task<object> MeasureCycle(string scenario, bool openAction, bool useMouseClick = true, int idleSec = 0)
        {
            var frameTicks = new List<long>(96);
            var framePhases = new List<double>(96);
            var cbMs = new List<double>(96);
            long enterTick = 0;
            bool complete = false;
            string expectedStage = openAction ? "open-complete" : "close-complete";
            long triggerTick = 0;

            c.DrawerTiming = stage =>
            {
                if (stage == "frame-enter") enterTick = Stopwatch.GetTimestamp();
                else if (stage == "frame-leave" && enterTick != 0) cbMs.Add((Stopwatch.GetTimestamp() - enterTick) * freqMs);
                else if (stage == expectedStage) complete = true;
            };
            c.DrawerAnimationFrame = (phase, _, _, _) =>
            {
                frameTicks.Add(Stopwatch.GetTimestamp());
                framePhases.Add(phase);
            };

            long allocBefore = GC.GetTotalAllocatedBytes();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            proc.Refresh();
            double cpuBefore = proc.TotalProcessorTime.TotalMilliseconds;

            try
            {
                if (useMouseClick)
                {
                    var pt = c.Dock.GatewayCenter;
                    await MovePointer(pt.X, pt.Y);
                    await Task.Delay(20);
                    if (Native.WindowFromPoint(pt) != c.Dock.Handle)
                        throw new InvalidOperationException($"Gateway point ({pt.X},{pt.Y}) not owned by Dock");
                    mouse_event(2, 0, 0, 0, InputTag);
                    await Task.Delay(16);
                    triggerTick = Stopwatch.GetTimestamp();
                    mouse_event(4, 0, 0, 0, InputTag);
                }
                else
                {
                    triggerTick = Stopwatch.GetTimestamp();
                    if (openAction) c.ToggleDrawer();
                    else c.CloseDrawer(keepDock: true, force: true);
                }
                await Until(() => complete, 5000);
            }
            finally
            {
                c.DrawerTiming = null;
                c.DrawerAnimationFrame = null;
            }

            proc.Refresh();
            double cpuMs = proc.TotalProcessorTime.TotalMilliseconds - cpuBefore;
            long allocBytes = GC.GetTotalAllocatedBytes() - allocBefore;
            int[] gcDelta = { GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2 };

            // Exclude final synchronous Finish() echo at phase==0 or phase==1 within <0.4ms of prior tick
            var intervals = new List<double>(frameTicks.Count);
            for (int i = 1; i < frameTicks.Count; i++)
            {
                double dt = (frameTicks[i] - frameTicks[i - 1]) * freqMs;
                bool isEndEcho = i == frameTicks.Count - 1 && dt < 0.45 && (framePhases[i] >= 0.999 || framePhases[i] <= 0.001);
                if (!isEndEcho) intervals.Add(dt);
            }
            var sorted = intervals.OrderBy(x => x).ToArray();
            var sortedCb = cbMs.OrderBy(x => x).ToArray();
            double firstMs = frameTicks.Count > 0 ? (frameTicks[0] - triggerTick) * freqMs : 0;
            double totalMs = frameTicks.Count > 0 ? (frameTicks[^1] - triggerTick) * freqMs : 0;

            return new
            {
                kind = "cycle",
                scenario,
                action = openAction ? "open" : "close",
                idleSec,
                rawFrames = frameTicks.Count,
                intervalsCount = sorted.Length,
                firstFrameMs = Math.Round(firstMs, 3),
                totalMs = Math.Round(totalMs, 3),
                minMs = Math.Round(sorted.FirstOrDefault(), 3),
                p50Ms = Math.Round(Percentile(sorted, 0.50), 3),
                meanMs = Math.Round(sorted.Length == 0 ? 0 : sorted.Average(), 3),
                p95Ms = Math.Round(Percentile(sorted, 0.95), 3),
                p99Ms = Math.Round(Percentile(sorted, 0.99), 3),
                maxMs = Math.Round(sorted.LastOrDefault(), 3),
                over6_94 = sorted.Count(x => x > 6.944),
                over8_33 = sorted.Count(x => x > 8.333),
                over16_67 = sorted.Count(x => x > 16.667),
                over25 = sorted.Count(x => x > 25.0),
                cbP50Ms = Math.Round(Percentile(sortedCb, 0.50), 3),
                cbP95Ms = Math.Round(Percentile(sortedCb, 0.95), 3),
                cbMaxMs = Math.Round(sortedCb.LastOrDefault(), 3),
                allocatedBytes = allocBytes,
                gcDelta,
                cpuMs = Math.Round(cpuMs, 3),
                workingSetMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 2),
                intervalsMs = intervals.Select(x => Math.Round(x, 2)).ToArray()
            };
        }

        async Task RunIdleRound(int idleSeconds, bool dockShown)
        {
            if (dockShown)
            {
                await EnsureDockVisible();
                Native.GetWindowRect(c.Dock.Handle, out var dr);
                await MovePointer((dr.Left + dr.Right) / 2, dr.Bottom - (int)Math.Ceiling(6 * c.Scale));
                await Task.Delay(250);
            }
            else
            {
                await ParkPointer();
                await Until(() => !c.Dock.IsVisible);
                await Task.Delay(200);
            }

            uint inputBefore = LastInputTick();
            proc.Refresh();
            double cpuBefore = proc.TotalProcessorTime.TotalMilliseconds;
            long wsBefore = proc.WorkingSet64;
            var sw = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(idleSeconds));
            proc.Refresh();
            uint inputAfter = LastInputTick();

            records.Add(new
            {
                kind = "idle-period",
                idleSeconds,
                dockShown,
                actualSeconds = Math.Round(sw.Elapsed.TotalSeconds, 3),
                idleCpuMs = Math.Round(proc.TotalProcessorTime.TotalMilliseconds - cpuBefore, 2),
                wsStartMb = Math.Round(wsBefore / (1024.0 * 1024.0), 2),
                wsEndMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 2),
                externalInputUnchanged = inputBefore == inputAfter
            });
            Save();

            await EnsureDockVisible();
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                records.Add(await MeasureCycle($"idle-{idleSeconds}s-attempt{attempt}", true, useMouseClick: true, idleSec: attempt == 1 ? idleSeconds : 0));
                await Task.Delay(120);
                records.Add(await MeasureCycle($"idle-{idleSeconds}s-close{attempt}", false, useMouseClick: true));
                await Task.Delay(150);
            }
            Save();
        }

        try
        {
            bg = new Window
            {
                Title = "MacDock Genie Benchmark Backdrop",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = false,
                Left = c.WorkArea.Left / c.Scale,
                Top = c.WorkArea.Top / c.Scale,
                Width = c.WorkArea.Width / c.Scale,
                Height = c.WorkArea.Height / c.Scale,
                Background = new LinearGradientBrush(Color.FromRgb(28, 42, 74), Color.FromRgb(18, 22, 34), 35)
            };
            bg.Show();
            await EnsureDockVisible();
            await Task.Delay(200);

            // 1. Cold first open (right after startup before full icon wait)
            records.Add(await MeasureCycle("cold-first-open", true, useMouseClick: true));
            await Task.Delay(150);
            records.Add(await MeasureCycle("cold-first-close", false, useMouseClick: true));
            Save();

            // Wait for all drawer icons to settle before continuous & idle benchmarks
            var icons = c.Store.Data.Entries.Where(e => e.InDrawer || e.Pinned).Select(e => c.Icon(e.Path, c.Layout.DrawerIcon)).ToArray();
            var iconDl = Stopwatch.StartNew();
            while (icons.Any(i => i.Loading) && iconDl.ElapsedMilliseconds < 15000) await Task.Delay(30);

            // 2. Continuous 10x open + close (20 animations)
            for (int i = 1; i <= 10; i++)
            {
                records.Add(await MeasureCycle($"continuous-{i}", true, useMouseClick: true));
                await Task.Delay(90);
                records.Add(await MeasureCycle($"continuous-{i}", false, useMouseClick: true));
                await Task.Delay(90);
            }
            Save();

            // 3. Real idle intervals: 12s, 20s, 35s, 60s
            await RunIdleRound(12, dockShown: false);
            await RunIdleRound(20, dockShown: true);
            await RunIdleRound(35, dockShown: false);
            await RunIdleRound(60, dockShown: true);

            // 4. Mid-flight reversal (open -> reverse close -> reverse open -> settle)
            await EnsureDockVisible();
            var revTicks = new List<long>();
            var revPhases = new List<double>();
            bool triggeredClose = false, triggeredReopen = false;
            c.DrawerAnimationFrame = (p, _, _, _) =>
            {
                revTicks.Add(Stopwatch.GetTimestamp());
                revPhases.Add(p);
                if (!triggeredClose && p >= 0.38)
                {
                    triggeredClose = true;
                    c.CloseDrawer(keepDock: true, force: true);
                }
                else if (triggeredClose && !triggeredReopen && p <= 0.45)
                {
                    triggeredReopen = true;
                    c.ToggleDrawer();
                }
            };
            await ClickGateway();
            await Until(() => c.Drawer is { IsVisible: true, IsDeforming: false } && !c.DrawerMotionRunning, 5000);
            c.DrawerAnimationFrame = null;
            var revIntervals = revTicks.Zip(revTicks.Skip(1), (a, b) => (b - a) * freqMs).Where(x => x > 0.45).OrderBy(x => x).ToArray();
            records.Add(new
            {
                kind = "mid-reversal",
                triggeredClose,
                triggeredReopen,
                settledOpen = c.Drawer is { IsVisible: true, IsDeforming: false } && !c.DrawerMotionRunning,
                frames = revTicks.Count,
                p50Ms = Math.Round(Percentile(revIntervals, 0.50), 3),
                p95Ms = Math.Round(Percentile(revIntervals, 0.95), 3),
                maxMs = Math.Round(revIntervals.LastOrDefault(), 3),
                over6_94 = revIntervals.Count(x => x > 6.944),
                over16_67 = revIntervals.Count(x => x > 16.667)
            });
            if (!triggeredClose || !triggeredReopen || c.Drawer?.IsDeforming != false)
                failures.Add("mid-reversal failed");
            await MeasureCycle("post-reversal-close", false, useMouseClick: true);
            Save();

            // 5. Rapid consecutive clicks (10 clicks, ~35ms apart)
            await EnsureDockVisible();
            var rapidTicks = new List<long>();
            c.DrawerAnimationFrame = (_, _, _, _) => rapidTicks.Add(Stopwatch.GetTimestamp());
            for (int i = 0; i < 10; i++)
            {
                await ClickGateway();
                await Task.Delay(35);
            }
            await Until(() => !c.DrawerMotionRunning, 5000);
            c.DrawerAnimationFrame = null;
            var rapidIntervals = rapidTicks.Zip(rapidTicks.Skip(1), (a, b) => (b - a) * freqMs).Where(x => x > 0.45).OrderBy(x => x).ToArray();
            bool rapidSettledClean = !c.DrawerMotionRunning && c.Drawer?.IsDeforming != true && Anim.ActiveTimers == 0;
            records.Add(new
            {
                kind = "rapid-clicks",
                clicks = 10,
                finalDrawerVisible = c.Drawer?.IsVisible == true,
                settledClean = rapidSettledClean,
                frames = rapidTicks.Count,
                p50Ms = Math.Round(Percentile(rapidIntervals, 0.50), 3),
                p95Ms = Math.Round(Percentile(rapidIntervals, 0.95), 3),
                maxMs = Math.Round(rapidIntervals.LastOrDefault(), 3),
                over6_94 = rapidIntervals.Count(x => x > 6.944),
                over16_67 = rapidIntervals.Count(x => x > 16.667)
            });
            if (!rapidSettledClean) failures.Add("rapid-clicks failed to settle cleanly");
            if (c.Drawer?.IsVisible == true) await MeasureCycle("post-rapid-close", false, useMouseClick: true);
            Save();

            // 6. Window states & appearances (Dock Resident vs AutoHide; Light vs Dark)
            c.AutoHideDock = false;
            c.CheckFullscreen();
            await Task.Delay(250);
            records.Add(await MeasureCycle("state-dock-resident", true, useMouseClick: false));
            await Task.Delay(120);
            records.Add(await MeasureCycle("state-dock-resident", false, useMouseClick: false));
            c.AutoHideDock = true;
            c.CheckFullscreen();
            await Task.Delay(250);

            string savedApp = c.Store.Data.Appearance;
            foreach (string theme in new[] { "light", "dark" })
            {
                c.SetAppearance(theme);
                await Task.Delay(180);
                await EnsureDockVisible();
                records.Add(await MeasureCycle($"theme-{theme}", true, useMouseClick: true));
                await Task.Delay(100);
                records.Add(await MeasureCycle($"theme-{theme}", false, useMouseClick: true));
            }
            c.SetAppearance(savedApp);
            await Task.Delay(150);

            // 7. Visual proof captures across open, reverse, and close poses
            async Task CapturePose(double target, bool opening, string shotName)
            {
                await EnsureDockVisible();
                bool held = false;
                c.DrawerAnimationFrame = (p, _, _, _) =>
                {
                    if (!held && (opening ? p >= target : p <= target))
                    {
                        held = true;
                        c.PauseDrawerMotion();
                    }
                };
                if (opening) c.ToggleDrawer();
                else c.CloseDrawer(keepDock: true, force: true);
                await Until(() => held, 3000);
                c.DrawerAnimationFrame = null;
                await Task.Delay(60);
                CaptureArea(shotName);
                c.ResumeDrawerMotion();
                await Until(() => !c.DrawerMotionRunning, 3000);
            }

            await CapturePose(0.22, true, "open-0.22");
            await CapturePose(0.50, false, "close-0.50");
            await CapturePose(0.48, true, "open-0.48");
            await CapturePose(0.50, false, "close-0.50-after");
            await CapturePose(0.72, true, "open-0.72");
            await Task.Delay(60);
            CaptureArea("open-settled-1.00");
            c.CloseDrawer(collapseDock: true, force: true);
            await Until(() => !c.DrawerMotionRunning && c.Drawer?.IsVisible != true, 3000);
        }
        catch (Exception ex)
        {
            failures.Add(ex.ToString());
            Environment.ExitCode = 1;
        }
        finally
        {
            c.DrawerTiming = null;
            c.DrawerAnimationFrame = null;
            bg?.Close();
            if (failures.Count > 0) Environment.ExitCode = 1;
            Save();
            Application.Current.Shutdown(Environment.ExitCode);
        }
    }
}
#endif
