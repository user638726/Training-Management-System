using System.Diagnostics.Metrics;
namespace TrainingManagement.Api.Infrastructure;
public sealed class ApplicationHealth : IDisposable
{
    private readonly Meter meter = new("TrainingManagement.Api", "1.0");
    private readonly Counter<long> requests;
    private readonly Histogram<double> duration;
    private long requestCount, errorCount, lastJobTicks;
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public ApplicationHealth() { requests = meter.CreateCounter<long>("training.http.requests"); duration = meter.CreateHistogram<double>("training.http.duration", "ms"); }
    public void Observe(int status, double milliseconds)
    {
        Interlocked.Increment(ref requestCount); if (status >= 500) Interlocked.Increment(ref errorCount);
        requests.Add(1, new KeyValuePair<string, object?>("status", status)); duration.Record(milliseconds);
    }
    public void JobSucceeded() => Interlocked.Exchange(ref lastJobTicks, DateTime.UtcNow.Ticks);
    public object Snapshot() { var ticks = Interlocked.Read(ref lastJobTicks); return new { startedAt = StartedAt,
        requests = Interlocked.Read(ref requestCount), serverErrors = Interlocked.Read(ref errorCount),
        lastJobSuccess = ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc) }; }
    public void Dispose() => meter.Dispose();
}
