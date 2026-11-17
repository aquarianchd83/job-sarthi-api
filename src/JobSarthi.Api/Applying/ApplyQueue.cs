using System.Collections.Concurrent;
using System.Threading.Channels;

namespace JobSarthi.Api.Applying;

public record ApplyRequest(int ApplicationId, bool Submit);

/// <summary>Applications run one at a time, in order. An application already waiting or running is not queued twice.</summary>
public class ApplyQueue
{
    private readonly Channel<ApplyRequest> _channel = Channel.CreateUnbounded<ApplyRequest>(new() { SingleReader = true });
    private readonly ConcurrentDictionary<int, bool> _active = new();

    public bool Enqueue(int applicationId, bool submit)
    {
        if (!_active.TryAdd(applicationId, true)) return false;
        _channel.Writer.TryWrite(new ApplyRequest(applicationId, submit));
        return true;
    }

    public bool IsActive(int applicationId) => _active.ContainsKey(applicationId);
    public void Done(int applicationId) => _active.TryRemove(applicationId, out _);
    public IAsyncEnumerable<ApplyRequest> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
