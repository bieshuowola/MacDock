namespace MacDock;

// The same reversible Dock phase drives the visual relay. The sensing rectangle and
// transparent native window never follow the shrinking visible line.
internal static class WakeStrip
{
    internal static (double Length,double Height,double Alpha) Pose(double dockPhase)
    {
        double h=GeniePose.Smooth(1-Math.Clamp(dockPhase/.68,0,1));
        return(.035+.965*h,.55+.45*Math.Sqrt(h),Math.Pow(h,.72));
    }
}
