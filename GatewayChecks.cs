using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace MacDock;

internal static class GatewayChecks
{
    internal static async Task Run(Controller owner)
    {
        string output = Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var results = new List<object>();
        var frames = new List<object>();
        var clock = Stopwatch.StartNew();
        int observedClicks = 0;
        double worstAnchorError = 0;
        double worstPhaseError = 0;
        var background = new Window {
            Title = "MacDock gateway capture", WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, Width = owner.WorkArea.Width / owner.Scale,
            Height = owner.WorkArea.Height / owner.Scale, Left = 0, Top = 0,
            Background = new SolidColorBrush(Color.FromRgb(30, 58, 120))
        };
        void Check(string name, bool pass, object? detail = null)
        {
            results.Add(new { name, pass, detail });
            if (!pass) throw new InvalidOperationException(name);
        }
        void Marker(string name) => frames.Add(new { name, time = clock.Elapsed.TotalMilliseconds, phase = owner.DrawerProgress });
        void Shot(string name)
        {
            var screen = owner.Screen.Bounds;
            using var picture = new System.Drawing.Bitmap(screen.Width, screen.Height);
            using (var g = System.Drawing.Graphics.FromImage(picture))
                g.CopyFromScreen(screen.Location, System.Drawing.Point.Empty, picture.Size);
            picture.Save(Path.Combine(output, name + ".png"));
        }
        async Task Click(string name)
        {
            Marker(name);
            var p = owner.Dock.GatewayCenter;
            Native.MovePointer(p.X, p.Y);
            var hit = Native.WindowFromPoint(p);
            Check(name + " uses the visible Dock hit target", hit == owner.Dock.Handle,
                new { p.X, p.Y, hit = (long)hit, dock = (long)owner.Dock.Handle, owner.Dock.IsVisible });
            Native.TestButton(true);
            await Task.Delay(18);
            Native.TestButton(false);
        }
        async Task WaitForPhase(double low, double high)
        {
            for (int i = 0; i < 30; i++)
            {
                if (owner.DrawerProgress > low && owner.DrawerProgress < high && owner.DrawerMotionRunning) return;
                await Task.Delay(8);
            }
            throw new InvalidOperationException("The live transition did not reach the requested intermediate phase");
        }
        bool Closed() => owner.Drawer?.IsVisible == false && owner.Dock.GatewayPhase == 0 && !owner.DrawerMotionRunning;
        bool Open() => owner.Drawer?.IsVisible == true && owner.Dock.GatewayPhase == 1 && !owner.DrawerMotionRunning;
        try
        {
            background.Show();
            owner.Busy = true;
            owner.Expand();
            await Task.Delay(410);
            var button = owner.Dock.GatewayCenter;
            Native.MovePointer(button.X, button.Y);
            await Task.Delay(100);
            owner.Busy = false;
            InteractionChecks.Record = (name, _) =>
            {
                if (name != "gateway-click") return;
                observedClicks++;
                Marker("actual-gateway-release");
            };
            owner.DrawerAnimationFrame = (phase, _, _, _) =>
            {
                var d = owner.Drawer!;
                var pose = d.CurrentPose!;
                var body = d.BodyBounds;
                worstAnchorError = Math.Max(worstAnchorError, Math.Abs(body.Left + pose.AnchorX * owner.Scale - button.X));
                worstAnchorError = Math.Max(worstAnchorError, Math.Abs(body.Top + pose.AnchorY * owner.Scale - button.Y));
                worstPhaseError = Math.Max(worstPhaseError, Math.Abs(owner.Dock.GatewayPhase - phase));
                frames.Add(new { name = "pose", time = clock.Elapsed.TotalMilliseconds, phase,
                    gatewayPhase = owner.Dock.GatewayPhase, p = new { button.X, button.Y },
                    anchorX = body.Left + pose.AnchorX * owner.Scale,
                    anchorY = body.Top + pose.AnchorY * owner.Scale });
            };

            Shot("button-rest");
            await Click("normal-open");
            await Task.Delay(520);
            Check("settled open has no complete four-pane glyph", Open());
            Shot("drawer-open");
            await Click("normal-close");
            await Task.Delay(460);
            Check("normal close restores the four panes", Closed());
            Shot("drawer-closed");

            await Click("start-opening");
            await WaitForPhase(.08, .57);
            await Click("reverse-opening");
            await Task.Delay(450);
            Check("opening interrupted by the real button ends closed", Closed());

            await Click("open-before-close-interruption");
            await Task.Delay(470);
            Check("open before reversing close", Open());
            await Click("start-closing");
            await WaitForPhase(.31, .92);
            await Click("reverse-closing");
            await Task.Delay(500);
            Check("closing interrupted by the real button ends open", Open());

            await Click("reset-before-rapid-clicks");
            await Task.Delay(450);
            Check("closed before rapid clicks", Closed());
            Marker("rapid-clicks-start");
            int clicksBeforeRapid = observedClicks;
            for (int i = 0; i < 8; i++)
            {
                await Click("rapid-" + i);
                await Task.Delay(27);
            }
            await Task.Delay(520);
            Check("all eight rapid physical gestures reach the fixed button", observedClicks - clicksBeforeRapid == 8);
            Check("eight rapid real clicks end closed", Closed());
            Shot("rapid-final");

            await Click("open-before-escape");
            await Task.Delay(470);
            Native.TestKey(27, true);
            await Task.Delay(20);
            Native.TestKey(27, false);
            await Task.Delay(420);
            Check("Escape restores button and retains Dock", Closed() && owner.Dock.IsVisible);

            owner.Store.Data.ReducedMotion = true;
            await Click("reduced-open");
            await Task.Delay(140);
            Check("reduced motion snaps both parts to open", Open());
            await Click("reduced-close");
            await Task.Delay(150);
            Check("reduced motion restores button without active frames", Closed() && Anim.ActiveTimers == 0);
            Check("drawer and glyph use the same phase with a fixed screen anchor", worstAnchorError <= 1 && worstPhaseError < .00001,
                new { worstAnchorError, worstPhaseError });
        }
        catch (Exception e) { results.Add(new { name = "exception", pass = false, detail = e.ToString() }); }
        finally
        {
            owner.DrawerAnimationFrame = null;
            InteractionChecks.Record = null;
            Native.TestButton(false);
            File.WriteAllText(Path.Combine(output, "gateway-checks.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "gateway-frames.json"), JsonSerializer.Serialize(frames));
            background.Close();
            Application.Current.Shutdown();
        }
    }
}
