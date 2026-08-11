namespace Gma.Modules.TaskRuntime.Persistence;

using System.Diagnostics;
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
    TaskRuntimeRetentionMetrics metrics,
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
                            (message.ScopeId == null ||
                             !dbContext.TaskScopeStates.Any(state =>
                                 state.ScopeId == message.ScopeId)) &&
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
            logger.LogError(
                "Failed to expire task control messages with {ExceptionType}; the next lifecycle cycle will retry.",
                exception.GetType().Name);
        }
    }

    private async Task CleanupControlStatusAsync(
        TaskRuntimeDbContext dbContext,
        TaskControlMessageStatus status,
        DateTimeOffset cutoff,
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskControlMessageState> terminal = dbContext.TaskControlMessages
            .Where(message =>
                (message.ScopeId == null ||
                 !dbContext.TaskScopeStates.Any(state => state.ScopeId == message.ScopeId)) &&
                message.Status == status &&
                message.CompletedAtUtc != null);

        await this.CleanupBatchesAsync(
            "control-message",
            status.ToString(),
            currentOptions,
            (batchSize, token) => terminal
                .Where(message => message.CompletedAtUtc < cutoff)
                .OrderBy(message => message.CompletedAtUtc)
                .Take(batchSize)
                .ExecuteDeleteAsync(token),
            token => terminal.MinAsync(message => message.CompletedAtUtc, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupRunStatusAsync(
        TaskRuntimeDbContext dbContext,
        TaskRunStatus status,
        DateTimeOffset cutoff,
        TaskRuntimeRetentionOptions currentOptions,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskRun> terminal = dbContext.TaskRuns
            .Where(run =>
                (run.ScopeId == null ||
                 !dbContext.TaskScopeStates.Any(state => state.ScopeId == run.ScopeId)) &&
                run.Status == status &&
                run.CompletedAtUtc != null &&
                !dbContext.TaskControlMessages.Any(message => message.RunId == run.Id));

        await this.CleanupBatchesAsync(
            "run",
            status.ToString(),
            currentOptions,
            (batchSize, token) => terminal
                .Where(run => run.CompletedAtUtc < cutoff)
                .OrderBy(run => run.CompletedAtUtc)
                .Take(batchSize)
                .ExecuteDeleteAsync(token),
            token => terminal.MinAsync(run => run.CompletedAtUtc, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupBatchesAsync(
        string recordKind,
        string status,
        TaskRuntimeRetentionOptions currentOptions,
        Func<int, CancellationToken, Task<int>> deleteBatch,
        Func<CancellationToken, Task<DateTimeOffset?>> getOldestTerminal,
        CancellationToken cancellationToken)
    {
        int deletedTotal = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await BoundedBatchProcessor.ExecuteAsync(
                    currentOptions.BatchSize,
                    currentOptions.MaxBatchesPerStatusPerCycle,
                    deleteBatch,
                    processed => deletedTotal = checked(deletedTotal + processed),
                    cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset? oldestTerminal = await getOldestTerminal(cancellationToken)
                .ConfigureAwait(false);
            this.TrySetOldestTerminal(recordKind, status, oldestTerminal);

            if (deletedTotal > 0)
            {
                this.TryRecordDeleted(recordKind, status, deletedTotal);
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
            this.TryRecordDeleted(recordKind, status, deletedTotal);
            this.TryRecordFailure(recordKind, status);
            this.TrySetOldestTerminal(recordKind, status, null);
            logger.LogError(
                "Failed to clean terminal task {RecordKind} records with status {Status} using {ExceptionType}; other statuses will continue.",
                recordKind,
                status,
                exception.GetType().Name);
        }
        finally
        {
            stopwatch.Stop();
            this.TryRecordDuration(recordKind, status, stopwatch.Elapsed);
        }
    }

    private void TryRecordDeleted(string recordKind, string status, int count)
    {
        try
        {
            metrics.RecordDeleted(recordKind, status, count);
        }
        catch (Exception)
        {
            // Metrics are observability only; exporter failures must not stop cleanup.
        }
    }

    private void TryRecordFailure(string recordKind, string status)
    {
        try
        {
            metrics.RecordFailure(recordKind, status);
        }
        catch (Exception)
        {
            // Metrics are observability only; exporter failures must not stop cleanup.
        }
    }

    private void TryRecordDuration(string recordKind, string status, TimeSpan duration)
    {
        try
        {
            metrics.RecordDuration(recordKind, status, duration);
        }
        catch (Exception)
        {
            // Metrics are observability only; exporter failures must not stop cleanup.
        }
    }

    private void TrySetOldestTerminal(
        string recordKind,
        string status,
        DateTimeOffset? completedAtUtc)
    {
        try
        {
            metrics.SetOldestTerminal(recordKind, status, completedAtUtc);
        }
        catch (Exception)
        {
            // Metrics are observability only; exporter failures must not stop cleanup.
        }
    }
}
