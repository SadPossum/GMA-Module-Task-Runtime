namespace Gma.Modules.TaskRuntime.Persistence;

using System.Data;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Framework.Tasks.Infrastructure;
using Microsoft.EntityFrameworkCore;

internal sealed class TaskRuntimeRunStore(TaskRuntimeDbContext dbContext, ISystemClock clock)
    : EfTaskRunStore<TaskRuntimeDbContext>(dbContext, clock)
{
    public override async Task<TaskRunEnqueueResult> EnqueueAsync(
        TaskRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ScopeId is null)
        {
            return await base.EnqueueAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        return await this.ExecuteAdmissionTransactionAsync(
                request.ScopeId,
                token => base.EnqueueAsync(request, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task<TaskRunMutationOutcome> RetryAsync(
        Guid runId,
        string? requestedBy,
        DateTimeOffset scheduledAtUtc,
        CancellationToken cancellationToken) =>
        this.ExecuteRunAdmissionTransactionAsync(
            runId,
            token => base.RetryAsync(
                runId,
                requestedBy,
                scheduledAtUtc,
                token),
            cancellationToken);

    public override Task<TaskControlMessageEnqueueOutcome>
        EnqueueControlMessageAsync(
            TaskControlMessage message,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this.ExecuteRunAdmissionTransactionAsync(
            message.RunId,
            token => base.EnqueueControlMessageAsync(message, token),
            cancellationToken);
    }

    protected override async ValueTask<bool> IsScopeAcceptingNewWorkAsync(
        string? scopeId,
        CancellationToken cancellationToken)
    {
        if (scopeId is null)
        {
            return true;
        }

        return !await this.StoreDbContext.TaskScopeStates
            .AsNoTracking()
            .AnyAsync(state => state.ScopeId == scopeId, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task<IReadOnlyList<TaskRunLease>> ClaimReadyAsync(
        TaskWorkerClaim claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (this.StoreDbContext.Database.IsNpgsql())
        {
            return this.ClaimLockedAsync(
                claim,
                IsolationLevel.ReadCommitted,
                token => this.StoreDbContext.TaskRuns
                    .FromSqlInterpolated($$"""
                        SELECT *
                        FROM tasks.task_runs
                        WHERE "WorkerGroup" = {{claim.WorkerGroup}}
                          AND ("ScopeId" IS NULL OR NOT EXISTS (
                              SELECT 1
                              FROM tasks.task_scope_states scope_state
                              WHERE scope_state."ScopeId" = task_runs."ScopeId"))
                          AND "ScheduledAtUtc" <= {{claim.ClaimedAtUtc}}
                          AND "Attempts" < "MaxAttempts"
                          AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= {{claim.ClaimedAtUtc}})
                          AND ("LockedUntilUtc" IS NULL OR "LockedUntilUtc" <= {{claim.ClaimedAtUtc}})
                          AND "Status" IN ({{TaskRunStatus.Queued}}, {{TaskRunStatus.Leased}}, {{TaskRunStatus.Running}}, {{TaskRunStatus.CancellationRequested}}, {{TaskRunStatus.RetryScheduled}})
                        ORDER BY "ScheduledAtUtc", "CreatedAtUtc", "Id"
                        LIMIT {{claim.MaxRuns}}
                        FOR UPDATE SKIP LOCKED
                        """)
                    .ToListAsync(token),
                cancellationToken);
        }

        if (this.StoreDbContext.Database.IsSqlServer())
        {
            return this.ClaimLockedAsync(
                claim,
                IsolationLevel.ReadCommitted,
                token => this.StoreDbContext.TaskRuns
                    .FromSqlInterpolated($$"""
                        SELECT TOP ({{claim.MaxRuns}}) *
                        FROM [tasks].[task_runs] WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE [WorkerGroup] = {{claim.WorkerGroup}}
                          AND ([ScopeId] IS NULL OR NOT EXISTS (
                              SELECT 1
                              FROM [tasks].[task_scope_states] AS [scope_state]
                              WHERE [scope_state].[ScopeId] = [task_runs].[ScopeId]))
                          AND [ScheduledAtUtc] <= {{claim.ClaimedAtUtc}}
                          AND [Attempts] < [MaxAttempts]
                          AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= {{claim.ClaimedAtUtc}})
                          AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] <= {{claim.ClaimedAtUtc}})
                          AND [Status] IN ({{TaskRunStatus.Queued}}, {{TaskRunStatus.Leased}}, {{TaskRunStatus.Running}}, {{TaskRunStatus.CancellationRequested}}, {{TaskRunStatus.RetryScheduled}})
                        ORDER BY [ScheduledAtUtc], [CreatedAtUtc], [Id]
                        """)
                    .ToListAsync(token),
                cancellationToken);
        }

        return this.ClaimLockedAsync(
            claim,
            IsolationLevel.Serializable,
            token => this.StoreDbContext.TaskRuns
                .Where(taskRun =>
                    (taskRun.ScopeId == null ||
                     !this.StoreDbContext.TaskScopeStates.Any(state =>
                         state.ScopeId == taskRun.ScopeId)) &&
                    taskRun.WorkerGroup == claim.WorkerGroup &&
                    taskRun.ScheduledAtUtc <= claim.ClaimedAtUtc &&
                    taskRun.Attempts < taskRun.MaxAttempts &&
                    (taskRun.NextAttemptAtUtc == null ||
                     taskRun.NextAttemptAtUtc <= claim.ClaimedAtUtc) &&
                    (taskRun.LockedUntilUtc == null ||
                     taskRun.LockedUntilUtc <= claim.ClaimedAtUtc) &&
                    (taskRun.Status == TaskRunStatus.Queued ||
                     taskRun.Status == TaskRunStatus.Leased ||
                     taskRun.Status == TaskRunStatus.Running ||
                     taskRun.Status == TaskRunStatus.CancellationRequested ||
                     taskRun.Status == TaskRunStatus.RetryScheduled))
                .OrderBy(taskRun => taskRun.ScheduledAtUtc)
                .ThenBy(taskRun => taskRun.CreatedAtUtc)
                .ThenBy(taskRun => taskRun.Id)
                .Take(claim.MaxRuns)
                .ToListAsync(token),
            cancellationToken);
    }

    public override Task<IReadOnlyList<TaskRunSummary>> MarkStaleTimedOutAsync(
        DateTimeOffset nowUtc,
        TimeSpan staleAfter,
        int maxRuns,
        CancellationToken cancellationToken)
    {
        TaskRun.RequireTimestamp(nowUtc, nameof(nowUtc));
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), staleAfter, "Stale timeout window must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxRuns, 1);
        DateTimeOffset staleBeforeUtc = nowUtc.Subtract(staleAfter);

        if (this.StoreDbContext.Database.IsNpgsql())
        {
            return this.MarkStaleLockedAsync(
                nowUtc,
                IsolationLevel.ReadCommitted,
                token => this.StoreDbContext.TaskRuns
                    .FromSqlInterpolated($$"""
                        SELECT *
                        FROM tasks.task_runs
                        WHERE ("Status" = {{TaskRunStatus.Leased}} AND "LockedUntilUtc" <= {{nowUtc}})
                           OR ("Status" IN ({{TaskRunStatus.Running}}, {{TaskRunStatus.CancellationRequested}})
                               AND (("LastHeartbeatAtUtc" IS NOT NULL AND "LastHeartbeatAtUtc" <= {{staleBeforeUtc}})
                                    OR ("LastHeartbeatAtUtc" IS NULL AND "LockedUntilUtc" <= {{nowUtc}})))
                        ORDER BY "LockedUntilUtc", "StartedAtUtc", "Id"
                        LIMIT {{maxRuns}}
                        FOR UPDATE SKIP LOCKED
                        """)
                    .ToListAsync(token),
                cancellationToken);
        }

        if (this.StoreDbContext.Database.IsSqlServer())
        {
            return this.MarkStaleLockedAsync(
                nowUtc,
                IsolationLevel.ReadCommitted,
                token => this.StoreDbContext.TaskRuns
                    .FromSqlInterpolated($$"""
                        SELECT TOP ({{maxRuns}}) *
                        FROM [tasks].[task_runs] WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE ([Status] = {{TaskRunStatus.Leased}} AND [LockedUntilUtc] <= {{nowUtc}})
                           OR ([Status] IN ({{TaskRunStatus.Running}}, {{TaskRunStatus.CancellationRequested}})
                               AND (([LastHeartbeatAtUtc] IS NOT NULL AND [LastHeartbeatAtUtc] <= {{staleBeforeUtc}})
                                    OR ([LastHeartbeatAtUtc] IS NULL AND [LockedUntilUtc] <= {{nowUtc}})))
                        ORDER BY [LockedUntilUtc], [StartedAtUtc], [Id]
                        """)
                    .ToListAsync(token),
                cancellationToken);
        }

        return base.MarkStaleTimedOutAsync(nowUtc, staleAfter, maxRuns, cancellationToken);
    }

    private async Task<T> ExecuteRunAdmissionTransactionAsync<T>(
        Guid runId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        string? scopeId = await this.StoreDbContext.TaskRuns
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => run.ScopeId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.ExecuteAdmissionTransactionAsync(
                scopeId,
                action,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T> ExecuteAdmissionTransactionAsync<T>(
        string? scopeId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!this.StoreDbContext.Database.IsRelational())
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }

        if (this.StoreDbContext.Database.CurrentTransaction is not null)
        {
            if (scopeId is not null)
            {
                await TaskRuntimeScopeMutationLock.AcquireAdmissionAsync(
                        this.StoreDbContext,
                        scopeId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return await action(cancellationToken).ConfigureAwait(false);
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await this.StoreDbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken)
                .ConfigureAwait(false);
        try
        {
            if (scopeId is not null)
            {
                await TaskRuntimeScopeMutationLock.AcquireAdmissionAsync(
                        this.StoreDbContext,
                        scopeId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            T result = await action(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
