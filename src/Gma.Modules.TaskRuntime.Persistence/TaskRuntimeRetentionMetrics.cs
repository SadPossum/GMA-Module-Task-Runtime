namespace Gma.Modules.TaskRuntime.Persistence;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Gma.Framework.Observability;
using Gma.Framework.Observability.Infrastructure;
using Gma.Framework.Runtime;
using Gma.Framework.Runtime.Time;
using Microsoft.Extensions.Options;

internal sealed class TaskRuntimeRetentionMetrics
{
    private readonly Counter<long> deleted;
    private readonly Counter<long> failures;
    private readonly Histogram<double> duration;
    private readonly ConcurrentDictionary<HistoryIdentity, DateTimeOffset> oldestTerminal = new();
    private readonly ISystemClock clock;

    public TaskRuntimeRetentionMetrics(
        IMeterFactory meterFactory,
        IOptions<ApplicationIdentityOptions> applicationIdentity,
        ISystemClock clock)
    {
        this.clock = clock;
        string applicationNamespace = applicationIdentity.Value.EffectiveNamespace;
        Meter meter = meterFactory.Create(ObservabilityMeterNames.TasksFor(applicationNamespace));
        this.deleted = meter.CreateCounter<long>(
            ObservabilityInstrumentNames.TaskRetentionDeletedFor(applicationNamespace),
            unit: "{record}",
            description: "Number of terminal task runtime records deleted by retention cleanup.");
        this.failures = meter.CreateCounter<long>(
            ObservabilityInstrumentNames.TaskRetentionFailuresFor(applicationNamespace),
            unit: "{failure}",
            description: "Number of failed task runtime retention cleanup attempts.");
        this.duration = meter.CreateHistogram<double>(
            ObservabilityInstrumentNames.TaskRetentionDurationFor(applicationNamespace),
            unit: "ms",
            description: "Duration of one task runtime retention cleanup attempt.");
        meter.CreateObservableGauge(
            ObservabilityInstrumentNames.TaskRetentionOldestTerminalAgeFor(applicationNamespace),
            this.ObserveOldestTerminalAge,
            unit: "s",
            description: "Age of the oldest retained eligible terminal task runtime record.");
    }

    public void RecordDeleted(string recordKind, string status, int count)
    {
        if (count > 0)
        {
            this.deleted.Add(count, CreateTags(recordKind, status));
        }
    }

    public void RecordFailure(string recordKind, string status) =>
        this.failures.Add(1, CreateTags(recordKind, status));

    public void RecordDuration(string recordKind, string status, TimeSpan elapsed) =>
        this.duration.Record(Math.Max(0, elapsed.TotalMilliseconds), CreateTags(recordKind, status));

    public void SetOldestTerminal(
        string recordKind,
        string status,
        DateTimeOffset? completedAtUtc)
    {
        HistoryIdentity identity = CreateIdentity(recordKind, status);
        if (completedAtUtc is null)
        {
            this.oldestTerminal.TryRemove(identity, out _);
            return;
        }

        this.oldestTerminal[identity] = completedAtUtc.Value;
    }

    private IEnumerable<Measurement<double>> ObserveOldestTerminalAge()
    {
        DateTimeOffset nowUtc = this.clock.UtcNow;
        foreach (KeyValuePair<HistoryIdentity, DateTimeOffset> pair in this.oldestTerminal)
        {
            yield return new Measurement<double>(
                Math.Max(0, (nowUtc - pair.Value).TotalSeconds),
                CreateTags(pair.Key.RecordKind, pair.Key.Status));
        }
    }

    private static TagList CreateTags(string recordKind, string status)
    {
        HistoryIdentity identity = CreateIdentity(recordKind, status);
        return new TagList
        {
            { ObservabilityTagNames.Operation, identity.RecordKind },
            { ObservabilityTagNames.TaskStatus, identity.Status },
        };
    }

    private static HistoryIdentity CreateIdentity(string recordKind, string status) =>
        new(
            MetricTagValues.Operation(recordKind),
            MetricTagValues.Operation(status).ToLowerInvariant());

    private readonly record struct HistoryIdentity(string RecordKind, string Status);
}
