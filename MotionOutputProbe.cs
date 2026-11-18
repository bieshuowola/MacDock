using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MacDock;

// Independent screen pixels. Frame times are capture times, not display presents.
internal sealed class MotionOutputProbe:IDisposable
{
    readonly Thread worker;
    volatile bool stopped;
    internal MotionOutputProbe(string folder,Stopwatch clock,Rectangle area)
    {
        worker=new Thread(()=>Sample(folder,clock,area)){IsBackground=true,Name="MacDock desktop pixel sampler"};
        worker.Start();
    }
    void Sample(string folder,Stopwatch clock,Rectangle area)
    {
        const int step=8;
        bool wide=Environment.GetEnvironmentVariable("MACDOCK_MOTION_WIDE")=="1";
        int width=wide?1360:1200,height=wide?1120:960;
        int left=area.Left+(area.Width-width)/2,top=area.Bottom-height;
        try
        {
            using var image=new Bitmap(width,height,PixelFormat.Format32bppArgb);
            using var graphics=Graphics.FromImage(image);
            using var output=new BinaryWriter(File.Create(Path.Combine(folder,"desktop-samples.bin")));
            using var edges=new BinaryWriter(File.Create(Path.Combine(folder,"edge-samples.bin")));
            var pixels=new byte[width*height*4];
            var sampled=new byte[width/step*(height/step)];
            // Panel ends at work bottom minus the settled Dock height and gaps.
            // Keep a small full-resolution strip to distinguish an extra horizontal line.
            var edge=new byte[170];
            while(!stopped)
            {
                graphics.CopyFromScreen(left,top,0,0,image.Size);
                double time=clock.Elapsed.TotalMilliseconds;
                var locked=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try{Marshal.Copy(locked.Scan0,pixels,0,pixels.Length);}
                finally{image.UnlockBits(locked);}
                byte Luma(int x,int y){int i=(y*width+x)*4;return (byte)((29*pixels[i]+150*pixels[i+1]+77*pixels[i+2])>>8);}
                int n=0;
                for(int y=0;y<height;y+=step)for(int x=0;x<width;x+=step)sampled[n++]=Luma(x,y);
                for(int y=0;y<edge.Length;y++)
                {
                    int total=0;for(int x=120;x<280;x+=4)total+=Luma(x,height-230+y);
                    edge[y]=(byte)(total/40);
                }
                output.Write(time);output.Write(sampled);
                edges.Write(time);edges.Write(edge);
                Thread.Sleep(6);
            }
        }
        catch(Exception error){File.WriteAllText(Path.Combine(folder,"desktop-sampler-error.txt"),error.ToString());}
    }
    public void Dispose(){stopped=true;worker.Join(3000);}
}
