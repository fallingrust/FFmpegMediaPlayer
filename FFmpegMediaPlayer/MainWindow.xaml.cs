using System;
using System.Windows;
using System.IO;
using MediaPlayer;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Shell;
using System.Drawing;
using System.Drawing.Imaging;

namespace FFmpegMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        int rotate = 0;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void VideoView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
           
            // videoView.Shot(Environment.CurrentDirectory, "test");
        }

        private void add_Click(object sender, RoutedEventArgs e)
        {
            foreach (UIElement item in mainGrid.Children)
            {
                if (item is VideoView)
                {
                    
                    var videoView = (item as VideoView);
                    if (!videoView.isPlaying)
                    {
                        var current = Environment.CurrentDirectory;
                        var probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");
                        var ffmpegBinaryPath = Path.Combine(current, probe);
                        videoView.Init("rtsp://admin:zhibo2020@10.10.10.110:554/h264/ch1/main/av_stream", true, ffmpegBinaryPath);
                        videoView.Start();
                        break;
                    }                
                }
            }
        }
    }
}
