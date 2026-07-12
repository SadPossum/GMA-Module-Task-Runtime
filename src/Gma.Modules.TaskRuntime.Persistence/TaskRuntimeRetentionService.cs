namespace Gma.Modules.TaskRuntime.Persistence;

using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Framework.Tasks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class TaskRuntimeRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<TaskRuntimeRetentionOptions> options,
    ILogger<TaskRuntimeRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TaskRuntimeRetentionOptions currentOptions = options.Value;
        if (!currentOptions.Enabled)
        {
            logger.LogInformation("Task runtime retention cleanup is disabled.");
            return;
        }

        using PeriodicTimer timer = new(currentOptions.CleanupInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await this.CleanupOnceAsync(currentOptions, stoppingToken).ConfigureAwait(false);
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task CleanupOnceAsync(
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        TaskRuntimeDbContext dbContext = scope.ServiceProvider.GetRequiredService<TaskRuntimeDbContext>();
        DateTimeOffset nowUtc = scope.ServiceProvider.GetRequiredService<ISystemClock>().UtcNow;

        await this.CleanupControlStatusAsync(
            dbContext,
            TaskControlMessageStatus.Handled,
            nowUtc.Subtract(currentOptions.HandledControlRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);
        await this.CleanupControlStatusAsync(
            dbContext,
            TaskControlMessageStatus.Failed,
            nowUtc.Subtract(currentOptions.FailedControlRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);
        await this.CleanupControlStatusAsync(
            dbContext,
            TaskControlMessageStatus.Expired,
            nowUtc.Subtract(currentOptions.ExpiredControlRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);

        await this.CleanupRunStatusAsync(
            dbContext,
            TaskRunStatus.Succeeded,
            nowUtc.Subtract(currentOptions.SucceededRunRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);
        await this.CleanupRunStatusAsync(
            dbContext,
            TaskRunStatus.Failed,
            nowUtc.Subtract(currentOptions.FailedRunRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);
        await this.CleanupRunStatusAsync(
            dbContext,
            TaskRunStatus.Canceled,
            nowUtc.Subtract(currentOptions.CanceledRunRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);
        await this.CleanupRunStatusAsync(
            dbContext,
            TaskRunStatus.TimedOut,
            nowUtc.Subtract(currentOptions.TimedOutRunRetention),
            currentOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupControlStatusAsync(
        TaskRuntimeDbContext dbContext,
        TaskControlMessageStatus status,
        DateTimeOffset cutoff,
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken) =>
        await this.CleanupBatchesAsync(
            "control message",
            status.ToString(),
            currentOptions,
            token => dbContext.TaskControlMessages
                .Where(message =>
                    message.Status == status &&
                    message.CompletedAtUtc != null &&
                    message.CompletedAtUtc < cutoff)
                .OrderBy(message => message.CompletedAtUtc)
                .Take(currentOptions.BatchSize)
                .ExecuteDeleteAsync(token),
            cancellationToken).ConfigureAwait(false);

    private async Task CleanupRunStatusAsync(
        TaskRuntimeDbContext dbContext,
        TaskRunStatus status,
        DateTimeOffset cutoff,
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken) =>
        await this.CleanupBatchesAsync(
            "run",
            status.ToString(),
            currentOptions,
            token => dbContext.TaskRuns
                .Where(run =>
                    run.Status == status &&
                    run.CompletedAtUtc != null &&
                    run.CompletedAtUtc < cutoff &&
                    !dbContext.TaskControlMessages.Any(message => message.RunId == run.Id))
                .OrderBy(run => run.CompletedAtUtc)
                .Take(currentOptions.BatchSize)
                .ExecuteDeleteAsync(token),
            cancellationToken).ConfigureAwait(false);

    private async Task CleanupBatchesAsync(
        string recordKind,
        string status,
        TaskRuntimeRetentionOptions currentOptions,
        Func<CancellationToken, Task<int>> deleteBatch,
        CancellationToken cancellationToken)
    {
        int deletedTotal = 0;
        try
        {
            for (int batch = 0; batch < currentOptions.MaxBatchesPerStatusPerCycle; batch++)
            {
                int deleted = await deleteBatch(cancellationToken).ConfigureAwait(false);
                if (deleted < 0 || deleted > currentOptions.BatchSize)
                {
                    throw new InvalidOperationException(
                        $"Task runtime retention returned invalid batch count {deleted}.");
                }

                deletedTotal += deleted;
                if (deleted < currentOptions.BatchSize)
                {
                    break;
                }
            }

            if (deletedTotal > 0)
            {
                logger.LogInformation(
                    "Deleted {DeletedCount} terminal task {RecordKind} records with status {Status}.",
                    deletedTotal,
                    recordKind,
                    status);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to clean terminal task {RecordKind} records with status {Status}; other statuses will continue.",
                recordKind,
                status);
        }
    }
}
