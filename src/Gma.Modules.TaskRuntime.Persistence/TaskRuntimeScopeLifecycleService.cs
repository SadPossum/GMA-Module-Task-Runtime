namespace Gma.Modules.TaskRuntime.Persistence;

using Gma.Framework.Naming;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Modules.TaskRuntime.Contracts;
using Gma.Modules.TaskRuntime.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using DomainReceipt =
    Gma.Modules.TaskRuntime.Persistence.Entities.TaskRuntimeScopeDestroyReceipt;

internal sealed partial class TaskRuntimeScopeLifecycleService(
    TaskRuntimeDbContext dbContext,
    ISystemClock clock)
    : ITaskRuntimeScopeLifecycle
{
    private const string LifecycleActor = "task-runtime-scope-lifecycle";

    public async Task<TaskRuntimeScopeSnapshot> GetSnapshotAsync(
        string scopeId,
        CancellationToken cancellationToken)
    {
        if (!ScopeIds.TryNormalize(scopeId, out string? normalized))
        {
            return InvalidSnapshot();
        }

        return await this.ReadSnapshotAsync(normalized, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskRuntimeScopeSnapshot> ReadSnapshotAsync(
        string scopeId,
        CancellationToken cancellationToken)
    {
        TaskRuntimeScopeState? state = await dbContext.TaskScopeStates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeId == scopeId, cancellationToken)
            .ConfigureAwait(false);
        TaskRuntimeScopeDestroyOperation? operation = await dbContext
            .TaskScopeDestroyOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeId == scopeId, cancellationToken)
            .ConfigureAwait(false);
        DomainReceipt? receipt = await dbContext
            .TaskScopeDestroyReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeId == scopeId, cancellationToken)
            .ConfigureAwait(false);

        (long totalRuns, long activeRuns, long controlMessages) =
            await this.ReadCountsAsync(scopeId, cancellationToken)
                .ConfigureAwait(false);

        if (state is null)
        {
            if (operation is not null || receipt is not null)
            {
                return InvalidSnapshot();
            }

            return new TaskRuntimeScopeSnapshot(
                totalRuns == 0 && controlMessages == 0
                    ? TaskRuntimeScopeStatus.Missing
                    : TaskRuntimeScopeStatus.Open,
                Revision: 0,
                SelectedRevision: null,
                totalRuns,
                activeRuns,
                controlMessages);
        }

        if (state.Status == TaskRuntimeScopeStateStatus.Closing)
        {
            if (operation is null ||
                receipt is not null ||
                !Matches(state, operation))
            {
                return InvalidSnapshot();
            }

            return new TaskRuntimeScopeSnapshot(
                TaskRuntimeScopeStatus.Closing,
                state.ResultingRevision,
                state.SelectedRevision,
                totalRuns,
                activeRuns,
                controlMessages);
        }

        if (state.Status != TaskRuntimeScopeStateStatus.Closed ||
            operation is not null ||
            receipt is null ||
            !Matches(state, receipt) ||
            totalRuns != 0 ||
            activeRuns != 0 ||
            controlMessages != 0)
        {
            return InvalidSnapshot();
        }

        return new TaskRuntimeScopeSnapshot(
            TaskRuntimeScopeStatus.Closed,
            state.ResultingRevision,
            state.SelectedRevision,
            TotalRunCount: 0,
            ActiveRunCount: 0,
            ControlMessageCount: 0);
    }

    private async Task<(long TotalRuns, long ActiveRuns, long ControlMessages)>
        ReadCountsAsync(
            string scopeId,
            CancellationToken cancellationToken)
    {
        long totalRuns = await dbContext.TaskRuns
            .LongCountAsync(run => run.ScopeId == scopeId, cancellationToken)
            .ConfigureAwait(false);
        long activeRuns = await dbContext.TaskRuns
            .LongCountAsync(
                run =>
                    run.ScopeId == scopeId &&
                    (run.Status == TaskRunStatus.Queued ||
                     run.Status == TaskRunStatus.Leased ||
                     run.Status == TaskRunStatus.Running ||
                     run.Status == TaskRunStatus.CancellationRequested ||
                     run.Status == TaskRunStatus.RetryScheduled),
                cancellationToken)
            .ConfigureAwait(false);
        long controlMessages = await dbContext.TaskControlMessages
            .LongCountAsync(
                message => message.ScopeId == scopeId,
                cancellationToken)
            .ConfigureAwait(false);
        return (totalRuns, activeRuns, controlMessages);
    }

    private static TaskRuntimeScopeSnapshot InvalidSnapshot() =>
        new(
            TaskRuntimeScopeStatus.Invalid,
            Revision: 0,
            SelectedRevision: null,
            TotalRunCount: 0,
            ActiveRunCount: 0,
            ControlMessageCount: 0);

    private static bool Matches(
        TaskRuntimeScopeState state,
        TaskRuntimeScopeDestroyOperation operation) =>
        string.Equals(state.ScopeId, operation.ScopeId, StringComparison.Ordinal) &&
        state.OperationId == operation.OperationId &&
        string.Equals(
            state.RequestSha256,
            operation.RequestSha256,
            StringComparison.Ordinal) &&
        state.SelectedRevision == operation.SelectedRevision &&
        state.ResultingRevision == operation.ResultingRevision;

    private static bool Matches(
        TaskRuntimeScopeState state,
        DomainReceipt receipt) =>
        string.Equals(state.ScopeId, receipt.ScopeId, StringComparison.Ordinal) &&
        state.OperationId == receipt.OperationId &&
        string.Equals(
            state.RequestSha256,
            receipt.RequestSha256,
            StringComparison.Ordinal) &&
        state.SelectedRevision == receipt.SelectedRevision &&
        state.ResultingRevision == receipt.ResultingRevision &&
        state.ClosedAtUtc == receipt.CompletedAtUtc;
}
