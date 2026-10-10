using System.Text;
using System.Windows.Media.Imaging;

namespace QAFlowRecorder.Services;

/// <summary>
/// Optional, lightweight session video recorder. It stores Motion-JPEG frames in a standard AVI container
/// so QAFlow does not depend on an external encoder. Audio is intentionally not recorded.
/// Designed for short/medium QA sessions; long recordings can consume considerable CPU and disk space.
/// </summary>
public sealed class VideoRecordingService : IAsyncDisposable
{
    private readonly Func<BitmapSource?> _captureFrame;
    private readonly string _outputPath;
    private readonly int _framesPerSecond;
    private readonly int _jpegQuality;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private MjpegAviWriter? _writer;
    private volatile bool _paused;
    private bool _started;

    public VideoRecordingService(Func<BitmapSource?> captureFrame, string outputPath, int framesPerSecond = 4, int jpegQuality = 72)
    {
        _captureFrame = captureFrame ?? throw new ArgumentNullException(nameof(captureFrame));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _framesPerSecond = Math.Clamp(framesPerSecond, 1, 10);
        _jpegQuality = Math.Clamp(jpegQuality, 45, 90);
    }

    public string OutputPath => _outputPath;
    public bool IsRunning => _started && !_cts.IsCancellationRequested;
    public bool IsPaused => _paused;

    public Task StartAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        _loopTask = Task.Run(RecordLoopAsync);
        return Task.CompletedTask;
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public async Task<string?> StopAsync()
    {
        if (!_started) return null;
        try { _cts.Cancel(); } catch { }
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        try { _writer?.Dispose(); }
        catch (Exception ex) { TechnicalLogService.Warn(nameof(VideoRecordingService), "FinalizeVideo", ex.Message, ex); }
        finally { _writer = null; }

        return File.Exists(_outputPath) && new FileInfo(_outputPath).Length > 1024 ? _outputPath : null;
    }

    private async Task RecordLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000d / _framesPerSecond));
        while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
        {
            if (_paused) continue;
            try
            {
                var frame = _captureFrame();
                if (frame is null) continue;
                if (!frame.IsFrozen && frame.CanFreeze) frame.Freeze();

                var jpg = EncodeJpeg(frame, _jpegQuality);
                if (_writer is null)
                    _writer = new MjpegAviWriter(_outputPath, frame.PixelWidth, frame.PixelHeight, _framesPerSecond);

                if (frame.PixelWidth != _writer.Width || frame.PixelHeight != _writer.Height)
                {
                    TechnicalLogService.Warn(nameof(VideoRecordingService), "FrameSizeChanged",
                        $"Skipped a video frame because its size changed from {_writer.Width}x{_writer.Height} to {frame.PixelWidth}x{frame.PixelHeight}.");
                    continue;
                }

                _writer.WriteFrame(jpg);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                TechnicalLogService.Warn(nameof(VideoRecordingService), "CaptureFrame", ex.Message, ex);
            }
        }
    }

    private static byte[] EncodeJpeg(BitmapSource bitmap, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    /// <summary>
    /// Minimal classic AVI/MJPEG writer. The main header is checkpointed periodically so a recording
    /// interrupted by an unexpected application close has a better chance of remaining readable.
    /// The final index is written during normal Stop/Dispose.
    /// </summary>
    private sealed class MjpegAviWriter : IDisposable
    {
        private const long SoftFileLimit = 1_850_000_000L;
        private readonly FileStream _stream;
        private readonly BinaryWriter _writer;
        private readonly List<IndexEntry> _index = [];
        private readonly int _fps;
        private readonly long _riffSizePos;
        private readonly long _avihFlagsPos;
        private readonly long _avihFramesPos;
        private readonly long _strhLengthPos;
        private readonly long _moviSizePos;
        private readonly long _moviDataStart;
        private int _frameCount;
        private bool _disposed;

        private readonly record struct IndexEntry(uint Offset, uint Size);

        public MjpegAviWriter(string path, int width, int height, int fps)
        {
            Width = width;
            Height = height;
            _fps = fps;
            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            _writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);

            WriteFourCc("RIFF");
            _riffSizePos = _stream.Position;
            _writer.Write(0u);
            WriteFourCc("AVI ");

            WriteFourCc("LIST");
            var hdrlSizePos = _stream.Position;
            _writer.Write(0u);
            var hdrlStart = _stream.Position;
            WriteFourCc("hdrl");

            WriteFourCc("avih");
            _writer.Write(56u);
            _writer.Write((uint)(1_000_000 / Math.Max(1, fps)));
            _writer.Write((uint)Math.Min(uint.MaxValue, (long)width * height * 3L * fps));
            _writer.Write(0u);
            _avihFlagsPos = _stream.Position;
            _writer.Write(0u); // AVIF_HASINDEX set when finalized
            _avihFramesPos = _stream.Position;
            _writer.Write(0u);
            _writer.Write(0u);
            _writer.Write(1u);
            _writer.Write((uint)Math.Min(uint.MaxValue, (long)width * height * 3L));
            _writer.Write((uint)width);
            _writer.Write((uint)height);
            _writer.Write(0u); _writer.Write(0u); _writer.Write(0u); _writer.Write(0u);

            WriteFourCc("LIST");
            var strlSizePos = _stream.Position;
            _writer.Write(0u);
            var strlStart = _stream.Position;
            WriteFourCc("strl");

            WriteFourCc("strh");
            _writer.Write(56u);
            WriteFourCc("vids");
            WriteFourCc("MJPG");
            _writer.Write(0u);
            _writer.Write((ushort)0); _writer.Write((ushort)0);
            _writer.Write(0u);
            _writer.Write(1u);
            _writer.Write((uint)fps);
            _writer.Write(0u);
            _strhLengthPos = _stream.Position;
            _writer.Write(0u);
            _writer.Write((uint)Math.Min(uint.MaxValue, (long)width * height * 3L));
            _writer.Write(uint.MaxValue);
            _writer.Write(0u);
            _writer.Write((short)0); _writer.Write((short)0); _writer.Write((short)Math.Min(short.MaxValue, width)); _writer.Write((short)Math.Min(short.MaxValue, height));

            WriteFourCc("strf");
            _writer.Write(40u);
            _writer.Write(40u);
            _writer.Write(width);
            _writer.Write(height);
            _writer.Write((ushort)1);
            _writer.Write((ushort)24);
            WriteFourCc("MJPG");
            _writer.Write((uint)Math.Min(uint.MaxValue, (long)width * height * 3L));
            _writer.Write(0); _writer.Write(0); _writer.Write(0u); _writer.Write(0u);

            PatchUInt32(strlSizePos, checked((uint)(_stream.Position - strlStart)));
            PatchUInt32(hdrlSizePos, checked((uint)(_stream.Position - hdrlStart)));

            WriteFourCc("LIST");
            _moviSizePos = _stream.Position;
            _writer.Write(0u);
            WriteFourCc("movi");
            _moviDataStart = _stream.Position;
            CheckpointHeaders(flush: true);
        }

        public int Width { get; }
        public int Height { get; }

        public void WriteFrame(byte[] jpeg)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MjpegAviWriter));
            if (_stream.Position + jpeg.Length + 32 >= SoftFileLimit)
                throw new IOException("The optional session video reached the safe AVI size limit. Finish/export this session and start a new recording if more video is required.");

            var chunkStart = _stream.Position;
            WriteFourCc("00dc");
            _writer.Write((uint)jpeg.Length);
            _writer.Write(jpeg);
            if ((jpeg.Length & 1) != 0) _writer.Write((byte)0);

            var relative = checked((uint)(chunkStart - _moviDataStart));
            _index.Add(new IndexEntry(relative, checked((uint)jpeg.Length)));
            _frameCount++;

            if ((_frameCount % Math.Max(1, _fps)) == 0)
                CheckpointHeaders(flush: true);
        }

        private void CheckpointHeaders(bool flush)
        {
            var end = _stream.Position;
            PatchUInt32(_avihFramesPos, (uint)_frameCount);
            PatchUInt32(_strhLengthPos, (uint)_frameCount);
            PatchUInt32(_moviSizePos, checked((uint)(end - (_moviSizePos + 4))));
            PatchUInt32(_riffSizePos, checked((uint)(end - 8)));
            _stream.Position = end;
            if (flush) _stream.Flush(true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                var moviEnd = _stream.Position;
                PatchUInt32(_moviSizePos, checked((uint)(moviEnd - (_moviSizePos + 4))));
                _stream.Position = moviEnd;

                WriteFourCc("idx1");
                _writer.Write(checked((uint)(_index.Count * 16)));
                foreach (var entry in _index)
                {
                    WriteFourCc("00dc");
                    _writer.Write(0x10u);
                    _writer.Write(entry.Offset);
                    _writer.Write(entry.Size);
                }

                var end = _stream.Position;
                PatchUInt32(_avihFlagsPos, 0x10u);
                PatchUInt32(_avihFramesPos, (uint)_frameCount);
                PatchUInt32(_strhLengthPos, (uint)_frameCount);
                PatchUInt32(_riffSizePos, checked((uint)(end - 8)));
                _stream.Position = end;
                _stream.Flush(true);
            }
            finally
            {
                _writer.Dispose();
                _stream.Dispose();
            }
        }

        private void PatchUInt32(long position, uint value)
        {
            var current = _stream.Position;
            _stream.Position = position;
            _writer.Write(value);
            _stream.Position = current;
        }

        private void WriteFourCc(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length != 4) throw new ArgumentException("FOURCC must contain exactly four ASCII characters.", nameof(value));
            _writer.Write(bytes);
        }
    }
}
