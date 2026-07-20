namespace Gma.Modules.TaskRuntime.Persistence;

using System.Data;
using Gma.Framework.Tasks;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks.Infrastructure;
using Microsoft.EntityFrameworkCore;

internal sealed class TaskRuntimeRunStore(TaskRuntimeDbContext dbContext, ISystemClock clock)
    : EfTaskRunStore<TaskRuntimeDbContext>(dbContext, clock)
{
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

        return base.ClaimReadyAsync(claim, cancellationToken);
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
}
