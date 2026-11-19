using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MacDock;

internal enum GenieEngineMode { MacGpu, Shader, Legacy1992 }

internal static class GenieEngine
{
    internal static readonly GenieEngineMode Mode = Resolve();
    static GenieEngineMode Resolve()
    {
        string? raw = Environment.GetEnvironmentVariable("MACDOCK_GENIE_ENGINE");
        if (string.Equals(raw, "legacy-1992", StringComparison.OrdinalIgnoreCase)) return GenieEngineMode.Legacy1992;
        if (string.Equals(raw, "mac-gpu", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "macgpu", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "gpu", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "mesh", StringComparison.OrdinalIgnoreCase))
            return GenieEngineMode.MacGpu;
        return GenieEngineMode.Shader;
    }
    internal static bool IsLegacy => Mode == GenieEngineMode.Legacy1992;
    internal static bool IsShader => Mode == GenieEngineMode.Shader;
    internal static bool IsMacGpu => Mode == GenieEngineMode.MacGpu;
}

internal readonly record struct RowSample(double Left, double Right, double RoundedLeft, double RoundedRight, double Y, double Spread);

// A common, immutable silhouette for live WPF content, the host backdrop and input.
// The top unfurls first; the lower edge stays tethered to the actual drawer button.
internal sealed record GeniePose(double Width, double Height, double FrameHeight, double AnchorX, double AnchorY, double Progress)
{
    [DllImport("gdi32.dll")] static extern nint CreatePolygonRgn([In] Native.Point[] points, int count, int fill);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    internal static void SetRestRegion(nint hwnd, double radius, double scale)
    {
        if (hwnd == 0) return;
        // The layered foreground already has rounded per-pixel alpha. A GDI round
        // region is binary and loses the final right/bottom pixel as well as partial
        // coverage along its arc. It must not be the visual antialiasing boundary.
        SetWindowRgn(hwnd, 0, false);
    }

    [ThreadStatic] static Native.Point[]? inputRegionPoints;
    internal static void SetInputRegion(nint hwnd, GeniePose? pose, double scale)
    {
        if (hwnd == 0) return;
        if (pose == null) { SetWindowRgn(hwnd, 0, false); return; }
        int count = (Rows + 1) * 2;
        var edge = inputRegionPoints;
        if (edge == null || edge.Length != count) edge = inputRegionPoints = new Native.Point[count];
        for (int i = 0; i <= Rows; i++)
        {
            var r = pose.GetRow(i);
            int y = (int)Math.Round(r.Y * scale) + (i == 0 ? -(int)(8 * scale) : i == Rows ? (int)(14 * scale) : 0);
            edge[i] = new Native.Point((int)Math.Floor((r.RoundedLeft - 14) * scale), y);
            edge[edge.Length - 1 - i] = new Native.Point((int)Math.Ceiling((r.RoundedRight + 14) * scale), y);
        }
        var region = CreatePolygonRgn(edge, edge.Length, 1);
        if (region != 0 && SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
    }

    internal const int Rows = 64;
    static readonly double[] RowTTable = CreateRowTTable();
    static double[] CreateRowTTable()
    {
        var t = new double[Rows + 1];
        for (int i = 0; i <= Rows; i++) t[i] = .5 - .5 * Math.Cos(Math.PI * i / Rows);
        t[0] = 0;
        t[Rows] = 1;
        return t;
    }

    // Concentrate tessellation at rounded ends. Uniform rows left an entire 26-DIP
    // corner represented by only one or two trapezoids on a tall desktop drawer.
    internal static double RowT(int row) => (uint)row <= Rows ? RowTTable[row] : .5 - .5 * Math.Cos(Math.PI * row / Rows);
    internal static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    static long nextPoseId;
    readonly long poseId = Interlocked.Increment(ref nextPoseId);
    [ThreadStatic] static long cachedPoseId;
    [ThreadStatic] static RowSample[]? cachedRows;

    public bool Equals(GeniePose? other) =>
        other != null && Width == other.Width && Height == other.Height && FrameHeight == other.FrameHeight &&
        AnchorX == other.AnchorX && AnchorY == other.AnchorY && Progress == other.Progress;
    public override int GetHashCode() => HashCode.Combine(Width, Height, FrameHeight, AnchorX, AnchorY, Progress);

    RowSample ComputeRow(double t)
    {
        double p = Math.Clamp(Progress, 0, 1);
        double spread, top, bottom;
        if (GenieEngine.IsLegacy)
        {
            spread = Smooth((p - .38 * t) / (1 - .38 * t));
            top = AnchorY * (1 - Smooth(p / .72));
            bottom = AnchorY + (Height - AnchorY) * Smooth((p - .16) / .84);
        }
        else
        {
            // Authentic macOS Genie Cubic S-Curve funnel:
            // vertical tangents at top (t=0) and Dock anchor neck (t=1), asymmetric top unfurl + bottom unpinch.
            double topSpread = Smooth(p / 0.82);
            double botSpread = Smooth((p - 0.36) / 0.64);
            double uExp = 1.0 + 0.45 * (1.0 - p);
            double u = t <= 0 ? 0 : t >= 1 ? 1 : Math.Pow(t, uExp);
            double s = u * u * (3.0 - 2.0 * u);
            spread = topSpread + (botSpread - topSpread) * s;
            top = AnchorY * (1.0 - Smooth(p / 0.62));
            bottom = AnchorY + (Height - AnchorY) * Smooth((p - 0.14) / 0.86);
        }
        double width = 32 + (Width - 32) * spread;
        double center = Width * 0.5 + (AnchorX - Width * 0.5) * (1 - spread);
        double left = center - width * 0.5;
        double right = center + width * 0.5;
        double radius = Math.Min(26, width * .24);
        double edge = Math.Min(t, 1 - t) * Height;
        double inset = edge < radius ? radius - Math.Sqrt(Math.Max(0, radius * radius - (radius - edge) * (radius - edge))) : 0;
        double y = top + Math.Max(.5, bottom - top) * t;
        return new RowSample(left, right, left + inset, right - inset, y, spread);
    }

    internal RowSample GetRow(int rowIndex)
    {
        var table = cachedRows;
        if (table == null || cachedPoseId != poseId)
        {
            table ??= cachedRows = new RowSample[Rows + 1];
            for (int i = 0; i <= Rows; i++) table[i] = ComputeRow(RowTTable[i]);
            cachedPoseId = poseId;
        }
        return table[rowIndex];
    }

    internal (double Left, double Right, double Y, double Spread) Row(double t, bool rounded = false)
    {
        var r = ComputeRow(t);
        return rounded ? (r.RoundedLeft, r.RoundedRight, r.Y, r.Spread) : (r.Left, r.Right, r.Y, r.Spread);
    }

    internal bool Contains(double x, double y)
    {
        var top = GetRow(0);
        var bottom = GetRow(Rows);
        if (y < top.Y || y > bottom.Y) return false;
        double t = (y - top.Y) / Math.Max(.5, bottom.Y - top.Y);
        var r = ComputeRow(t);
        return x >= r.RoundedLeft && x <= r.RoundedRight;
    }
}

internal sealed class GenieShaderEffect : ShaderEffect
{
    const string ShaderBase64 =
        "AAL///7/OQBDVEFCHAAAALcAAAAAAv//BAAAABwAAAAAgQAAsAAAAGwAAAACAAIAAQAKAHQAAAAAAAAAhAAAAAIAAAABAAIAdAAAAAAAAACNAAAAAgABAAEABgB0AAAAAAAAAJQAAAADAAAAAQACAKAAAAAAAAAARXh0cmFzAKsBAAMAAQAEAAEAAAAAAAAAR2VvbWV0cnkAUGFyYW1zAGlucHV0VGV4AKurqwQADAABAAEAAQAAAAAAAABwc18yXzAATWljcm9zb2Z0IChSKSBITFNMIFNoYWRlciBDb21waWxlciAxMC4xAKtRAAAFAwAPoAAAgD8AAIBAAAAAAAAAAABRAAAFBAAPoAAAAD8AAABAAABAQAAAAMIfAAACAAAAgAAAA7AfAAACAAAAkAAID6AEAAAEAAAIgAAAVbAAAFWgAACqoQUAAAMAABGAAAD/gAEA/6ABAAACAQAJgAQA5KAEAAAEAAASgAAA/4ACAFWgAQAAgAQAAAQAAASAAAAAgAAAAIAAAACBBAAABAAABIACAKqgAACqgAAAAIAFAAADAAAIgAAAqoAAAKqABAAABAAABIAAAKqABABVoQQAqqAFAAADAAAEgAAAqoAAAP+AEgAABAEAAoAAAKqAAQCqoAEAVaAFAAADAAAEgAAAAIADAFWgAgAAAwAACIAAAACBAwAAoAUAAAMAAASAAAD/gAAAqoAFAAADAAAEgAEAVYAAAKqAAgAAAwAACIABAFWBAwAAoAUAAAMAAASAAAD/gAAAqoAFAAADAAAEgAAAqoACAP+gAgAAAwEABIABAP+AAAAAoAQAAAQBAAKAAQCqgAEAVYAEAP+hAgAAAwEABIABAFWBAwAAoAYAAAIBAAiAAQBVgFgAAAQBAASAAQCqgAMAAKABAP+ABQAAAwEACIABAACAAAAAoAEAAAICAAiAAAAAoAQAAAQCAAGAAgD/gAEAAIEBAACgBAAABAAACIACAACAAAD/gAEA/4AEAAAEAQAIgAEAVYAEAAChAAD/gAQAAAQAAAiAAQBVgAQAAKAAAP+ABAAABAAACIAAAACwAAAAoQAA/4AEAAAEAAAYgAAA/4ACAFWgAQAAgAQAAAQBAAKAAAAAsAAAAKABAP+BBQAAAwIAEYABAKqAAQBVgAQAAAQBABKAAQBVgAIAVaABAACABQAAAwAACIAAAP+AAQBVgAUAAAMBAAKAAgAAgAMAVaACAAADAQAEgAIAAIEDAACgBQAAAwEAAoABAKqAAQBVgAQAAAQAABGAAQBVgAAAqoAAAACABQAAAwIAAoAAAACAAgAAoEIAAAMCAA+AAgDkgAAI5KAEAAAEAAABgAAAVbAAAFWhAAD/oAQAAAQAABGAAAAAgAIAVaABAACABQAAAwAAAYAAAACAAABVgAUAAAMAAAGAAAAAgAAA/4AFAAADAAAPgAAAAIACAOSAAQAAAgAID4AAAOSA//8AAA==";

    static readonly PixelShader Shader = Load();
    static PixelShader Load()
    {
        var ps = new PixelShader();
        using var ms = new MemoryStream(Convert.FromBase64String(ShaderBase64));
        ps.SetStreamSource(ms);
        ps.Freeze();
        return ps;
    }

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(GenieShaderEffect), 0, SamplingMode.Bilinear);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register("Geometry", typeof(Point4D), typeof(GenieShaderEffect), new UIPropertyMetadata(new Point4D(1, 1, 1, 1), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register("Params", typeof(Point4D), typeof(GenieShaderEffect), new UIPropertyMetadata(new Point4D(0, 0, 0, 1), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty ExtrasProperty = DependencyProperty.Register("Extras", typeof(Point4D), typeof(GenieShaderEffect), new UIPropertyMetadata(new Point4D(1, 1, 0, 0), PixelShaderConstantCallback(2)));

    public GenieShaderEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(GeometryProperty);
        UpdateShaderValue(ParamsProperty);
        UpdateShaderValue(ExtrasProperty);
    }

    internal void Set(GeniePose pose, double scale)
    {
        double p = Math.Clamp(pose.Progress, 0, 1);
        double topY = pose.AnchorY * (1.0 - GeniePose.Smooth(p / 0.62));
        double botY = pose.AnchorY + (pose.Height - pose.AnchorY) * GeniePose.Smooth((p - 0.14) / 0.86);
        double spanY = Math.Max(0.5, botY - topY);
        double topSpread = GeniePose.Smooth(p / 0.82);
        double botSpread = GeniePose.Smooth((p - 0.36) / 0.64);
        double uBend = 0.35 * (1.0 - p);
        double archScale = 0.016 * (1.0 - p);
        SetValue(GeometryProperty, new Point4D(pose.Width, pose.FrameHeight, topY, botY));
        SetValue(ParamsProperty, new Point4D(pose.AnchorX, topSpread, botSpread, 1.0 / spanY));
        SetValue(ExtrasProperty, new Point4D(pose.Height / Math.Max(1.0, pose.FrameHeight), Math.Max(1.0, scale), uBend, archScale));
    }
}

// The foreground sheet is hosted on a persistent GPU mesh or HLSL shader surface
// backed by a pre-rendered, pixel-aligned bitmap of the live drawer controls.
internal sealed class GenieVisual
{
    const int Columns = 8;
    readonly Viewport3D? viewport;
    readonly Viewport2DVisual3D? legacyHost;
    readonly GeometryModel3D? gpuModel;
    readonly MeshGeometry3D? mesh;
    readonly Rectangle? shaderRect;
    readonly GenieShaderEffect? shaderEffect;
    readonly double frame;
    readonly bool isLegacy = GenieEngine.IsLegacy;
    readonly bool isShader = GenieEngine.IsShader;
    SoftContour? shadow;
    GeniePose? previous;
    BitmapSource? boundBitmap;
    double boundW, boundH;

    internal BitmapSource? BoundBitmap => boundBitmap;
    internal bool HasBitmap => isLegacy || boundBitmap != null;
    internal bool IsBitmapBound(BitmapSource? bitmap, double panelWidth, double panelHeight) =>
        isLegacy || (bitmap != null && ReferenceEquals(boundBitmap, bitmap) && Math.Abs(boundW - panelWidth) < 0.1 && Math.Abs(boundH - panelHeight) < 0.1);
    internal UIElement View => isShader ? shaderRect! : viewport!;

    internal GenieVisual(double width, double frame, bool shareShadow = false)
        : this(width, frame, frame, shareShadow) { }

    internal GenieVisual(double width, double height, double frame, bool shareShadow = false)
    {
        this.frame = frame;
        if (isShader)
        {
            shaderEffect = new GenieShaderEffect();
            shaderRect = new Rectangle
            {
                Width = width,
                Height = frame,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Effect = shaderEffect
            };
            return;
        }

        mesh = new MeshGeometry3D();
        var uv = new PointCollection((GeniePose.Rows + 1) * (Columns + 1));
        var normals = new Vector3DCollection((GeniePose.Rows + 1) * (Columns + 1));
        var indices = new Int32Collection(GeniePose.Rows * Columns * 6);
        var forwardNormal = new Vector3D(0, 0, 1);
        for (int r = 0; r <= GeniePose.Rows; r++)
            for (int c = 0; c <= Columns; c++)
            {
                uv.Add(new Point((double)c / Columns, GeniePose.RowT(r)));
                normals.Add(forwardNormal);
            }
        for (int r = 0; r < GeniePose.Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                int a = r * (Columns + 1) + c, b = a + Columns + 1;
                indices.Add(a); indices.Add(b); indices.Add(a + 1);
                indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
            }
        uv.Freeze();
        normals.Freeze();
        indices.Freeze();
        mesh.TextureCoordinates = uv;
        mesh.Normals = normals;
        mesh.TriangleIndices = indices;

        double pad = shareShadow ? SoftContour.Pad : 0;
        viewport = new Viewport3D
        {
            Width = width + 2 * pad,
            Height = frame + 2 * pad,
            Margin = new Thickness(-pad),
            ClipToBounds = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Camera = new OrthographicCamera(new Point3D(width / 2, frame / 2, 10), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), width + 2 * pad)
        };
        viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });

        if (isLegacy)
        {
            var material = new DiffuseMaterial(Brushes.White);
            Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
            legacyHost = new Viewport2DVisual3D
            {
                Geometry = mesh,
                Material = material,
                CacheMode = new BitmapCache(2) { SnapsToDevicePixels = false }
            };
            viewport.Children.Add(legacyHost);
        }
        else
        {
            gpuModel = new GeometryModel3D { Geometry = mesh };
            viewport.Children.Add(new ModelVisual3D { Content = gpuModel });
        }
        RenderOptions.SetEdgeMode(viewport, EdgeMode.Unspecified);
    }

    internal void SetBitmap(BitmapSource bitmap, double panelWidth, double panelHeight)
    {
        if (isLegacy) return;
        if (ReferenceEquals(boundBitmap, bitmap) && Math.Abs(boundW - panelWidth) < 0.1 && Math.Abs(boundH - panelHeight) < 0.1)
            return;
        boundBitmap = bitmap;
        boundW = panelWidth;
        boundH = panelHeight;

        if (isShader)
        {
            var brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.None,
                TileMode = TileMode.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Viewbox = new Rect(0, 0, panelWidth, panelHeight),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, panelWidth, panelHeight),
                ViewportUnits = BrushMappingMode.Absolute
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.Linear);
            brush.Freeze();
            shaderRect!.Fill = brush;
            return;
        }

        var meshBrush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        RenderOptions.SetBitmapScalingMode(meshBrush, BitmapScalingMode.Linear);
        meshBrush.Freeze();
        var mat = new DiffuseMaterial(meshBrush);
        mat.Freeze();
        gpuModel!.Material = mat;
    }

    internal void Set(GeniePose pose, double dpiScale = 1.0)
    {
        if (Equals(previous, pose)) return;
        previous = pose;

        if (isShader)
        {
            shaderEffect!.Set(pose, dpiScale);
            return;
        }

        var points = new Point3DCollection((GeniePose.Rows + 1) * (Columns + 1));
        if (isLegacy)
        {
            for (int r = 0; r <= GeniePose.Rows; r++)
            {
                var row = pose.GetRow(r);
                double span = row.Right - row.Left;
                for (int col = 0; col <= Columns; col++)
                    points.Add(new Point3D(row.Left + span * col / Columns, frame - row.Y, 0));
            }
        }
        else
        {
            double p = Math.Clamp(pose.Progress, 0, 1);
            double archScale = 0.016 * (1.0 - p) * pose.Height;
            for (int r = 0; r <= GeniePose.Rows; r++)
            {
                var row = pose.GetRow(r);
                double t = GeniePose.RowT(r);
                double span = row.Right - row.Left;
                double arch = Math.Sin(Math.PI * t) * row.Spread * (1.0 - row.Spread) * archScale;
                for (int col = 0; col <= Columns; col++)
                {
                    double u = (double)col / Columns;
                    double dy = 4.0 * u * (1.0 - u) * arch;
                    points.Add(new Point3D(row.Left + span * u, frame - (row.Y + dy), 0));
                }
            }
        }
        points.Freeze();
        mesh!.Positions = points;
    }

    internal void Attach(FrameworkElement visual)
    {
        if (legacyHost == null) return;
        var size = new Size(visual.Width, visual.Height);
        visual.Measure(size);
        visual.Arrange(new Rect(size));
        legacyHost.Visual = visual;
    }

    internal void Detach()
    {
        if (legacyHost != null) legacyHost.Visual = null;
        boundBitmap = null;
        if (shaderRect != null) shaderRect.Fill = null;
        if (gpuModel != null) gpuModel.Material = gpuModel.BackMaterial = null;
    }

    internal void AttachShadow(SoftContour contour)
    {
        if (viewport == null || shadow == contour) return;
        DetachShadow();
        contour.ReleaseProjection();
        contour.Projection.Transform = new TranslateTransform3D(-SoftContour.Pad, -SoftContour.Pad, -.01);
        viewport.Children.Insert(1, contour.Projection);
        shadow = contour;
    }

    internal void DetachShadow()
    {
        if (shadow == null || viewport == null) return;
        viewport.Children.Remove(shadow.Projection);
        shadow.RestoreProjection();
        shadow = null;
    }
}

// Finite analytic spring, independent of the approved Dock motion. Retargets preserve
// position and velocity; callbacks may re-enter without an old completion hiding new UI.
internal sealed class DrawerMotion
{
    bool rendering;
    TimeSpan? lastRenderingTime;
    readonly Stopwatch clock = new(); readonly Action<double> render; readonly Action<bool> completed;
    double last; long revision; bool paused, startOnRender;
    volatile bool inputPause, inputEnabled;
    internal bool InputPaused => inputPause;
    internal void PauseFromHook() { if (inputEnabled) inputPause = true; }
    internal double Value { get; private set; }
    internal double Velocity { get; private set; }
    internal double Target { get; private set; }
    internal bool Running => rendering || paused;
    internal DrawerMotion(Action<double> render, Action<bool> completed) { this.render = render; this.completed = completed; }
    internal void Snap(double p) { GestureChecks.Trace?.Invoke("drawer-snap", new { p, oldTarget = Target, phase = Value }); revision++; Stop(); paused = false; inputPause = false; Value = Target = p; Velocity = 0; render(p); }
    internal void To(bool open, bool startOnRender = false) { GestureChecks.Trace?.Invoke("drawer-target", new { open, phase = Value, velocity = Velocity }); Target = open ? 1 : 0; revision++; paused = false; inputPause = false; if (Math.Abs(Value - Target) < .0001 && Math.Abs(Velocity) < .002) { Finish(revision); return; } Start(startOnRender); }
    void Start(bool onRender = false)
    {
        if (rendering) return;
        inputEnabled = true;
        clock.Restart();
        startOnRender = onRender;
        last = 0;
        lastRenderingTime = null;
        rendering = true;
        CompositionTarget.Rendering += RenderFrame;
        Anim.ActiveTimers++;
    }
    void RenderFrame(object? sender, EventArgs e)
    {
        if (!rendering || e is not RenderingEventArgs frame || frame.RenderingTime == lastRenderingTime) return;
        lastRenderingTime = frame.RenderingTime;
        if (startOnRender)
        {
            // Advance the first presented pose by at most one 120 Hz frame. A slow
            // first callback still cannot spend the whole wait in a single spring step.
            last = -Math.Min(clock.Elapsed.TotalSeconds, 1.0 / 120);
            clock.Restart();
            startOnRender = false;
        }
        Tick();
    }
    internal void Pause() { if (!rendering) return; Stop(); paused = true; }
    internal void Resume() { inputPause = false; if (!paused) return; paused = false; Start(); }
    void Tick() { if (inputPause) { Pause(); return; } long request = revision; double dt = Math.Clamp(clock.Elapsed.TotalSeconds - last, 0, .04); last = clock.Elapsed.TotalSeconds; double w = Target == 1 ? 30 : 38, x = Value - Target, b = Velocity + w * x, e = Math.Exp(-w * dt); Value = Target + e * (x + b * dt); Velocity = e * (Velocity - w * b * dt); render(Value); if (request != revision) return; if (!Anim.Allowed || Math.Abs(Value - Target) < .002 && Math.Abs(Velocity) < .065) Finish(request); }
    void Finish(long request) { if (request != revision) return; Stop(); paused = false; Value = Target; Velocity = 0; render(Value); if (request == revision) completed(Target == 1); }
    void Stop()
    {
        inputEnabled = false;
        if (!rendering) return;
        rendering = false;
        CompositionTarget.Rendering -= RenderFrame;
        clock.Stop();
        Anim.ActiveTimers--;
    }
    internal void Cancel() { revision++; Stop(); paused = false; inputPause = false; }
}
