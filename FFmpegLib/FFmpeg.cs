using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace FFmpegLib
{
    public unsafe class FFmpeg
    {
        public  delegate void DataCallback(IntPtr dataPtr, int width, int height);
        public delegate void ErrorCallback(string message);
        public  string Url;
        public  bool Hw = false;
        public  string FFmpegBinaryPath;

        private  DataCallback dataCallback;
        private  ErrorCallback errorCallback;
        public  bool IsRunning = false;

        private static FFmpeg mFFmpeg;
        public static FFmpeg getInstance()
        {
            mFFmpeg = new FFmpeg();

            return mFFmpeg;
        }
        
        public unsafe int Init(DataCallback data,ErrorCallback error,string url ,bool hw,string ffmepgPath)
        {
            if (string.IsNullOrEmpty(url)) return -1;
            Url = url;

            Hw = hw;

            if (string.IsNullOrEmpty(ffmepgPath)) return -1;
            FFmpegBinaryPath = ffmepgPath;

            if (data == null) return -1;
            dataCallback = data;

            if (error == null) return -1;
            errorCallback = error;

            if (Directory.Exists(FFmpegBinaryPath))
            {
                ffmpeg.RootPath = FFmpegBinaryPath;
            }
            else
            {
                error($"Could not find FFmpeg binaries in {FFmpegBinaryPath}");
                return -1;
            }

            return 0;
        }


        public void Start()
        {
            if(IsRunning == true)
            {
                errorCallback("thread is already running!");
                return;
            }
            IsRunning = true;
            Thread thread = new Thread(FFmpegStart);
            thread.Start();     
        }
        public void Stop()
        {
            if(IsRunning == true)
            {
                IsRunning = false;
            }
        }

        private unsafe void FFmpegStart()
        {
            ffmpeg.avcodec_register_all();
            ffmpeg.avformat_network_init();

            ConfigureHWDecoder(out var deviceType);
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
            var _formatContext = ffmpeg.avformat_alloc_context();
            int error;
            error = ffmpeg.avformat_open_input(&_formatContext, Url, null, null);
            if (error != 0) throw new ApplicationException(GetErrorMessage(error));

            error = ffmpeg.avformat_find_stream_info(_formatContext, null);
            if (error != 0) throw new ApplicationException(GetErrorMessage(error));

            AVCodec* codec = null;
            int _streamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
            AVCodecContext* _avcodecCtx = ffmpeg.avcodec_alloc_context3(codec);
            if (deviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                error = ffmpeg.av_hwdevice_ctx_create(&_avcodecCtx->hw_device_ctx, deviceType, null, null, 0);
                if (error != 0) throw new ApplicationException(GetErrorMessage(error));
            }

            error = ffmpeg.avcodec_parameters_to_context(_avcodecCtx, _formatContext->streams[_streamIndex]->codecpar);
            if (error != 0) throw new ApplicationException(GetErrorMessage(error));


            var width = _avcodecCtx->width;
            var height = _avcodecCtx->height;
            var sourcePixFmt = _avcodecCtx->pix_fmt;

            // 得到编码器ID
            var codecId = _avcodecCtx->codec_id;
            // 目标像素格式
            var destinationPixFmt = AVPixelFormat.AV_PIX_FMT_BGR24;


            // 某些264格式codecContext.pix_fmt获取到的格式是AV_PIX_FMT_NONE 统一都认为是YUV420P
            if (sourcePixFmt == AVPixelFormat.AV_PIX_FMT_NONE && codecId == AVCodecID.AV_CODEC_ID_H264)
            {
                sourcePixFmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            }

            // 得到SwsContext对象：用于图像的缩放和转换操作
            var pConvertContext = ffmpeg.sws_getContext(width, height, sourcePixFmt,
                width, height, destinationPixFmt,
                ffmpeg.SWS_FAST_BILINEAR, null, null, null);
            if (pConvertContext == null) throw new ApplicationException(@"Could not initialize the conversion context.");

            //分配一个默认的帧对象:AVFrame
            var pConvertedFrame = ffmpeg.av_frame_alloc();
            // 目标媒体格式需要的字节长度
            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixFmt, width, height, 1);
            // 分配目标媒体格式内存使用
            var convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            var dstData = new byte_ptrArray4();
            var dstLinesize = new int_array4();
            // 设置图像填充参数
            ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, (byte*)convertedFrameBufferPtr, destinationPixFmt, width, height, 1);

            var pCodec = ffmpeg.avcodec_find_decoder(codecId);
            if (pCodec == null) throw new ApplicationException(@"Unsupported codec.");

            if ((pCodec->capabilities & ffmpeg.AV_CODEC_CAP_TRUNCATED) == ffmpeg.AV_CODEC_CAP_TRUNCATED)
                _avcodecCtx->flags |= ffmpeg.AV_CODEC_FLAG_TRUNCATED;

            // 通过解码器打开解码器上下文:AVCodecContext pCodecContext
            error = ffmpeg.avcodec_open2(_avcodecCtx, pCodec, null);
            if (error < 0) throw new ApplicationException(GetErrorMessage(error));

            // 分配解码帧对象：AVFrame pDecodedFrame
            var pDecodedFrame = ffmpeg.av_frame_alloc();
           
            // 初始化媒体数据包
            var packet = new AVPacket();
            var pPacket = &packet;
            ffmpeg.av_init_packet(pPacket);

            var frameNumber = 0;
            while (true)
            {
                try
                {
                    do
                    {
                        // 读取一帧未解码数据
                        error = ffmpeg.av_read_frame(_formatContext, pPacket);
                       // Debug.WriteLine(pPacket->dts);
                        if (error == ffmpeg.AVERROR_EOF) break;
                        if (error < 0) throw new ApplicationException(GetErrorMessage(error));

                        if (pPacket->stream_index != _streamIndex) continue;

                        // 解码
                        error = ffmpeg.avcodec_send_packet(_avcodecCtx, pPacket);
                        if (error < 0) throw new ApplicationException(GetErrorMessage(error));
                        // 解码输出解码数据
                        error = ffmpeg.avcodec_receive_frame(_avcodecCtx, pDecodedFrame);
                    } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));
                    if (error == ffmpeg.AVERROR_EOF) break;
                    if (error < 0) throw new ApplicationException(GetErrorMessage(error));

                    if (pPacket->stream_index != _streamIndex) continue;              

                    //yuv 420 to bgr24
                    ffmpeg.sws_scale(pConvertContext, pDecodedFrame->data, pDecodedFrame->linesize, 0, height, dstData, dstLinesize);
                }
                finally
                {
                    ffmpeg.av_packet_unref(pPacket);//释放数据包对象引用
                    ffmpeg.av_frame_unref(pDecodedFrame);//释放解码帧对象引用
                }
               
                // 封装Bitmap图片   

                dataCallback(convertedFrameBufferPtr, width, height);
                // 回调


                frameNumber++;
            }
            //播放完置空播放图片 
            dataCallback(IntPtr.Zero, 0, 0);



            Marshal.FreeHGlobal(convertedFrameBufferPtr);
            ffmpeg.av_free(pConvertedFrame);
            ffmpeg.sws_freeContext(pConvertContext);

            ffmpeg.av_free(pDecodedFrame);
            ffmpeg.avcodec_close(_avcodecCtx);
            ffmpeg.avformat_close_input(&_formatContext);
        }


        private  void ConfigureHWDecoder(out AVHWDeviceType HWtype)
        {
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            if (!Hw)
            {
                return;
            }
            Debug.WriteLine("Use hardware acceleration for decoding?[n]");

            var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();

            Debug.WriteLine("Select hardware decoder:");
            var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            var number = 0;
            while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                Debug.WriteLine($"{++number}. {type}");
                availableHWDecoders.Add(number, type);
            }
            if (availableHWDecoders.Count == 0)
            {
                Debug.WriteLine("Your system have no hardware decoders.");
                HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                return;
            }
            int decoderNumber = availableHWDecoders.SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
            if (decoderNumber == 0)
                decoderNumber = availableHWDecoders.First().Key;
            availableHWDecoders.TryGetValue(decoderNumber, out HWtype);

        }
        private static unsafe string GetErrorMessage(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }
    }
}
