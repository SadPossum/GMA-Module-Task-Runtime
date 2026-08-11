namespace Gma.Modules.TaskRuntime.Tests.Persistence;

using System.Diagnostics.Metrics;
using Gma.Framework.Observability;
using Gma.Framework.Runtime;
using Gma.Framework.Runtime.Time;
using Gma.Modules.TaskRuntime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TaskRuntimeRetentionMetricsTests
{
    [Fact]
    public async Task Metrics_report_bounded_terminal_history_lifecycle_state()
    {
        List<MetricMeasurement> measurements = [];
        using MeterListener listener = CreateListener(measurements);
        ServiceCollection services = new();
        services.AddMetrics();
        await using ServiceProvider provider = services.BuildServiceProvider();
        TestClock clock = new(new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.Zero));
        TaskRuntimeRetentionMetrics metrics = new(
            provider.GetRequiredService<IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions()),
            clock);

        metrics.RecordDeleted("run", "Succeeded", 4);
        metrics.RecordFailure("run", "Succeeded");
        metrics.RecordDuration("run", "Succeeded", TimeSpan.FromMilliseconds(18));
        metrics.SetOldestTerminal("run", "Succeeded", clock.UtcNow.AddDays(-2));
        listener.RecordObservableInstruments();

        MetricMeasurement deleted = Assert.Single(
            measurements,
            item => item.InstrumentName == ObservabilityInstrumentNames.TaskRetentionDeleted);
        MetricMeasurement failure = Assert.Single(
            measurements,
            item => item.InstrumentName == ObservabilityInstrumentNames.TaskRetentionFailures);
        MetricMeasurement duration = Assert.Single(
            measurements,
            item => item.InstrumentName == ObservabilityInstrumentNames.TaskRetentionDuration);
        MetricMeasurement oldest = Assert.Single(
            measurements,
            item => item.InstrumentName == ObservabilityInstrumentNames.TaskRetentionOldestTerminalAge);

        Assert.Equal(4, deleted.Value);
        Assert.Equal(1, failure.Value);
        Assert.Equal(18, duration.Value);
        Assert.Equal(172800, oldest.Value);
        Assert.All([deleted, failure, duration, oldest], measurement =>
        {
            Assert.Equal("run", measurement.Tags[ObservabilityTagNames.Operation]);
            Assert.Equal("succeeded", measurement.Tags[ObservabilityTagNames.TaskStatus]);
            Assert.DoesNotContain(
                measurement.Tags.Keys,
                key => key.Contains("scope", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static MeterListener CreateListener(ICollection<MetricMeasurement> measurements)
    {
        MeterListener listener = new()
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == ObservabilityMeterNames.Tasks)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, ToDictionary(tags))));
        listener.Start();
        return listener;
    }

    private static Dictionary<string, object?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private sealed record MetricMeasurement(
        string InstrumentName,
        double Value,
        Dictionary<string, object?> Tags);

    private sealed class TestClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
