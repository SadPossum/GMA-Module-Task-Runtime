namespace Gma.Modules.TaskRuntime.Persistence;

public sealed class TaskRuntimeRetentionOptions
{
    public const string SectionName = "TaskRuntimeRetention";

    public bool Enabled { get; set; }
    public TimeSpan SucceededRunRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan FailedRunRetention { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan CanceledRunRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan TimedOutRunRetention { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan HandledControlRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan FailedControlRetention { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan ExpiredControlRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
    public int BatchSize { get; set; } = 500;
    public int MaxBatchesPerStatusPerCycle { get; set; } = 10;
}
