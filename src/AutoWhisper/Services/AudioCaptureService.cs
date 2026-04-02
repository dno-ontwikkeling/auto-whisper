using System.IO;
using NAudio.Wave;

namespace AutoWhisper.Services;

public class AudioCaptureService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private WaveFileWriter? _writer;
    private readonly object _lock = new();
    private long _totalBytesRecorded;
    private Exception? _recordingError;

    // Preview mode — lightweight mic test without buffering audio
    private WaveInEvent? _previewWaveIn;
    private volatile float _latestRms;

    public bool IsRecording { get; private set; }
    public bool IsPreviewing { get; private set; }
    public float LatestRms => _latestRms;

    public void StartRecording(int deviceNumber = 0)
    {
        lock (_lock)
        {
            if (IsRecording) return;

            _totalBytesRecorded = 0;
            _recordingError = null;
            _audioBuffer = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(rate: 16000, bits: 16, channels: 1),
                BufferMilliseconds = 50
            };

            _writer = new WaveFileWriter(new IgnoreDisposeStream(_audioBuffer), _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            IsRecording = true;

            Logger.Log($"Recording started. Device={deviceNumber}, Format=16kHz/16bit/mono");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_writer is null) return;
            _writer.Write(e.Buffer, 0, e.BytesRecorded);
            _totalBytesRecorded += e.BytesRecorded;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Logger.Log($"Recording error: {e.Exception}");
            _recordingError = e.Exception;
        }
    }

    public MemoryStream? StopRecording(int silenceThreshold = 200, bool normalize = false)
    {
        lock (_lock)
        {
            if (!IsRecording) return null;

            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;

            // Finalize WAV headers
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            IsRecording = false;

            Logger.Log($"Recording stopped. Total audio bytes={_totalBytesRecorded}, Stream length={_audioBuffer?.Length ?? 0}");

            // If a recording error occurred, discard the buffer
            if (_recordingError is not null)
            {
                Logger.Log("Discarding audio buffer due to recording error.");
                _audioBuffer?.Dispose();
                _audioBuffer = null;
                return null;
            }

            if (_audioBuffer is { Length: > 44 }) // >44 means more than just a WAV header
            {
                // Find the actual start of PCM data by locating the "data" chunk
                int dataOffset = FindDataChunkOffset(_audioBuffer);

                // Calculate RMS using streaming approach — no large allocation
                _audioBuffer.Position = dataOffset;
                double sumSquares = 0;
                long sampleCount = 0;
                var chunk = new byte[4096];
                int bytesRead;
                while ((bytesRead = _audioBuffer.Read(chunk, 0, chunk.Length)) > 0)
                {
                    for (int i = 0; i < bytesRead - 1; i += 2)
                    {
                        short sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                        sumSquares += sample * (double)sample;
                        sampleCount++;
                    }
                }
                double rms = sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0;
                Logger.Log($"Audio RMS level: {rms:F0} (threshold={silenceThreshold}, speech typically > 500)");

                if (rms < silenceThreshold)
                {
                    Logger.Log("Audio below silence threshold, discarding.");
                    _audioBuffer.Dispose();
                    _audioBuffer = null;
                    return null;
                }

                // Normalize audio to consistent level for Whisper (~-14 dBFS, linear 0.20)
                if (normalize)
                    NormalizeAudio(rms, dataOffset);

                _audioBuffer.Position = 0;
                var buffer = _audioBuffer;
                _audioBuffer = null; // transfer ownership to caller
                return buffer;
            }

            Logger.Log("Audio buffer too small, discarding.");
            _audioBuffer?.Dispose();
            _audioBuffer = null;
            return null;
        }
    }

    private void NormalizeAudio(double currentRms, int dataOffset)
    {
        const double targetRms = 0.20 * 32768; // ~-14 dBFS
        const double maxGain = 6.0;

        if (currentRms < 1.0) return; // near-silence, don't amplify

        double gain = Math.Min(targetRms / currentRms, maxGain);
        if (Math.Abs(gain - 1.0) < 0.05) return; // close enough, skip

        Logger.Log($"Normalizing audio: gain={gain:F2}x (current RMS={currentRms:F0}, target={targetRms:F0})");

        // Rebuild the stream with normalized samples to ensure WAV integrity
        var src = _audioBuffer!;
        var dest = new MemoryStream((int)src.Length);

        // Copy WAV header as-is
        src.Position = 0;
        var header = new byte[dataOffset];
        src.Read(header, 0, dataOffset);
        dest.Write(header, 0, dataOffset);

        // Normalize PCM samples
        var chunk = new byte[4096];
        int bytesRead;
        while ((bytesRead = src.Read(chunk, 0, chunk.Length)) > 0)
        {
            for (int i = 0; i < bytesRead - 1; i += 2)
            {
                short sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                int scaled = Math.Clamp((int)(sample * gain), short.MinValue, short.MaxValue);
                chunk[i] = (byte)(scaled & 0xFF);
                chunk[i + 1] = (byte)((scaled >> 8) & 0xFF);
            }
            dest.Write(chunk, 0, bytesRead);
        }

        src.Dispose();
        _audioBuffer = dest;
    }

    /// <summary>
    /// Parses the WAV header to find the byte offset where the "data" chunk begins.
    /// </summary>
    private static int FindDataChunkOffset(MemoryStream stream)
    {
        stream.Position = 12; // skip RIFF header (4) + size (4) + WAVE (4)
        var buf = new byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            stream.Read(buf, 0, 8);
            int chunkSize = buf[4] | (buf[5] << 8) | (buf[6] << 16) | (buf[7] << 24);

            if (buf[0] == 'd' && buf[1] == 'a' && buf[2] == 't' && buf[3] == 'a')
                return (int)stream.Position;

            stream.Position += chunkSize;
        }

        // Fallback if "data" chunk not found
        Logger.Log("WAV 'data' chunk not found, falling back to offset 44");
        return 44;
    }

    public void StartPreview(int deviceNumber = 0)
    {
        if (IsPreviewing) return;

        _latestRms = 0;
        _previewWaveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(rate: 16000, bits: 16, channels: 1),
            BufferMilliseconds = 50
        };
        _previewWaveIn.DataAvailable += OnPreviewDataAvailable;
        _previewWaveIn.StartRecording();
        IsPreviewing = true;
    }

    public void StopPreview()
    {
        if (!IsPreviewing) return;

        _previewWaveIn?.StopRecording();
        _previewWaveIn?.Dispose();
        _previewWaveIn = null;
        IsPreviewing = false;
        _latestRms = 0;
    }

    private void OnPreviewDataAvailable(object? sender, WaveInEventArgs e)
    {
        double sumSquares = 0;
        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            sumSquares += sample * (double)sample;
        }
        float rms = sampleCount > 0 ? (float)Math.Sqrt(sumSquares / sampleCount) : 0f;
        _latestRms = rms;
    }

    public static List<string> GetAvailableDevices()
    {
        var devices = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(caps.ProductName);
        }
        return devices;
    }

    public void Dispose()
    {
        StopPreview();
        lock (_lock)
        {
            _writer?.Dispose();
            _waveIn?.Dispose();
            _audioBuffer?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Wraps a stream to suppress disposal. Required because NAudio's WaveFileWriter
/// calls Dispose() on its underlying stream, which would close the MemoryStream
/// before we can read the recorded audio back.
/// </summary>
internal class IgnoreDisposeStream : Stream
{
    private readonly Stream _inner;

    public IgnoreDisposeStream(Stream inner) => _inner = inner;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        // Do not dispose the inner stream
    }
}
