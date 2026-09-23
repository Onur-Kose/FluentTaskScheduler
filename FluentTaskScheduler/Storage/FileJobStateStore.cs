using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentTaskScheduler.Storage;

/// <summary>
/// Durable state and process-wide exclusive execution on a filesystem supporting exclusive opens and atomic rename.
/// All instances must use the same directory and job keys. Corrupt state is reported, never silently discarded.
/// </summary>
public sealed class FileJobStateStore : IJobStateStore
{
    private readonly string _directory;
    public bool RequiresStableKeys => true;
    public FileJobStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public async ValueTask<IJobStateLease?> TryAcquireAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var path = Path.Combine(_directory, name);
        FileStream handle;
        try { handle = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 or 11) { return null; }
        try
        {
            JobState? state = null;
            if (File.Exists(path + ".json"))
            {
                await using var input = File.OpenRead(path + ".json");
                state = await JsonSerializer.DeserializeAsync(input, JobStateJsonContext.Default.JobState, cancellationToken)
                    ?? throw new InvalidDataException("The stored job state is empty.");
                if (state.FormatVersion != 1)
                    throw new InvalidDataException("Unsupported job state format.");
            }
            return new Lease(handle, path + ".json", state);
        }
        catch { await handle.DisposeAsync(); throw; }
    }

    private sealed class Lease(FileStream handle, string path, JobState? state) : IJobStateLease
    {
        private bool _disposed;
        public JobState? State { get; private set; } = state;
        public async ValueTask SaveAsync(JobState state, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(output, state, JobStateJsonContext.Default.JobState, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
                State = state;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public async ValueTask DisposeAsync()
        {
            if (!_disposed) { _disposed = true; await handle.DisposeAsync(); }
        }
    }
}

[JsonSerializable(typeof(JobState))]
internal partial class JobStateJsonContext : JsonSerializerContext;
