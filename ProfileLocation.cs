using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace MacDock;

internal static class ProfileLocation
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateFile(string name,uint access,uint share,nint security,uint creation,uint flags,nint template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder name,uint length,uint flags);

    internal static string PhysicalDirectory(string directory)
    {
        using var handle=CreateFile(directory,0,7,0,3,0x02000000,0);
        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var name=new StringBuilder(32768);
        uint count=GetFinalPathNameByHandle(handle,name,(uint)name.Capacity,0);
        if(count==0||count>=name.Capacity)throw new IOException("无法确认配置目录的实际位置。");
        return name.ToString();
    }
    internal static void RequireUserProfile(string directory)
    {
        // A merged MSIX directory can resolve to the real directory while an
        // individual file inside it resolves to the package's private overlay.
        // Inspect the actual configuration file whenever it exists.
        string file=Path.Combine(directory,"config.json");
        string physical=PhysicalDirectory(File.Exists(file)?file:directory);
        // MSIX launchers can transparently substitute their own private copy of
        // LocalAppData even though the logical filename stays unchanged. Never
        // load/save a second layout under the launching application's package.
        if(physical.Contains("\\Packages\\",StringComparison.OrdinalIgnoreCase)&&
           physical.Contains("\\LocalCache\\Local\\",StringComparison.OrdinalIgnoreCase)&&
           !directory.Contains("\\Packages\\",StringComparison.OrdinalIgnoreCase))
            throw new IOException("启动环境将 MacDock 配置重定向到了其他应用的私有目录。为保护现有布局，请从 Windows 桌面或开始菜单启动 MacDock。");
    }
}
