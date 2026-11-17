using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI.Composition;
namespace MacDock;

// Public IGeometrySource2D / IGeometrySource2DInterop ABI, as in Microsoft's
// Composition HelloVectors sample. No additional graphics framework.
internal static unsafe class NativeContour
{
    static readonly Guid unknown=new("00000000-0000-0000-C000-000000000046"),inspectable=new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"),
        marker=new("CAFF7902-670C-4181-A624-DA977203B845"),interop=new("0657AF73-53FD-47CF-84FF-C8492D2A80A3"),agile=new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    static readonly nint factory=CreateFactory(),markerTable=MarkerTable(),interopTable=InteropTable();
    sealed class Source
    {
        internal nint Geometry,Marker,Interop,Handle;
        internal int References=1;
        internal Source(nint geometry)
        {
            Geometry=geometry;Handle=GCHandle.ToIntPtr(GCHandle.Alloc(this));
            Marker=Marshal.AllocHGlobal(2*IntPtr.Size);Interop=Marshal.AllocHGlobal(2*IntPtr.Size);
            Marshal.WriteIntPtr(Marker,markerTable);Marshal.WriteIntPtr(Marker,IntPtr.Size,Handle);
            Marshal.WriteIntPtr(Interop,interopTable);Marshal.WriteIntPtr(Interop,IntPtr.Size,Handle);
        }
    }
    static Source State(nint self)=>(Source)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self,IntPtr.Size)).Target!;
    static nint MarkerTable()
    {
        var table=(nint*)Marshal.AllocHGlobal(6*IntPtr.Size);
        table[0]=(nint)(delegate* unmanaged[Stdcall]<nint,Guid*,nint*,int>)&Query;
        table[1]=(nint)(delegate* unmanaged[Stdcall]<nint,uint>)&AddRef;
        table[2]=(nint)(delegate* unmanaged[Stdcall]<nint,uint>)&Release;
        table[3]=(nint)(delegate* unmanaged[Stdcall]<nint,uint*,nint*,int>)&GetIids;
        table[4]=(nint)(delegate* unmanaged[Stdcall]<nint,nint*,int>)&GetName;
        table[5]=(nint)(delegate* unmanaged[Stdcall]<nint,int*,int>)&GetTrust;
        return (nint)table;
    }
    static nint InteropTable()
    {
        var table=(nint*)Marshal.AllocHGlobal(5*IntPtr.Size);
        table[0]=(nint)(delegate* unmanaged[Stdcall]<nint,Guid*,nint*,int>)&Query;
        table[1]=(nint)(delegate* unmanaged[Stdcall]<nint,uint>)&AddRef;
        table[2]=(nint)(delegate* unmanaged[Stdcall]<nint,uint>)&Release;
        table[3]=(nint)(delegate* unmanaged[Stdcall]<nint,nint*,int>)&GetGeometry;
        table[4]=(nint)(delegate* unmanaged[Stdcall]<nint,nint,nint*,int>)&TryGeometry;
        return (nint)table;
    }
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static int Query(nint self,Guid* id,nint* result)
    {
        *result=0;var state=State(self);
        if(*id==unknown||*id==inspectable||*id==marker||*id==agile)*result=state.Marker;
        else if(*id==interop)*result=state.Interop;else return unchecked((int)0x80004002);
        Interlocked.Increment(ref state.References);return 0;
    }
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static uint AddRef(nint self)=>(uint)Interlocked.Increment(ref State(self).References);
    static uint ReleaseCore(nint self)
    {
        var state=State(self);int count=Interlocked.Decrement(ref state.References);
        if(count==0){Marshal.Release(state.Geometry);Marshal.FreeHGlobal(state.Marker);Marshal.FreeHGlobal(state.Interop);GCHandle.FromIntPtr(state.Handle).Free();}
        return (uint)count;
    }
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static uint Release(nint self)=>ReleaseCore(self);
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static int GetIids(nint self,uint* count,nint* ids){*ids=Marshal.AllocCoTaskMem(16);*(Guid*)*ids=marker;*count=1;return 0;}
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static int GetName(nint self,nint* name){*name=0;return 0;}
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static int GetTrust(nint self,int* trust){*trust=0;return 0;}
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static int GetGeometry(nint self,nint* geometry){*geometry=State(self).Geometry;Marshal.AddRef(*geometry);return 0;}
    [UnmanagedCallersOnly(CallConvs=new[]{typeof(CallConvStdcall)})]
    static int TryGeometry(nint self,nint unused,nint* geometry){*geometry=0;return unchecked((int)0x80004001);}

    [DllImport("d2d1.dll")]static extern int D2D1CreateFactory(uint type,ref Guid iid,nint options,out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreatePath(nint self,out nint geometry);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int OpenPath(nint self,out nint sink);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Begin(nint self,Vector2 point,uint fill);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Lines(nint self,[In]Vector2[] points,uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void End(nint self,uint end);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Close(nint self);
    static T Method<T>(nint self,int index) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),index*IntPtr.Size));
    static nint CreateFactory(){var iid=new Guid("06152247-6F50-465A-9245-118BFD3B6007");Marshal.ThrowExceptionForHR(D2D1CreateFactory(1,ref iid,0,out var value));return value;}
    static readonly Vector2[] sharedPoints=new Vector2[(GeniePose.Rows+1)*2];
    internal static CompositionPath Build(GeniePose pose,double scale)
    {
        const float insetPx=0.75f;
        const float bottomInsetPx=1.5f;
        float topY=(float)(pose.Row(0,true).Y*scale)+insetPx;
        float botY=(float)(pose.Row(GeniePose.RowT(GeniePose.Rows),true).Y*scale)-bottomInsetPx;
        bool collapsed=botY<=topY+0.5f;
        for(int i=0;i<=GeniePose.Rows;i++){
            var row=pose.Row(GeniePose.RowT(i),true);
            float l=(float)(row.Left*scale)+insetPx,r=(float)(row.Right*scale)-insetPx;
            if(r<l){float mid=(l+r)*0.5f;l=r=mid;}
            float y=collapsed?topY:Math.Clamp((float)(row.Y*scale)+(i==0?insetPx:i==GeniePose.Rows?-bottomInsetPx:0f),topY,botY);
            sharedPoints[i]=new(l,y);
            sharedPoints[sharedPoints.Length-1-i]=new(r,y);
        }
        Marshal.ThrowExceptionForHR(Method<CreatePath>(factory,10)(factory,out var geometry));
        nint sink=0;Source? source=null;
        try{
            Marshal.ThrowExceptionForHR(Method<OpenPath>(geometry,17)(geometry,out sink));
            Method<Begin>(sink,5)(sink,sharedPoints[0],0);Method<Lines>(sink,6)(sink,sharedPoints,(uint)sharedPoints.Length);Method<End>(sink,8)(sink,1);Marshal.ThrowExceptionForHR(Method<Close>(sink,9)(sink));
            source=new Source(geometry);var projected=WinRT.MarshalInterface<IGeometrySource2D>.FromAbi(source.Marker);
            return new CompositionPath(projected);
        }finally{if(sink!=0)Marshal.Release(sink);if(source!=null)ReleaseCore(source.Marker);else Marshal.Release(geometry);}
    }
}
