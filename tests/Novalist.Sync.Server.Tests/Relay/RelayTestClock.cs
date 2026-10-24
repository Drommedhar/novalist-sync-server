using System.Threading.Channels;

namespace Novalist.Sync.Server.Tests.Relay;

internal sealed class RelayTestClock : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ClockTimer> timers = new();
    private readonly Channel<TimeSpan> scheduled = Channel.CreateUnbounded<TimeSpan>();
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() { lock (gate) return now; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ClockTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public Task<TimeSpan> NextDelayAsync() => scheduled.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    public void Advance(TimeSpan duration)
    {
        ClockTimer[] ready;
        lock (gate)
        {
            now += duration;
            ready = timers.Where(timer => timer.Due <= now).ToArray();
            foreach (var timer in ready) timer.Due = timer.Period > TimeSpan.Zero ? now + timer.Period : DateTimeOffset.MaxValue;
        }
        foreach (var timer in ready) timer.Fire();
    }

    private sealed class ClockTimer(RelayTestClock clock, TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;
        public DateTimeOffset Due { get; set; }
        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.gate)
            {
                if (disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime;
                Period = period;
                if (!clock.timers.Contains(this)) clock.timers.Add(this);
                clock.scheduled.Writer.TryWrite(dueTime);
                return true;
            }
        }

        public void Fire() { lock (clock.gate) if (!disposed) callback(state); }
        public void Dispose() { lock (clock.gate) { disposed = true; clock.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
