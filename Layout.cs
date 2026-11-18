namespace MacDock;

// All dimensions below are DIPs. Win32 placement is converted to physical pixels only at the boundary.
// The drawer is a landscape panel: width is derived from the monitor so a wide desktop is used
// properly, while the visible row count stays deliberately small (a drawer, not a file manager).
internal sealed record Layout(double WorkWidth,double WorkHeight,int DockCapacity=0,int Rows=0,int RequestedColumns=0,double DesktopIconDip=48)
{
    // Explorer's real icon preference provides a gentle bias, not another DPI scale.
    // Manual row/column/capacity settings still take precedence over automatic density.
    public double Preference => Math.Clamp(Math.Sqrt(Math.Clamp(DesktopIconDip,16,256)/48),.92,1.10);
    public double DensityUnit => Math.Clamp(Math.Min(WorkWidth/1920,WorkHeight/1080),.84,1.18)*Preference;
    // Visual compactness is independent of automatic capacity and the wake target.
    public double Unit => .92*DensityUnit;

    // Dock metrics. The bar is a little taller than the icon row so magnification can grow
    // upwards without being clipped by the window, and a few pixels of horizontal slack give
    // the magnified wave room to spread.
    public double Icon => 42*Unit;
    public double DockTile => 58*Unit;
    public double DockTileHeight => 66*Unit;
    public double DockHeight => 68*Unit;
    public double DockBottomGap => 16*DensityUnit;
    public double DockGap => 6*Unit;
    public double DockPad => 8*Unit;
    public double DividerWidth => 1+12*Unit;
    public double DrawerButton => 42*Unit;

    // Drawer metrics: smaller icons, more air around them, wider cells.
    public double DrawerIcon => 60*Unit;
    public double DrawerLabelGap => 11*Unit;
    public double DrawerLabelLine => Math.Max(16,17*Unit);
    public double DrawerTile => 102*Unit;
    public double DrawerTileHeight => 116*Unit;
    public double RowHeight => 134*Unit;
    public double GapX => 24*Unit;
    public double Chrome => 30*Unit;
    public double ScrollReserve => 6;
    public double DrawerPadTop => 16*Unit;
    public double DrawerPadBottom => 14*Unit;
    public double ToolbarHeight => Math.Max(34,34*Unit);
    public double SearchHeight => Math.Max(32,34*Unit);
    public double FooterHeight => Math.Max(30,34*Unit);
    public double Header => DrawerPadTop+ToolbarHeight+16*Unit;
    public double Footer => 10*Unit+FooterHeight+DrawerPadBottom;

    public int EffectiveCapacity=>DockCapacity>0?DockCapacity:Math.Clamp((int)Math.Round(WorkWidth/(150*DensityUnit)),4,12);
    // Bounded desktop grid, typically 7-8 columns at the primary display size.
    public int AutoColumns => Math.Clamp((int)Math.Round(WorkWidth*.58/(126*DensityUnit)),4,8);
    public int EffectiveColumns => RequestedColumns>0?RequestedColumns:AutoColumns;
    // Five rows is the desktop-class target; short screens fall back to fewer.
    public int AutoRows
    {
        get
        {
            double density=DensityUnit;
            double chrome=56*density+Math.Max(34,34*density)+Math.Max(30,34*density);
            return Math.Clamp((int)((WorkHeight*.72-chrome)/(134*density)),2,5);
        }
    }
    public int EffectiveRows => Rows>0?Rows:AutoRows;
    public int VisibleDockCount(int count)=>Math.Min(Math.Min(count,EffectiveCapacity),Math.Max(1,(int)((WorkWidth-140*Unit)/(DockTile+DockGap))));
    public double DrawerWidth => Math.Min(WorkWidth-40*Unit,Chrome*2+ScrollReserve+EffectiveColumns*DrawerTile+(EffectiveColumns-1)*GapX);
    public int Columns => Math.Max(1,(int)Math.Floor((DrawerWidth-Chrome*2-ScrollReserve+GapX)/(DrawerTile+GapX)+1e-8));
    public int VisibleRows=>Math.Max(1,Math.Min(EffectiveRows,(int)((WorkHeight*.72-Header-Footer)/RowHeight)));
    public double GridHeight=>Math.Min(VisibleRows*RowHeight,Math.Max(1,WorkHeight-DockHeight-DockBottomGap-38-Header-Footer));
    public double DockWidth(int count)
    {
        var n=VisibleDockCount(count);
        var content=DockPad*2+n*DockTile+(n>0?DividerWidth+DockGap/2:0)+DrawerButton;
        return Math.Min(WorkWidth-24*Unit,content);
    }
    public double DrawerHeight(int rows,int headings) => Header+GridHeight+Footer;
}
