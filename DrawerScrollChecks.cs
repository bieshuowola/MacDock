#if MACDOCK_EXPERIENCE_CHECKS
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock;

internal static class DrawerScrollChecks
{
    static ulong SampleBitmapRegionHash(BitmapSource bmp)
    {
        int w = bmp.PixelWidth;
        int h = bmp.PixelHeight;
        int x0 = Math.Clamp((int)(w * 0.08), 0, Math.Max(0, w - 1));
        int x1 = Math.Clamp((int)(w * 0.92), x0 + 1, w);
        int y0 = Math.Clamp((int)(h * 0.24), 0, Math.Max(0, h - 1));
        int y1 = Math.Clamp((int)(h * 0.82), y0 + 1, h);
        int rw = x1 - x0;
        int rh = y1 - y0;
        int stride = rw * 4;
        byte[] buf = new byte[stride * rh];
        bmp.CopyPixels(new Int32Rect(x0, y0, rw, rh), buf, stride, 0);
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < buf.Length; i += 4)
        {
            hash = (hash ^ buf[i]) * 1099511628211UL;
            hash = (hash ^ buf[i + 1]) * 1099511628211UL;
            hash = (hash ^ buf[i + 2]) * 1099511628211UL;
            hash = (hash ^ buf[i + 3]) * 1099511628211UL;
        }
        return hash;
    }

    static (int VisibleTiles, int RenderedIconTiles, int BlankTiles) CountVisibleTilesWithPixels(DrawerWindow drawer, BitmapSource prebaked, double scale)
    {
        if (drawer.Surface == null) return (0, 0, 0);
        var scroll = drawer.Scroller;
        var scrollTopLeft = scroll.TransformToAncestor(drawer.Surface).Transform(new Point(0, 0));
        double viewTop = scrollTopLeft.Y;
        double viewBottom = viewTop + scroll.ViewportHeight;
        int visible = 0, rendered = 0, blank = 0;

        foreach (var element in drawer.SortTiles())
        {
            if (element is not Border tile || tile.Visibility != Visibility.Visible || tile.ActualWidth <= 0 || tile.ActualHeight <= 0)
                continue;
            if (tile.Child is not StackPanel panel) continue;
            var iconGrid = panel.Children.OfType<Grid>().FirstOrDefault();
            if (iconGrid == null || iconGrid.ActualWidth <= 0 || iconGrid.ActualHeight <= 0) continue;

            var iconRect = iconGrid.TransformToAncestor(drawer.Surface).TransformBounds(new Rect(0, 0, iconGrid.ActualWidth, iconGrid.ActualHeight));
            if (iconRect.Top < viewTop + 2 || iconRect.Bottom > viewBottom - 2) continue;
            visible++;

            bool hasArrangedImg = iconGrid.Children.Count > 0 && iconGrid.Children[0] is IconViewHost host && host.HasRenderedImage && host.HasArrangedImage;
            int x0 = Math.Clamp((int)Math.Floor(iconRect.Left * scale), 0, Math.Max(0, prebaked.PixelWidth - 1));
            int y0 = Math.Clamp((int)Math.Floor(iconRect.Top * scale), 0, Math.Max(0, prebaked.PixelHeight - 1));
            int x1 = Math.Clamp((int)Math.Ceiling(iconRect.Right * scale), x0 + 1, prebaked.PixelWidth);
            int y1 = Math.Clamp((int)Math.Ceiling(iconRect.Bottom * scale), y0 + 1, prebaked.PixelHeight);
            int rw = x1 - x0;
            int rh = y1 - y0;
            int stride = rw * 4;
            byte[] buf = new byte[stride * rh];
            prebaked.CopyPixels(new Int32Rect(x0, y0, rw, rh), buf, stride, 0);
            byte minB = 255, maxB = 0, minG = 255, maxG = 0, minR = 255, maxR = 0, minA = 255, maxA = 0;
            for (int i = 0; i < buf.Length; i += 4)
            {
                if (buf[i] < minB) minB = buf[i]; if (buf[i] > maxB) maxB = buf[i];
                if (buf[i + 1] < minG) minG = buf[i + 1]; if (buf[i + 1] > maxG) maxG = buf[i + 1];
                if (buf[i + 2] < minR) minR = buf[i + 2]; if (buf[i + 2] > maxR) maxR = buf[i + 2];
                if (buf[i + 3] < minA) minA = buf[i + 3]; if (buf[i + 3] > maxA) maxA = buf[i + 3];
            }
            int spread = Math.Max(Math.Max(maxB - minB, maxG - minG), Math.Max(maxR - minR, maxA - minA));
            if (hasArrangedImg && spread >= 12) rendered++;
            else blank++;
        }
        return (visible, rendered, blank);
    }

    internal static async Task Run(Controller controller)
    {
        string output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        Directory.CreateDirectory(output);
        var observations=new List<object>();
        void Assert(bool cond, string message)
        {
            if (!cond) throw new InvalidOperationException(message);
        }
        async Task Until(Func<bool> cond, int timeoutMs = 4500)
        {
            var dl = Stopwatch.StartNew();
            while (!cond() && dl.ElapsedMilliseconds < timeoutMs) await Task.Delay(8);
            if (!cond()) throw new TimeoutException("Timeout waiting condition.");
        }
        try
        {
            BitmapSource? startupFirstBmp = null;
            long startupToggleTick = Stopwatch.GetTimestamp();
            double startupFirstRenderMs = 0;
            double freqMs = 1000.0 / Stopwatch.Frequency;
            controller.DrawerAnimationFrame = (phase, _, _, _) =>
            {
                if (startupFirstBmp == null && controller.Drawer != null)
                    startupFirstBmp = controller.Drawer.GenieBoundBitmap;
                if (startupFirstRenderMs == 0 && phase > 0.0001)
                    startupFirstRenderMs = (Stopwatch.GetTimestamp() - startupToggleTick) * freqMs;
            };
            try
            {
                controller.ToggleDrawer();
                await Until(() => controller.Drawer is { IsVisible: true, IsDeforming: false } && !controller.DrawerMotionRunning);
            }
            finally { controller.DrawerAnimationFrame = null; }
            var drawer=controller.Drawer!;
            var scroll=drawer.Scroller;
            Assert(startupFirstBmp != null, "normal-startup: expected first-frame GenieBoundBitmap");
            var startupDiag = drawer.LastIconDiagnostics;
            var startupPixels = CountVisibleTilesWithPixels(drawer, startupFirstBmp!, controller.Scale);
            bool startupIconsPass = startupDiag.Total > 0 && startupDiag.BlankPlaceholders == 0 && startupDiag.UnarrangedImages == 0 &&
                startupPixels.VisibleTiles > 0 && startupPixels.BlankTiles == 0;
            observations.Add(new
            {
                phase = "icon-normal-startup",
                pass = startupIconsPass,
                totalIcons = startupDiag.Total,
                readyIcons = startupDiag.Ready,
                blankIcons = startupDiag.BlankPlaceholders,
                unarrangedImages = startupDiag.UnarrangedImages,
                syncLoaded = startupDiag.LoadedSynchronously,
                prepareMs = startupDiag.PrepareMs,
                cacheHits = startupDiag.CacheHits,
                totalLoads = controller.IconLoads,
                visibleTiles = startupPixels.VisibleTiles,
                renderedIconTiles = startupPixels.RenderedIconTiles,
                prebakedBlankTiles = startupPixels.BlankTiles,
                firstRenderMs = Math.Round(startupFirstRenderMs, 2)
            });
            Assert(startupIconsPass, $"icon-normal-startup failed: blank={startupDiag.BlankPlaceholders}, unarranged={startupDiag.UnarrangedImages}, prebakedBlank={startupPixels.BlankTiles}");
            if(scroll.ScrollableHeight<50)throw new InvalidOperationException("Test requires the real drawer to overflow.");
            var point=scroll.PointToScreen(new Point(scroll.ActualWidth*.6,scroll.ActualHeight*.45));
            Native.MovePointer((int)point.X,(int)point.Y);
            await Task.Delay(120);
            var offsets=new List<(double Time,double Offset)>();
            var frames=new List<double>();
            var watch=Stopwatch.StartNew();
            TimeSpan? lastRender=null;
            double last=0;
            EventHandler render=(_,e)=>
            {
                if(e is not RenderingEventArgs tick||lastRender==tick.RenderingTime)return;
                lastRender=tick.RenderingTime;
                double now=watch.Elapsed.TotalMilliseconds;
                if(last>0)frames.Add(now-last);
                last=now;
                offsets.Add((now,scroll.VerticalOffset));
            };
            CompositionTarget.Rendering+=render;
            try
            {
                for(int i=0;i<65;i++)
                {
                    Native.TestWheel(i%10<5?-120:120);
                    await Task.Delay(13);
                }
                await Task.Delay(230);
            }
            finally { CompositionTarget.Rendering-=render; }
            double Quantile(double p)=>frames.Count==0?0:frames.Order().ElementAt((int)Math.Ceiling(p*frames.Count)-1);
            observations.Add(new{phase="wheel",pass=true,count=frames.Count,p95=Quantile(.95),p99=Quantile(.99),max=Quantile(1),longFrames=frames.Count(v=>v>12.2),changes=offsets.Zip(offsets.Skip(1)).Count(p=>Math.Abs(p.First.Offset-p.Second.Offset)>.01),extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight,screen=controller.Screen.DeviceName,scale=controller.Scale,wheel=drawer.LastWheelInput,frames});
            // Move cursor away from drawer tiles so accidental hover underline/shadow doesn't alter reference bitmap hashes.
            Native.MovePointer(controller.WorkArea.Left + 40, controller.WorkArea.Top + 40);
            await Task.Delay(180);

            async Task<(double FirstRenderMs, double MaxFrameMs, ulong FirstHash, int FrameCount)> VerifyReopen(
                string name, double expectedOffset, ulong? forbiddenHash, ulong? requiredHash, string? pauseShotName = null)
            {
                var frameOffsets = new List<double>();
                var framePrebakedOffsets = new List<double>();
                var frameHashes = new List<ulong>();
                var frameBoundMatches = new List<bool>();
                var renderTicks = new List<long>();
                BitmapSource? cachedBmp = null;
                ulong cachedHash = 0;
                bool capturedMidShot = false;
                long toggleTick = Stopwatch.GetTimestamp();
                double freqMs = 1000.0 / Stopwatch.Frequency;

                controller.DrawerAnimationFrame = (phase, _, _, _) =>
                {
                    frameOffsets.Add(scroll.VerticalOffset);
                    framePrebakedOffsets.Add(drawer.PrebakedScrollOffset);
                    var pre = drawer.PrebakedBitmap;
                    var bound = drawer.GenieBoundBitmap;
                    frameBoundMatches.Add(pre != null && ReferenceEquals(pre, bound));
                    if (!ReferenceEquals(bound, cachedBmp))
                    {
                        cachedBmp = bound;
                        cachedHash = bound != null ? SampleBitmapRegionHash(bound) : 0;
                    }
                    frameHashes.Add(cachedHash);
                    if (phase > 0.0001) renderTicks.Add(Stopwatch.GetTimestamp());
                    if (pauseShotName != null && !capturedMidShot && phase >= 0.42 && phase < 0.85)
                    {
                        capturedMidShot = true;
                        controller.PauseDrawerMotion();
                    }
                };
                try
                {
                    controller.ToggleDrawer();
                    if (pauseShotName != null)
                    {
                        await Until(() => capturedMidShot || (drawer.IsVisible && !drawer.IsDeforming && !controller.DrawerMotionRunning));
                        if (capturedMidShot)
                        {
                            await Task.Delay(50);
                            var b = drawer.BodyBounds;
                            using var bmp = new System.Drawing.Bitmap(b.Right - b.Left, b.Bottom - b.Top);
                            using (var g = System.Drawing.Graphics.FromImage(bmp))
                                g.CopyFromScreen(b.Left, b.Top, 0, 0, bmp.Size);
                            bmp.Save(Path.Combine(output, pauseShotName));
                            controller.ResumeDrawerMotion();
                        }
                    }
                    await Until(() => drawer.IsVisible && !drawer.IsDeforming && !controller.DrawerMotionRunning);
                }
                finally { controller.DrawerAnimationFrame = null; }

                Assert(frameOffsets.Count >= 2, $"{name}: expected animation frames, got {frameOffsets.Count}");
                double firstFrameOffset = frameOffsets[0];
                double firstPrebakedOffset = framePrebakedOffsets[0];
                ulong firstHash = frameHashes[0];
                bool allOffsetsValid = frameOffsets.All(o => Math.Abs(o - expectedOffset) < 1.0);
                bool allPrebakedOffsetsValid = framePrebakedOffsets.All(o => Math.Abs(o - expectedOffset) < 0.5);
                bool allBoundMatches = frameBoundMatches.All(b => b);
                bool allHashesValid = frameHashes.All(h => h != 0 && (forbiddenHash == null || h != forbiddenHash.Value) && (requiredHash == null || h == requiredHash.Value));
                double settledOffset = scroll.VerticalOffset;
                bool settledValid = Math.Abs(settledOffset - expectedOffset) < 1.0;

                double firstRenderMs = renderTicks.Count > 0 ? (renderTicks[0] - toggleTick) * freqMs : 0;
                double maxFrameMs = 0;
                for (int i = 1; i < renderTicks.Count; i++)
                    maxFrameMs = Math.Max(maxFrameMs, (renderTicks[i] - renderTicks[i - 1]) * freqMs);
                bool frameTimingValid = pauseShotName != null || maxFrameMs < 25.0;

                bool pass = allOffsetsValid && allPrebakedOffsetsValid && allBoundMatches && allHashesValid && settledValid && frameTimingValid;
                observations.Add(new
                {
                    phase = name,
                    pass,
                    expectedOffset,
                    firstFrameOffset,
                    firstPrebakedOffset,
                    settledOffset,
                    firstHash = $"0x{firstHash:X16}",
                    distinctHashesDuringOpen = frameHashes.Distinct().Count(),
                    allBoundMatches,
                    allOffsetsValid,
                    allPrebakedOffsetsValid,
                    allHashesValid,
                    frameTimingValid,
                    firstRenderMs = Math.Round(firstRenderMs, 2),
                    maxFrameMs = Math.Round(maxFrameMs, 2),
                    frameCount = frameOffsets.Count
                });
                Assert(pass, $"{name} failed: expectedOffset={expectedOffset}, firstFrameOffset={firstFrameOffset}, firstPrebakedOffset={firstPrebakedOffset}, settledOffset={settledOffset}, allBoundMatches={allBoundMatches}, allHashesValid={allHashesValid}, frameTimingValid={frameTimingValid}, maxFrameMs={maxFrameMs:F2}");
                return (firstRenderMs, maxFrameMs, firstHash, frameOffsets.Count);
            }

            // 1. Top position (with idle bake before close)
            scroll.ScrollToTop();
            await Task.Delay(400);
            Assert(scroll.VerticalOffset < 0.5, "Expected top offset");
            controller.CloseDrawer(keepDock: true, force: true);
            await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
            var (_, _, hashTop, _) = await VerifyReopen("top-reopen", 0, forbiddenHash: null, requiredHash: null);

            // 2. Middle position (wait >350ms so scrollBakeIdle prebakes before close - exact bug repro!)
            double midOffset = Math.Round(scroll.ScrollableHeight * 0.5);
            scroll.SetImmediateOffset(midOffset);
            await Task.Delay(420);
            Assert(Math.Abs(drawer.PrebakedScrollOffset - midOffset) < 0.5, $"Idle prebake did not match midOffset: {drawer.PrebakedScrollOffset} vs {midOffset}");
            controller.CloseDrawer(keepDock: true, force: true);
            await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
            var (_, _, hashMid, _) = await VerifyReopen("mid-reopen-after-idle-bake", midOffset, forbiddenHash: hashTop, requiredHash: null, pauseShotName: "drawer-reopen-mid-phase.png");
            Assert(hashMid != hashTop, "Mid bitmap hash must differ from top bitmap hash");

            // 3. Bottom position (immediate close before 280ms scrollBakeIdle fires)
            scroll.ScrollToBottom();
            await Task.Delay(50);
            double botOffset = scroll.VerticalOffset;
            Assert(botOffset > midOffset + 20, $"Expected bottom offset > midOffset, got {botOffset}");
            controller.CloseDrawer(keepDock: true, force: true);
            await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
            var (_, _, hashBot, _) = await VerifyReopen("bottom-reopen-immediate-close", botOffset, forbiddenHash: hashTop, requiredHash: null, pauseShotName: "drawer-reopen-bottom-phase.png");
            Assert(hashBot != hashTop && hashBot != hashMid, "Bottom bitmap hash must differ from top and mid hashes");

            // Save settled bottom screenshot
            await Task.Delay(80);
            using(var bitmap=new System.Drawing.Bitmap(drawer.BodyBounds.Right-drawer.BodyBounds.Left,drawer.BodyBounds.Bottom-drawer.BodyBounds.Top))
            {
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(drawer.BodyBounds.Left,drawer.BodyBounds.Top,0,0,bitmap.Size);
                bitmap.Save(Path.Combine(output,"drawer-bottom.png"));
            }

            // 4. Rapid consecutive close & reopen cycles (4x, including mid-flight reversal)
            for (int r = 1; r <= 4; r++)
            {
                controller.CloseDrawer(keepDock: true, force: true);
                if (r <= 2)
                {
                    await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
                }
                else
                {
                    // Mid-flight close -> reopen while closing
                    await Until(() => controller.DrawerProgress <= 0.45);
                }
                await VerifyReopen($"rapid-reopen-{r}", botOffset, forbiddenHash: hashTop, requiredHash: hashBot);
            }

            // 5. Hidden-state Render() + long idle (3s) + reopen + latency/frame check
            scroll.SetImmediateOffset(midOffset);
            await Task.Delay(120);
            controller.CloseDrawer(keepDock: true, force: true);
            await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
            drawer.Render();
            await Task.Delay(3000);
            var (idleFirstMs, idleMaxMs, _, _) = await VerifyReopen("idle-3s-after-hidden-render", midOffset, forbiddenHash: hashTop, requiredHash: hashMid);
            Assert(idleFirstMs < 80, $"First render latency too high after idle: {idleFirstMs}ms");
            observations.Add(new { phase = "reopen", before = midOffset, after = scroll.VerticalOffset, pass = Math.Abs(midOffset - scroll.VerticalOffset) < 1, idleFirstMs, idleMaxMs });

            // 6. Comprehensive icon readiness & sleep/resume verification
            async Task CheckIconOpenCycle(string cycleName, bool expectZeroLoads, string? captureMidShot = null)
            {
                BitmapSource? firstBmp = null;
                var cycleBitmaps = new List<BitmapSource>();
                bool capturedShot = false;
                long toggleTick = Stopwatch.GetTimestamp();
                double firstMs = 0;
                long loadsBefore = controller.IconLoads;
                long hitsBefore = controller.IconCacheHits;
                controller.DrawerAnimationFrame = (phase, _, _, _) =>
                {
                    if (drawer.GenieBoundBitmap is { } bmp)
                    {
                        firstBmp ??= bmp;
                        if (cycleBitmaps.Count == 0 || !ReferenceEquals(cycleBitmaps[^1], bmp))
                            cycleBitmaps.Add(bmp);
                    }
                    if (firstMs == 0 && phase > 0.0001)
                        firstMs = (Stopwatch.GetTimestamp() - toggleTick) * freqMs;
                    if (captureMidShot != null && !capturedShot && phase >= 0.42 && phase < 0.85)
                    {
                        capturedShot = true;
                        controller.PauseDrawerMotion();
                    }
                };
                try
                {
                    controller.ToggleDrawer();
                    if (captureMidShot != null)
                    {
                        await Until(() => capturedShot || (drawer.IsVisible && !drawer.IsDeforming && !controller.DrawerMotionRunning));
                        if (capturedShot)
                        {
                            await Task.Delay(50);
                            var b = drawer.BodyBounds;
                            using var bmp = new System.Drawing.Bitmap(b.Right - b.Left, b.Bottom - b.Top);
                            using (var g = System.Drawing.Graphics.FromImage(bmp))
                                g.CopyFromScreen(b.Left, b.Top, 0, 0, bmp.Size);
                            bmp.Save(Path.Combine(output, captureMidShot));
                            controller.ResumeDrawerMotion();
                        }
                    }
                    await Until(() => drawer.IsVisible && !drawer.IsDeforming && !controller.DrawerMotionRunning);
                }
                finally { controller.DrawerAnimationFrame = null; }

                Assert(firstBmp != null, $"{cycleName}: first-frame GenieBoundBitmap missing");
                var diag = drawer.LastIconDiagnostics;
                var pxCheck = CountVisibleTilesWithPixels(drawer, firstBmp!, controller.Scale);
                long loadsDelta = controller.IconLoads - loadsBefore;
                long hitsDelta = controller.IconCacheHits - hitsBefore;
                bool pass = diag.Total > 0 && diag.BlankPlaceholders == 0 && diag.UnarrangedImages == 0 &&
                    pxCheck.VisibleTiles > 0 && pxCheck.BlankTiles == 0 &&
                    (!expectZeroLoads || (loadsDelta == 0 && diag.LoadedSynchronously == 0));
                observations.Add(new
                {
                    phase = cycleName,
                    pass,
                    totalIcons = diag.Total,
                    readyIcons = diag.Ready,
                    blankIcons = diag.BlankPlaceholders,
                    unarrangedImages = diag.UnarrangedImages,
                    syncLoaded = diag.LoadedSynchronously,
                    prepareMs = diag.PrepareMs,
                    cacheHitsDelta = hitsDelta,
                    loadsDelta,
                    visibleTiles = pxCheck.VisibleTiles,
                    renderedIconTiles = pxCheck.RenderedIconTiles,
                    prebakedBlankTiles = pxCheck.BlankTiles,
                    distinctBitmaps = cycleBitmaps.Count,
                    firstRenderMs = Math.Round(firstMs, 2)
                });
                Assert(pass, $"{cycleName} failed: blank={diag.BlankPlaceholders}, unarranged={diag.UnarrangedImages}, prebakedBlank={pxCheck.BlankTiles}, loadsDelta={loadsDelta}, syncLoaded={diag.LoadedSynchronously}");
            }

            // 6a. Continuous open (5x)
            for (int cIdx = 1; cIdx <= 5; cIdx++)
            {
                controller.CloseDrawer(keepDock: true, force: true);
                await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
                await CheckIconOpenCycle($"icon-continuous-{cIdx}", expectZeroLoads: true);
            }

            // 6b. Long idle (2.5s) then open
            controller.CloseDrawer(keepDock: true, force: true);
            await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
            await Task.Delay(2500);
            await CheckIconOpenCycle("icon-long-idle", expectZeroLoads: true);

            // 6c. Random intervals (4x)
            foreach (int delayMs in new[] { 35, 190, 15, 480 })
            {
                controller.CloseDrawer(keepDock: true, force: true);
                await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
                await Task.Delay(delayMs);
                await CheckIconOpenCycle($"icon-random-{delayMs}ms", expectZeroLoads: true);
            }

            // 6d. Multiple system sleep/resume simulations (5x)
            string savedApp = controller.Store.Data.Appearance;
            try
            {
                for (int rIdx = 1; rIdx <= 5; rIdx++)
                {
                    controller.CloseDrawer(keepDock: true, force: true);
                    await Until(() => !drawer.IsVisible && !controller.DrawerMotionRunning);
                    if (rIdx == 4)
                    {
                        controller.SetAppearance(savedApp == "dark" ? "light" : "dark");
                        await Task.Delay(120);
                        controller.SetAppearance(savedApp);
                        await Task.Delay(60);
                    }
                    else if (rIdx == 5)
                    {
                        drawer.Render();
                    }
                    controller.TestResumeLifecycle();
                    int waitAfterResume = rIdx switch { 1 => 20, 2 => 140, 3 => 450, 4 => 90, _ => 40 };
                    await Task.Delay(waitAfterResume);
                    await CheckIconOpenCycle($"icon-resume-{rIdx}", expectZeroLoads: false,
                        captureMidShot: rIdx == 5 ? "drawer-resume-open-phase.png" : null);
                }
            }
            finally
            {
                if (controller.Store.Data.Appearance != savedApp)
                {
                    controller.SetAppearance(savedApp);
                    await Task.Delay(120);
                }
            }

            if(Environment.GetEnvironmentVariable("MACDOCK_SCROLL_INTERACTION_CHECK")=="1")
            {
                scroll.ScrollToBottom();
                await Task.Delay(240);
                double before = scroll.VerticalOffset;
                static T? Find<T>(DependencyObject root) where T:DependencyObject
                {
                    for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                    {
                        var child=VisualTreeHelper.GetChild(root,i);
                        if(child is T match)return match;
                        if(Find<T>(child) is {} nested)return nested;
                    }
                    return null;
                }
                var search=Find<TextBox>(drawer)??throw new InvalidOperationException("Search editor not found.");
                search.Text="MacDock-QA-NoMatch-1ECD0B";await Task.Delay(120);
                bool filtered=scroll.ScrollableHeight==0&&Math.Abs(scroll.VerticalOffset)<.5;
                search.Clear();await Task.Delay(180);
                bool filterPass = filtered && Math.Abs(before - scroll.VerticalOffset) < 1;
                observations.Add(new{phase="filter-clear",pass=filterPass,filtered,before,after=scroll.VerticalOffset});
                Assert(filterPass, "filter-clear failed");

                var bar=scroll.OverlayBar??throw new InvalidOperationException("Overlay scrollbar not found.");
                var barCenter=bar.PointToScreen(new Point(bar.ActualWidth/2,bar.ActualHeight/2));
                var add=drawer.AddFileButton;
                double addRight=add.PointToScreen(new Point(add.ActualWidth,0)).X;
                var firstRow=drawer.SortTiles(false).Take(controller.Layout.Columns);
                double iconRight=firstRow.Select(t=>
                {
                    var icon=((StackPanel)((Border)t).Child).Children.OfType<Grid>().First();
                    return icon.PointToScreen(new Point(icon.ActualWidth,0)).X;
                }).Max();
                double midpoint=(iconRight+drawer.BodyBounds.Right)/2;
                bool aligned=Math.Abs(addRight-iconRight)<=1&&Math.Abs(barCenter.X-midpoint)<=1;
                observations.Add(new{phase="gutter",pass=aligned,bar=barCenter.X,addRight,iconRight,midpoint});
                Assert(aligned, $"gutter alignment failed: bar={barCenter.X}, midpoint={midpoint}, addRight={addRight}, iconRight={iconRight}");

                var thumb=Find<Thumb>(bar)??throw new InvalidOperationException("Scrollbar thumb not found.");
                var thumbPoint=thumb.PointToScreen(new Point(thumb.ActualWidth/2,thumb.ActualHeight/2));
                Native.MovePointer((int)thumbPoint.X,(int)thumbPoint.Y);
                await Task.Delay(180);
                using(var image=new System.Drawing.Bitmap(drawer.BodyBounds.Right-drawer.BodyBounds.Left,drawer.BodyBounds.Bottom-drawer.BodyBounds.Top))
                {
                    using var graphics=System.Drawing.Graphics.FromImage(image);
                    graphics.CopyFromScreen(drawer.BodyBounds.Left,drawer.BodyBounds.Top,0,0,image.Size);
                    image.Save(Path.Combine(output,"drawer-alignment.png"));
                }
                Native.TestButton(true);
                try
                {
                    for(int i=1;i<=8;i++)
                    {
                        Native.MovePointer((int)thumbPoint.X,(int)thumbPoint.Y-i*12);
                        await Task.Delay(14);
                    }
                }
                finally{Native.TestButton(false);}
                await Task.Delay(160);
                bool thumbPass = scroll.VerticalOffset < before - 15;
                observations.Add(new{phase="thumb",pass=thumbPass,before,after=scroll.VerticalOffset});
                Assert(thumbPass, "thumb drag failed");

                scroll.ScrollToTop();await Task.Delay(60);
                var lastEntry=(Entry)drawer.SortTiles(false).Last().Tag;
                drawer.ApplyCustomOrder();
                drawer.EnsureVisible(lastEntry.Id);await Task.Delay(90);
                var center=drawer.TileCenter(lastEntry.Id);
                var topLeft=scroll.PointToScreen(new Point());
                bool visible=center.Y>=topLeft.Y&&center.Y<topLeft.Y+scroll.ActualHeight*controller.Scale
                    &&Native.WindowFromPoint(center)==drawer.Handle;
                bool bringPass = visible && scroll.VerticalOffset > 0;
                observations.Add(new{phase="bring-into-view",pass=bringPass,offset=scroll.VerticalOffset});
                Assert(bringPass, "bring-into-view failed");

                controller.Store.Data.Mode="自定义";
                drawer.Render();
                await Task.Delay(90);
                scroll.ScrollToTop();await Task.Delay(100);
                double initialOffset=scroll.VerticalOffset;
                var sortOrder=drawer.SortTiles(false).Select(t=>((Entry)t.Tag).Id).ToArray();
                int lowerRow=controller.Layout.Columns+1;
                controller.CommitSort(sortOrder,sortOrder[0],lowerRow,false);
                await Task.Delay(160);
                bool changed=drawer.SortTiles(false).Select(t=>((Entry)t.Tag).Id).ElementAt(lowerRow)==sortOrder[0];
                bool reorderPass = changed && initialOffset < .5 && scroll.VerticalOffset < .5;
                observations.Add(new{phase="reorder-at-top",pass=reorderPass,changed,initialOffset,offset=scroll.VerticalOffset});
                Assert(reorderPass, "reorder-at-top failed");
            }
        }
        catch(Exception error)
        {
            observations.Add(new{error=error.ToString()});
            Environment.ExitCode = 1;
        }
        finally
        {
            File.WriteAllText(Path.Combine(output,"drawer-scroll.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown(Environment.ExitCode);
        }
    }
}
#endif
