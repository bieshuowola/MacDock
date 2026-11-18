namespace MacDock;

// Hook-thread decision only: immutable physical rectangles, no Dispatcher/WPF access and
// no screen-sized receiver window. Keep ownership of a dismissal's up even after hiding.
internal sealed class ModalInputGate
{
    internal sealed record Regions(Native.Rect Drawer,Native.Rect Dock,bool Suspended=false,nint DrawerWindow=0,nint DockWindow=0,bool TextInput=false,bool Opening=false,GeniePose? Shape=null,double Scale=1,bool RedirectedForeground=false);
    internal volatile Regions? Current;
    readonly Action dismiss;
    readonly Action deactivate;
    volatile uint held;
    volatile uint twiceButton;
    internal bool OwnsGesture=>held!=0;
    internal void CaptureWake(){held|=1;twiceButton=0;}
    internal void FinishDismissal(){twiceButton=0;}
    long twiceUntil;
    Native.Point twicePoint;
    readonly int doubleTime,width,height;
    internal ModalInputGate(Action dismiss,int doubleTime=500,int width=4,int height=4,Action? deactivate=null){this.dismiss=dismiss;this.deactivate=deactivate??dismiss;this.doubleTime=doubleTime;this.width=width;this.height=height;}
    internal bool Filter(Native.Point point,uint message,uint data=0,long now=-1)
    {
        if(now<0)now=Environment.TickCount64;
        uint bit=message is 0x201 or 0x202?1u:message is 0x204 or 0x205?2u:message is 0x207 or 0x208?4u:message is 0x20B or 0x20C?((data>>16)==2?16u:8u):0;
        if(bit==0)return false;
        bool down=message is 0x201 or 0x204 or 0x207 or 0x20B;
        if(!down){bool owned=(held&bit)!=0;held&=~bit;return owned;}
        if(held!=0){held|=bit;return true;}
        if(bit==twiceButton&&now<=twiceUntil&&Math.Abs(point.X-twicePoint.X)<=width&&Math.Abs(point.Y-twicePoint.Y)<=height){twiceButton=0;held|=bit;return true;}
        twiceButton=0;
        var area=Current;
        if(area==null||area.Suspended)return false;
        if(area.DrawerWindow!=0&&!area.Opening)
        {
            var foreground=Native.GetForegroundWindow();
            if(foreground!=0&&foreground!=area.DrawerWindow&&foreground!=area.DockWindow)
            {
                if(Native.GetAncestor(foreground,3)==area.DrawerWindow)return false;
                Native.GetWindowThreadProcessId(foreground,out var process);
                if(process!=Environment.ProcessId){Current=null;deactivate();return false;}
            }
        }
        var hit=area.DrawerWindow==0&&area.DockWindow==0?0:Native.WindowFromPoint(point);
        if(area.TextInput&&hit!=0){var cls=Native.Class(hit);if(cls.StartsWith("Microsoft.IME.",StringComparison.Ordinal)||cls is "MSCTFIME UI" or "IME")return false;}
        bool inDrawer=area.Drawer.Contains(point)&&(area.Shape==null||area.Shape.Contains((point.X-area.Drawer.Left)/area.Scale,(point.Y-area.Drawer.Top)/area.Scale));
        if(inDrawer&&(area.DrawerWindow==0||hit==area.DrawerWindow)||area.Dock.Contains(point)&&(area.DockWindow==0||hit==area.DockWindow||area.RedirectedForeground&&hit==area.DrawerWindow&&!inDrawer))return false;
        // A composition backdrop may become visible a frame before the layered WPF content
        // has its first input bitmap. Absorb that pair without dismissing or clicking behind.
        if(area.Opening&&inDrawer){held|=bit;return true;}
        held|=bit;twiceButton=bit;twicePoint=point;twiceUntil=now+doubleTime;Current=null;dismiss();return true;
    }
}
