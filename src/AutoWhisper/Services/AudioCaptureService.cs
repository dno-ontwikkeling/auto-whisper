using System.IO;
using NAudio.Wave;

namespace AutoWhisper.Services;

public class AudioCaptureService
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private WaveFileWriter? _writer;
    private readonly object _lock = new();
    private long _totalBytesRecorded;

    public bool IsRecording { get; private set; }

    public void StartRecording(int deviceNumber = 0)
    {
        lock (_lock)
        {
            if (IsRecording) return;

            _totalBytesRecorded = 0;
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
            Logger.Log($"Recording error: {e.Exception.Message}");
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

            if (_audioBuffer is { Length: > 44 }) // >44 means more than just a WAV header
            {
                // Check if there's actual audio content (not just silence)
                _audioBuffer.Position = 44; // skip WAV header
                var samples = new byte[_audioBuffer.Length - 44];
                _audioBuffer.Read(samples, 0, samples.Length);

                // Calculate RMS to detect silence
                double sumSquares = 0;
                for (int i = 0; i < samples.Length - 1; i += 2)
                {
                    short sample = (short)(samples[i] | (samples[i + 1] << 8));
                    sumSquares += sample * (double)sample;
                }
                double rms = Math.Sqrt(sumSquares / (samples.Length / 2));
                Logger.Log($"Audio RMS level: {rms:F0} (silence < 100, speech typically > 500)");

                _audioBuffer.Position = 0;
                return _audioBuffer;
            }

            Logger.Log("Audio buffer too small, discarding.");
            _audioBuffer?.Dispose();
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
}

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
