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

    public bool IsRecording { get; private set; }

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

    public MemoryStream? StopRecording()
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
                var result = _audioBuffer;
                _audioBuffer = null;
                return null;
            }

            if (_audioBuffer is { Length: > 44 }) // >44 means more than just a WAV header
            {
                // Calculate RMS using streaming approach — no large allocation
                _audioBuffer.Position = 44; // skip WAV header
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
                Logger.Log($"Audio RMS level: {rms:F0} (silence < 100, speech typically > 500)");

                if (rms < 100)
                {
                    Logger.Log("Audio below silence threshold, discarding.");
                    _audioBuffer.Dispose();
                    _audioBuffer = null;
                    return null;
                }

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
