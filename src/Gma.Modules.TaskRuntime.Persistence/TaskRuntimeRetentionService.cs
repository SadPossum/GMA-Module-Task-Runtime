namespace Gma.Modules.TaskRuntime.Persistence;

using Gma.Framework.Runtime.Maintenance;
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
            logger.LogInformation(
                "Task runtime retention cleanup is disabled; durable control-message expiry remains active.");
        }

        using PeriodicTimer timer = new(currentOptions.CleanupInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (currentOptions.Enabled)
            {
                await this.CleanupOnceAsync(currentOptions, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                await this.ExpireControlMessagesOnceAsync(currentOptions, stoppingToken).ConfigureAwait(false);
            }
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

        await this.ExpireControlMessagesAsync(
            dbContext,
            nowUtc,
            currentOptions,
            cancellationToken).ConfigureAwait(false);

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

    private async Task ExpireControlMessagesOnceAsync(
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        TaskRuntimeDbContext dbContext = scope.ServiceProvider.GetRequiredService<TaskRuntimeDbContext>();
        DateTimeOffset nowUtc = scope.ServiceProvider.GetRequiredService<ISystemClock>().UtcNow;
        await this.ExpireControlMessagesAsync(
            dbContext,
            nowUtc,
            currentOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExpireControlMessagesAsync(
        TaskRuntimeDbContext dbContext,
        DateTimeOffset nowUtc,
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            int expired = await BoundedBatchProcessor.ExecuteAsync(
                    currentOptions.BatchSize,
                    currentOptions.MaxBatchesPerStatusPerCycle,
                    (batchSize, token) => dbContext.TaskControlMessages
                        .Where(message =>
                            (message.Status == TaskControlMessageStatus.Pending ||
                             message.Status == TaskControlMessageStatus.Delivered ||
                             message.Status == TaskControlMessageStatus.Failed) &&
                            message.ExpiresAtUtc != null &&
                            message.ExpiresAtUtc <= nowUtc)
                        .OrderBy(message => message.ExpiresAtUtc)
                        .ThenBy(message => message.Id)
                        .Take(batchSize)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(message => message.Status, TaskControlMessageStatus.Expired)
                                .SetProperty(message => message.CompletedAtUtc, message => message.ExpiresAtUtc)
                                .SetProperty(
                                    message => message.ConcurrencyVersion,
                                    message => message.ConcurrencyVersion + 1),
                            token),
                    cancellationToken)
                .ConfigureAwait(false);

            if (expired > 0)
            {
                logger.LogInformation("Expired {ExpiredCount} task control messages.", expired);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to expire task control messages; the next lifecycle cycle will retry.");
        }
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
            (batchSize, token) => dbContext.TaskControlMessages
                .Where(message =>
                    message.Status == status &&
                    message.CompletedAtUtc != null &&
                    message.CompletedAtUtc < cutoff)
                .OrderBy(message => message.CompletedAtUtc)
                .Take(batchSize)
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
            (batchSize, token) => dbContext.TaskRuns
                .Where(run =>
                    run.Status == status &&
                    run.CompletedAtUtc != null &&
                    run.CompletedAtUtc < cutoff &&
                    !dbContext.TaskControlMessages.Any(message => message.RunId == run.Id))
                .OrderBy(run => run.CompletedAtUtc)
                .Take(batchSize)
                .ExecuteDeleteAsync(token),
            cancellationToken).ConfigureAwait(false);

    private async Task CleanupBatchesAsync(
        string recordKind,
        string status,
        TaskRuntimeRetentionOptions currentOptions,
        Func<int, CancellationToken, Task<int>> deleteBatch,
        CancellationToken cancellationToken)
    {
        int deletedTotal = 0;
        try
        {
            deletedTotal = await BoundedBatchProcessor.ExecuteAsync(
                    currentOptions.BatchSize,
                    currentOptions.MaxBatchesPerStatusPerCycle,
                    deleteBatch,
                    cancellationToken)
                .ConfigureAwait(false);

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
