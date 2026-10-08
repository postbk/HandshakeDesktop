using NAudio.Wave;

namespace Handshake.Desktop.Services;

public sealed class AudioMemoService : IDisposable
{
    private WaveInEvent? _input;
    private WaveFileWriter? _writer;
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handshake", "memos");
    public bool IsRecording => _input is not null;
    public string? CurrentPath { get; private set; }

    public void Start()
    {
        if (IsRecording) return;
        Directory.CreateDirectory(_folder);
        CurrentPath = Path.Combine(_folder, $"memo-{DateTime.UtcNow:yyyyMMdd-HHmmss}.wav");
        _input = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
        _writer = new WaveFileWriter(CurrentPath, _input.WaveFormat);
        _input.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _input.RecordingStopped += (_, _) => { _writer?.Dispose(); _writer = null; _input?.Dispose(); _input = null; };
        _input.StartRecording();
    }

    public string? Stop()
    {
        if (!IsRecording) return CurrentPath;
        _input!.StopRecording();
        return CurrentPath;
    }

    public void Dispose() { if (IsRecording) Stop(); _writer?.Dispose(); _input?.Dispose(); }
}
