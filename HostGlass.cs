using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
namespace MacDock;

// Documented host backdrop: samples outside the HWND, rather than the app's own content.
internal sealed class HostGlass:IDisposable
{
#if MACDOCK_EXPERIENCE_CHECKS
    internal static bool DiagnosticHideDrawer;
    internal static bool DiagnosticSkipRegion;
#endif
    [StructLayout(LayoutKind.Sequential)]struct QueueOptions{public uint Size;public int Thread,Apartment;}
    [DllImport("CoreMessaging.dll")]static extern int CreateDispatcherQueueController(QueueOptions options,out nint controller);
    [ComImport,Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopInterop{void CreateDesktopWindowTarget(nint hwnd,[MarshalAs(UnmanagedType.Bool)]bool topmost,out nint target);void EnsureOnThread(uint thread);}
    static nint queue;
    readonly nint front,window;
    readonly double inset;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint data);
    [DllImport("user32.dll")]static extern bool DestroyWindow(nint hwnd);
    readonly Compositor compositor=null!;
    readonly DesktopWindowTarget target=null!;
    readonly ContainerVisual root=null!;
    readonly SpriteVisual backdrop=null!;
    CompositionRoundedRectangleGeometry geometry=null!;
    CompositionGeometricClip clip=null!;
    CompositionPathGeometry? curveGeometry;CompositionGeometricClip? curveClip;
    bool disposed;
    bool showing;
    float alpha=-1;
    Native.Rect bounds;
    [DllImport("user32.dll")]static extern nint GetWindow(nint window,uint command);
    int widthPixels,heightPixels,radiusPixels;
    double reveal=1;
    internal object Snapshot(){Native.GetWindowRect(front,out var outer);var f=outer;int padding=(int)Math.Round(inset*dpiScale);f.Left+=padding;f.Top+=padding;f.Right-=padding;f.Bottom-=padding;Native.GetWindowRect(window,out var b);return new{composedWidth=widthPixels,composedHeight=heightPixels,frontVisible=Native.IsWindowVisible(front),backdropVisible=Native.IsWindowVisible(window),nativeFront=new{outer.Left,outer.Top,outer.Right,outer.Bottom},front=new{f.Left,f.Top,f.Right,f.Bottom},backdrop=new{b.Left,b.Top,b.Right,b.Bottom},deforming=genie!=null};}
    readonly List<SpriteVisual> ribbon=new();
    // Follow each rounded end at full resolution. In the middle of the
    // silhouette the smooth taper needs fewer live backdrop sprites.
    static readonly int[] ribbonRows=Enumerable.Range(0,GeniePose.Rows+1)
        .Where(i=>i<=7||i>=GeniePose.Rows-7||i%3==0).ToArray();
    GeniePose? genie;
    bool poseDirty=true;
    RestShadow? restShadow;double dpiScale=1;
    internal nint BackdropHandle => window;
    internal nint ShadowHandle => restShadow?.Handle ?? 0;
    internal void UpdateScale(double scale)
    {
        if(scale<=0||Math.Abs(dpiScale-scale)<=0.0001)return;
        dpiScale=scale;poseDirty=true;
        if(showing)RefreshShadow();
    }
    [DllImport("gdi32.dll")]static extern nint CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")]static extern nint CreatePolygonRgn([In]Native.Point[] points,int count,int fill);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")]static extern int SetWindowRgn(nint window,nint region,bool redraw);
    internal HostGlass(nint front,double inset=0)
    {
        this.front=front;
        this.inset=inset;
        window=CreateWindowEx(0x082000A8,"STATIC","MacDock compositor backdrop",0x80000000,0,0,1,1,0,0,0,0);
        if(window==0)throw new System.ComponentModel.Win32Exception();
        try{
        Native.HostBackdrop(window);
        // The native non-client outline is independent of sprite geometry/opacity.
        // Keep the host backdrop capability, with no native glass frame or shadow.
        int policy=1,square=1;Native.DwmSetWindowAttribute(window,2,ref policy,4);Native.DwmSetWindowAttribute(window,33,ref square,4);var frame=new Native.Margins();Native.DwmExtendFrameIntoClientArea(window,ref frame);
        if(queue==0)Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new QueueOptions{Size=(uint)Marshal.SizeOf<QueueOptions>(),Thread=2,Apartment=2},out queue));
        compositor=new Compositor();var interop=compositor.As<IDesktopInterop>();interop.CreateDesktopWindowTarget(window,false,out var pointer);
        try{target=MarshalInspectable<DesktopWindowTarget>.FromAbi(pointer);}finally{Marshal.Release(pointer);}
        root=compositor.CreateContainerVisual();backdrop=compositor.CreateSpriteVisual();backdrop.Brush=compositor.CreateHostBackdropBrush();
        geometry=compositor.CreateRoundedRectangleGeometry();clip=compositor.CreateGeometricClip(geometry);root.Clip=clip;root.Children.InsertAtTop(backdrop);target.Root=root;
        }catch{Dispose();throw;}
    }
    internal void Resize(double width,double height,double scale,double radius)
    {dpiScale=scale;var size=new Vector2((float)(width*scale),(float)(height*scale));
        int w=(int)Math.Round(size.X),h=(int)Math.Round(size.Y),r=(int)Math.Round(radius*scale);
        // The geometric clip gives antialiased visuals; the HWND region also excludes input
        // at the empty corners. The auxiliary HWND can never steal a desktop corner click.
        if(w>0&&h>0&&(w!=widthPixels||h!=heightPixels||r!=radiusPixels)){root.Size=size;backdrop.Size=size;poseDirty=true;widthPixels=w;heightPixels=h;radiusPixels=r;ApplyReveal();}
    }
    internal void Reveal(double value){reveal=value;ApplyReveal();RefreshShadow();}
    internal void RevealAndOpacity(double revealValue,double opacityValue)
    {
        reveal=revealValue;ApplyReveal();
        float next=(float)Math.Clamp(opacityValue,0,1);
        if(next!=alpha){alpha=next;root.Opacity=showing?next:0;}
        RefreshShadow();
    }
    void RefreshShadow()
    {
        if(inset>0)return;
        if(!showing){restShadow?.Hide();return;}
        if(widthPixels<=0||heightPixels<=0)return;
        restShadow??=new RestShadow();var shape=DockMotion.Shape(widthPixels/dpiScale,heightPixels/dpiScale,radiusPixels/dpiScale,reveal);
        restShadow.Shape(widthPixels/dpiScale,heightPixels/dpiScale,radiusPixels/dpiScale,shape.Bounds,genie,Math.Max(0,alpha));
        restShadow.Sync(window,bounds,dpiScale,showing&&(genie==null||genie.Progress>.0001));
    }
    void ApplyReveal(){if(genie!=null)return;var (r,c)=DockMotion.Shape(widthPixels,heightPixels,radiusPixels,reveal);geometry.Offset=new Vector2((float)r.X,(float)r.Y);geometry.Size=new Vector2((float)r.Width,(float)r.Height);geometry.CornerRadius=new Vector2((float)c);}
    internal void Deform(GeniePose? pose,double scale)
    {
        if(scale>0&&Math.Abs(dpiScale-scale)>0.0001){dpiScale=scale;poseDirty=true;}
        if(!poseDirty&&Equals(genie,pose))return;
        bool dirty=poseDirty;
        poseDirty=false;
        bool changing=(genie==null)!=(pose==null);
        genie=pose;
        if(pose==null){
            root.Size=new Vector2(widthPixels,heightPixels);
            backdrop.Size=new Vector2(widthPixels,heightPixels);
            // Build the complete normal clip off-tree. Revealing the plain sprite and
            // attaching an old clip before updating its size/radius exposes white corner
            // wedges when the two compositors consume the handoff in different frames.
            if(changing){var shape=DockMotion.Shape(widthPixels,heightPixels,radiusPixels,reveal);var next=compositor.CreateRoundedRectangleGeometry();next.Offset=new Vector2((float)shape.Bounds.X,(float)shape.Bounds.Y);next.Size=new Vector2((float)shape.Bounds.Width,(float)shape.Bounds.Height);next.CornerRadius=new Vector2((float)shape.Radius);var ready=compositor.CreateGeometricClip(next);var priorClip=clip;var priorGeometry=geometry;geometry=next;clip=ready;root.Clip=ready;priorClip.Dispose();priorGeometry.Dispose();}
            else{ApplyReveal();root.Clip=clip;}
            // Keep full per-pixel antialiased compositor clip without GDI 1-bit SetWindowRgn hard clipping.
            if(changing)SetWindowRgn(window,0,false);
            backdrop.IsVisible=true;foreach(var strip in ribbon)strip.IsVisible=false;
            // Restore the independent rounded shadow, never the full-client native
            // glass frame, which is outside the Composition geometric clip.
            if(changing&&showing)Sync(true);
            RefreshShadow();
            return;
        }
        if(inset>0){
            var topRow=pose.Row(0,true);var bottomRow=pose.Row(1,true);
            bool hasArea=pose.Progress>0.001&&(bottomRow.Y-topRow.Y)*scale>=2.0;
            if(changing||dirty||curveGeometry==null||curveClip==null)
            {
                curveGeometry??=compositor.CreatePathGeometry();curveClip??=compositor.CreateGeometricClip(curveGeometry);
                curveGeometry.Path=NativeContour.Build(pose,scale);root.Clip=curveClip;
                var deformSize=new Vector2(widthPixels,(float)(pose.FrameHeight*scale));
                root.Size=deformSize;backdrop.Size=deformSize;backdrop.IsVisible=hasArea;
                foreach(var strip in ribbon)strip.IsVisible=false;
                if(changing)SetWindowRgn(window,0,false);
            }
            else
            {
                curveGeometry.Path=NativeContour.Build(pose,scale);
                if(backdrop.IsVisible!=hasArea)backdrop.IsVisible=hasArea;
            }
            RefreshShadow();return;
        }
        if(changing){root.Clip=null;backdrop.IsVisible=false;}
        RefreshShadow();
#if MACDOCK_EXPERIENCE_CHECKS
        if(DiagnosticSkipRegion&&changing)SetWindowRgn(window,0,false);
        if(!DiagnosticSkipRegion)
#endif
        {
            var edge=new Native.Point[(GeniePose.Rows+1)*2];for(int i=0;i<=GeniePose.Rows;i++){var row=pose.Row(GeniePose.RowT(i),true);edge[i]=new Native.Point((int)Math.Floor(row.Left*scale)-1,(int)Math.Round(row.Y*scale));edge[edge.Length-1-i]=new Native.Point((int)Math.Ceiling(row.Right*scale)+1,(int)Math.Round(row.Y*scale));}var region=CreatePolygonRgn(edge,edge.Length,1);if(region!=0&&SetWindowRgn(window,region,false)==0)DeleteObject(region);
        }
        while(ribbon.Count<ribbonRows.Length-1){var v=compositor.CreateSpriteVisual();v.Brush=backdrop.Brush;root.Children.InsertAtTop(v);ribbon.Add(v);}
        for(int i=0;i<ribbon.Count;i++){
            var a=pose.Row(GeniePose.RowT(ribbonRows[i]),true);var b=pose.Row(GeniePose.RowT(ribbonRows[i+1]),true);
            double left=a.Left*scale,right=a.Right*scale,nextLeft=b.Left*scale,nextRight=b.Right*scale,y=a.Y*scale,nextY=b.Y*scale;
            float w=(float)Math.Max(.1,right-left),h=(float)Math.Max(.05,nextY-y),k=(float)((right-left)/Math.Max(.1,nextRight-nextLeft));
            var strip=ribbon[i];if(changing)strip.IsVisible=true;strip.Size=new Vector2(w,h+(i+1<ribbon.Count?.35f:0));
            // Projective quad, not a stretched one-pixel image: every strip still samples
            // the live compositor host backdrop. Shared edges use identical coordinates.
            strip.TransformMatrix=new Matrix4x4(1,0,0,0,(float)(nextLeft*k-left)/h,(float)(nextY*k-y)/h,0,(k-1)/h,0,0,1,0,(float)left,(float)y,0,1);
        }
    }
    internal void Sync(bool visible)
    {
#if MACDOCK_EXPERIENCE_CHECKS
        if(DiagnosticHideDrawer&&inset>0)visible=false;
#endif
        if(!visible){root.Opacity=0;restShadow?.Hide();if(showing)Native.ShowWindow(window,0);showing=false;return;}
        root.Opacity=Math.Max(0,alpha);
        Native.GetWindowRect(front,out var r);
        int padding=(int)Math.Round(inset*dpiScale);
        r.Left+=padding;r.Top+=padding;r.Right-=padding;r.Bottom-=padding;
        bool needWindowZ=!showing||GetWindow(front,2)!=window;
        bool sameWindowBounds=showing&&r.Equals(bounds);
        int w=r.Right-r.Left,h=r.Bottom-r.Top;
        uint winFlags=(needWindowZ?0u:0x0004u)|(showing&&w==bounds.Right-bounds.Left&&h==bounds.Bottom-bounds.Top?0x0001u:0u)|0x0400u;
        if(!showing)winFlags|=0x0040u;
        bounds=r;showing=true;
        bool needWindow=!sameWindowBounds||needWindowZ;
        if(needWindow&&needWindowZ)restShadow?.InvalidateZOrder();
        bool needShadow=false;
        nint shadowHwnd=0;int sx=0,sy=0,sw=0,sh=0;uint sFlags=0;
        if(inset==0&&widthPixels>0&&heightPixels>0)
        {
            restShadow??=new RestShadow();
            var shape=DockMotion.Shape(widthPixels/dpiScale,heightPixels/dpiScale,radiusPixels/dpiScale,reveal);
            restShadow.Shape(widthPixels/dpiScale,heightPixels/dpiScale,radiusPixels/dpiScale,shape.Bounds,genie,Math.Max(0,alpha));
            needShadow=restShadow.PreparePlacement(window,bounds,dpiScale,showing&&(genie==null||genie.Progress>.0001),out shadowHwnd,out sx,out sy,out sw,out sh,out sFlags);
        }
        if(needWindow&&needShadow)
            Native.DeferWindowPosPair(window,front,r.Left,r.Top,w,h,winFlags,shadowHwnd,window,sx,sy,sw,sh,sFlags);
        else if(needWindow)
            Native.DeferWindowPosAsync(window,front,r.Left,r.Top,w,h,winFlags);
        else if(needShadow)
            Native.DeferWindowPosAsync(shadowHwnd,window,sx,sy,sw,sh,sFlags);
    }
    internal void Opacity(double value){float next=(float)Math.Clamp(value,0,1);if(next==alpha)return;alpha=next;root.Opacity=showing?next:0;RefreshShadow();}
    internal static void ShutdownQueue()
    {
        if(queue==0)return;
        try{var controller=MarshalInspectable<Windows.System.DispatcherQueueController>.FromAbi(queue);var action=controller.ShutdownQueueAsync();var deadline=Environment.TickCount64+3000;
            while(action.Status==Windows.Foundation.AsyncStatus.Started&&Environment.TickCount64<deadline){for(int i=0;i<32&&Native.PeekMessage(out var message,0,0,0,1);i++){if(message.message!=0x12){Native.TranslateMessage(ref message);Native.DispatchMessage(ref message);}}Thread.Sleep(1);}if(action.Status!=Windows.Foundation.AsyncStatus.Started)action.Close();
        }catch(Exception e){Program.Log(e);}finally{Marshal.Release(queue);queue=0;}
    }
    public void Dispose(){if(disposed)return;disposed=true;restShadow?.Close();Native.ShowWindow(window,0);if(target!=null)target.Root=null;foreach(var strip in ribbon)strip.Dispose();ribbon.Clear();backdrop?.Brush?.Dispose();backdrop?.Dispose();curveClip?.Dispose();curveGeometry?.Dispose();clip?.Dispose();geometry?.Dispose();root?.Dispose();target?.Dispose();compositor?.Dispose();DestroyWindow(window);}
}
