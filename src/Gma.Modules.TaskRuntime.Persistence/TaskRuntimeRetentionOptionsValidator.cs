namespace Gma.Modules.TaskRuntime.Persistence;

using Microsoft.Extensions.Options;

internal sealed class TaskRuntimeRetentionOptionsValidator : IValidateOptions<TaskRuntimeRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, TaskRuntimeRetentionOptions options)
    {
        List<string> failures = [];

        ValidateRetention(options.SucceededRunRetention, nameof(options.SucceededRunRetention), failures);
        ValidateRetention(options.FailedRunRetention, nameof(options.FailedRunRetention), failures);
        ValidateRetention(options.CanceledRunRetention, nameof(options.CanceledRunRetention), failures);
        ValidateRetention(options.TimedOutRunRetention, nameof(options.TimedOutRunRetention), failures);
        ValidateRetention(options.HandledControlRetention, nameof(options.HandledControlRetention), failures);
        ValidateRetention(options.FailedControlRetention, nameof(options.FailedControlRetention), failures);
        ValidateRetention(options.ExpiredControlRetention, nameof(options.ExpiredControlRetention), failures);

        if (options.CleanupInterval < TimeSpan.FromSeconds(1))
        {
            failures.Add($"{TaskRuntimeRetentionOptions.SectionName}:CleanupInterval must be at least one second.");
        }

        if (options.BatchSize is < 1 or > 10_000)
        {
            failures.Add($"{TaskRuntimeRetentionOptions.SectionName}:BatchSize must be between 1 and 10000.");
        }

        if (options.MaxBatchesPerStatusPerCycle is < 1 or > 1_000)
        {
            failures.Add(
                $"{TaskRuntimeRetentionOptions.SectionName}:MaxBatchesPerStatusPerCycle must be between 1 and 1000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRetention(TimeSpan value, string propertyName, List<string> failures)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{TaskRuntimeRetentionOptions.SectionName}:{propertyName} must be positive.");
        }
    }
}
