
using SharpDX.Direct3D9;
using SharpDX.Mathematics.Interop;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static FFmpegLib.FFmpeg;

namespace MediaPlayer
{
    /// <summary>
    /// VideoView.xaml 的交互逻辑
    /// </summary>
    public partial class VideoView : UserControl
    {
        public FFmpegLib.FFmpeg FFmpeg;

        private DataCallback dataCallback;
        private ErrorCallback errorCallback;
        private WriteableBitmap writableBitmap;
        private int Rotate = 0;
        public bool isPlaying = false;

        public VideoView()
        {
            InitializeComponent();           
            FFmpeg = getInstance();
          
            dataCallback = (data, width, height) =>
            {                          
                    Application.Current.Dispatcher.Invoke(() =>
                    {

                        using MemoryStream memory = new MemoryStream();
                        if (writableBitmap == null)
                        {
                            writableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, BitmapPalettes.Gray256);

                        }
                        writableBitmap.Lock();
                        unsafe
                        {
                            if (data == IntPtr.Zero)
                            {
                                return;
                            }
                            NativeMethods.CopyMemory(writableBitmap.BackBuffer, data, (uint)(width * height * 3));
                        }
                        //指定更改位图的区域
                        writableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                        writableBitmap.Unlock();
                        image.Source = writableBitmap;
                        image.RenderTransform = new RotateTransform(Rotate, image.ActualWidth / 2, image.ActualHeight / 2);

                    });
                
               
            };
            errorCallback = (message) =>
            {
                Console.WriteLine(message);
            };
            
        }

     
        public static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = false)]
            public static extern IntPtr GetDesktopWindow();
            [DllImport("kernel32.dll", EntryPoint = "RtlCopyMemory", SetLastError = false)]
            public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);
          
           
        }
        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="url">地址</param>
        /// <param name="hw">是否开启硬件加速</param>
        /// <param name="ffmepgPath">FFmpeg dll路径</param>
        /// <returns></returns>
        public bool Init(string url, bool hw, string ffmepgPath)
        {
            if (FFmpeg == null) return false;
            return FFmpeg.Init(dataCallback, errorCallback, url, hw, ffmepgPath) == 0;
        }

        /// <summary>
        /// 调整旋转角度
        /// </summary>
        /// <param name="rotate"></param>
        public void SetRotate(int rotate)
        {
            Rotate = rotate;
        } 
       
        /// <summary>
        /// 开始播放
        /// </summary>
        public void Start()
        {
            if(FFmpeg != null)
            {
                FFmpeg.Start();
                isPlaying = true;
            }
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        public void Stop()
        {
            if (FFmpeg != null)
            {
                FFmpeg.Stop();
                isPlaying = false;
            }
        }
        /// <summary>
        /// 截图
        /// </summary>
        /// <param name="path">保存路径</param>
        /// <param name="fileName">文件名称</param>
        public void Shot(string path,string fileName)
        {
            var encode = new PngBitmapEncoder();
            encode.Frames.Add(BitmapFrame.Create((BitmapSource)image.Source));
            using FileStream fs = new FileStream(Path.Combine(path, fileName + ".png"), FileMode.Create);
            encode.Save(fs); 
            fs.Flush();
            fs.Close();
        }
        [DllImport("user32.dll")]
        public static extern int GetClientRect(IntPtr hWnd, out RECT lpRect);
        // 矩形结构
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
    }
}
