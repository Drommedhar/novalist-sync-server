using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace Novalist.Sync.Server.Relay;

public sealed class RelaySessions
{
    private readonly ConcurrentDictionary<Guid, RelaySession> sessions = new();

    internal RelaySession Create(string issuer, string subject)
    {
        var session = new RelaySession(issuer, subject);
        if (!sessions.TryAdd(session.Id, session)) throw new InvalidOperationException("Relay session collision.");
        return session;
    }

    internal RelaySession? Find(Guid id) => sessions.GetValueOrDefault(id);
    internal void Remove(RelaySession session)
    {
        sessions.TryRemove(session.Id, out _);
        session.Stop.Cancel();
        session.Requests.Writer.TryComplete();
    }
}

internal sealed class RelaySession(string issuer, string subject)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Issuer { get; } = issuer;
    public string Subject { get; } = subject;
    public CancellationTokenSource Stop { get; } = new();
    public ConcurrentDictionary<Guid, RelayRoute> Routes { get; } = new();
    public Channel<Guid> Requests { get; } = Channel.CreateBounded<Guid>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
}

internal sealed class RelayRoute
{
    public TaskCompletionSource<WebSocket> Host { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
