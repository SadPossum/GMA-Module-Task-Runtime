namespace Gma.Modules.TaskRuntime.Persistence;

using System.Data;
using System.Globalization;
using Gma.Framework.Tasks;
using Gma.Framework.Tasks.Infrastructure;
using Gma.Modules.TaskRuntime.Contracts;
using Gma.Modules.TaskRuntime.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ContractReceipt =
    Contracts.TaskRuntimeScopeDestroyReceipt;
using DomainReceipt =
    Entities.TaskRuntimeScopeDestroyReceipt;

internal sealed partial class TaskRuntimeScopeLifecycleService
{
    public async Task<TaskRuntimeScopeDestroyResult> DestroyBatchAsync(
        TaskRuntimeScopeDestroyRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            request.OperationId == Guid.Empty ||
            !Framework.Naming.ScopeIds.TryNormalize(
                request.ScopeId,
                out string? scopeId) ||
            request.ExpectedRevision < 0 ||
            request.ExpectedRevision == long.MaxValue ||
            request.BatchSize is < 1 or >
                TaskRuntimeScopeLifecycleLimits.MaximumDestroyBatchSize)
        {
            return DestroyResult(TaskRuntimeScopeDestroyStatus.Invalid);
        }

        string requestSha256 = DestroyRequestSha256(request, scopeId);
        TaskRuntimeScopeDestroyResult? completed = await this
            .TryReadCompletedResultAsync(
                request.OperationId,
                scopeId,
                requestSha256,
                cancellationToken)
            .ConfigureAwait(false);
        if (completed is not null)
        {
            return completed;
        }

        await using IDbContextTransaction? transaction =
            await this.BeginLifecycleTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        try
        {
            if (transaction is not null)
            {
                await TaskRuntimeScopeMutationLock.AcquireLifecycleAsync(
                        dbContext,
                        scopeId,
                        request.OperationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            DomainReceipt[] receiptMatches = await dbContext
                .TaskScopeDestroyReceipts
                .Where(receipt =>
                    receipt.OperationId == request.OperationId ||
                    receipt.ScopeId == scopeId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (receiptMatches.Length > 0)
            {
                DomainReceipt? exact = receiptMatches.SingleOrDefault(receipt =>
                    string.Equals(receipt.ScopeId, scopeId, StringComparison.Ordinal));
                TaskRuntimeScopeDestroyResult result =
                    exact is not null &&
                    receiptMatches.Length == 1 &&
                    exact.Matches(request.OperationId, requestSha256)
                        ? DestroyResult(
                            TaskRuntimeScopeDestroyStatus.Replayed,
                            receipt: Map(exact))
                        : DestroyResult(TaskRuntimeScopeDestroyStatus.Conflict);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }

            TaskRuntimeScopeDestroyOperation[] operationMatches = await dbContext
                .TaskScopeDestroyOperations
                .Where(operation =>
                    operation.OperationId == request.OperationId ||
                    operation.ScopeId == scopeId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            TaskRuntimeScopeDestroyOperation? operation = operationMatches
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.ScopeId, scopeId, StringComparison.Ordinal));
            if (operationMatches.Length > 0 &&
                (operation is null ||
                 operationMatches.Length != 1 ||
                 !operation.Matches(request.OperationId, requestSha256)))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(TaskRuntimeScopeDestroyStatus.Conflict);
            }

            TaskRuntimeScopeState? state = await dbContext.TaskScopeStates
                .SingleOrDefaultAsync(
                    candidate => candidate.ScopeId == scopeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (operation is null)
            {
                if (state is not null)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(TaskRuntimeScopeDestroyStatus.Conflict);
                }

                if (request.ExpectedRevision != 0)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(TaskRuntimeScopeDestroyStatus.Stale);
                }

                DateTimeOffset startedAtUtc = clock.UtcNow;
                state = TaskRuntimeScopeState.TryBeginClosing(
                    scopeId,
                    request.OperationId,
                    requestSha256,
                    request.ExpectedRevision,
                    request.ExpectedRevision + 1,
                    startedAtUtc);
                operation = TaskRuntimeScopeDestroyOperation.TryCreate(
                    request.OperationId,
                    scopeId,
                    requestSha256,
                    request.ExpectedRevision,
                    request.ExpectedRevision + 1,
                    request.BatchSize,
                    TaskRuntimeScopeLifecycleLimits.MaximumDestroyBatchSize,
                    startedAtUtc);
                if (state is null || operation is null)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(TaskRuntimeScopeDestroyStatus.Invalid);
                }

                await dbContext.TaskScopeStates.AddAsync(state, cancellationToken)
                    .ConfigureAwait(false);
                await dbContext.TaskScopeDestroyOperations
                    .AddAsync(operation, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (state is null ||
                state.Status != TaskRuntimeScopeStateStatus.Closing ||
                !Matches(state, operation))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(TaskRuntimeScopeDestroyStatus.Conflict);
            }

            while (!operation.IsComplete)
            {
                if (operation.Stage == TaskRuntimeScopeDestroyStage.QuiesceRuns)
                {
                    TaskRuntimeScopeDestroyResult? quiescence = await this
                        .QuiesceRunsAsync(
                            operation,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (quiescence is not null)
                    {
                        return quiescence;
                    }

                    continue;
                }

                int take = operation.BatchSize + 1;
                if (operation.Stage ==
                    TaskRuntimeScopeDestroyStage.ControlMessages)
                {
                    TaskControlMessageState[] loaded = await dbContext
                        .TaskControlMessages
                        .Where(message => message.ScopeId == scopeId)
                        .OrderBy(message => message.Id)
                        .Take(take)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (loaded.Length == 0)
                    {
                        EnsureAdvanced(operation, clock.UtcNow);
                        continue;
                    }

                    TaskControlMessageState[] selected = loaded
                        .Take(operation.BatchSize)
                        .ToArray();
                    dbContext.TaskControlMessages.RemoveRange(selected);
                    EnsureBatchRecorded(
                        operation,
                        selected.Select(message => message.Id),
                        selected.Length,
                        loaded.Length <= operation.BatchSize,
                        clock.UtcNow);
                    return await this.SaveProgressAsync(
                            operation,
                            transaction,
                            TaskRuntimeScopeDestroyStatus.InProgress,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (operation.Stage == TaskRuntimeScopeDestroyStage.TaskRuns)
                {
                    TaskRun[] loaded = await dbContext.TaskRuns
                        .Where(run => run.ScopeId == scopeId)
                        .OrderBy(run => run.Id)
                        .Take(take)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (loaded.Length == 0)
                    {
                        EnsureAdvanced(operation, clock.UtcNow);
                        continue;
                    }

                    if (loaded.Any(run => IsActive(run.Status)))
                    {
                        throw new InvalidDataException(
                            "Task-runtime scope destruction reached active runs after quiescence.");
                    }

                    TaskRun[] selected = loaded
                        .Take(operation.BatchSize)
                        .ToArray();
                    dbContext.TaskRuns.RemoveRange(selected);
                    EnsureBatchRecorded(
                        operation,
                        selected.Select(run => run.Id),
                        selected.Length,
                        loaded.Length <= operation.BatchSize,
                        clock.UtcNow);
                    return await this.SaveProgressAsync(
                            operation,
                            transaction,
                            TaskRuntimeScopeDestroyStatus.InProgress,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                throw new InvalidDataException(
                    "Task-runtime scope destruction stage is invalid.");
            }

            bool hasRecords = await dbContext.TaskRuns
                .AnyAsync(run => run.ScopeId == scopeId, cancellationToken)
                .ConfigureAwait(false) ||
                await dbContext.TaskControlMessages
                    .AnyAsync(
                        message => message.ScopeId == scopeId,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (hasRecords)
            {
                throw new InvalidDataException(
                    "Task-runtime scope destruction completed with remaining records.");
            }

            DateTimeOffset completedAtUtc = clock.UtcNow;
            DomainReceipt? completedReceipt =
                DomainReceipt.TryCreate(operation, completedAtUtc);
            if (completedReceipt is null || !state.Complete(completedAtUtc))
            {
                throw new InvalidDataException(
                    "Task-runtime scope destruction completion proof is invalid.");
            }

            await dbContext.TaskScopeDestroyReceipts
                .AddAsync(completedReceipt, cancellationToken)
                .ConfigureAwait(false);
            dbContext.TaskScopeDestroyOperations.Remove(operation);
            await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return DestroyResult(
                TaskRuntimeScopeDestroyStatus.Completed,
                receipt: Map(completedReceipt));
        }
        catch
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<TaskRuntimeScopeDestroyResult?> QuiesceRunsAsync(
        TaskRuntimeScopeDestroyOperation operation,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        TaskRun[] unleased = await dbContext.TaskRuns
            .Where(run =>
                run.ScopeId == operation.ScopeId &&
                (run.Status == TaskRunStatus.Queued ||
                 run.Status == TaskRunStatus.RetryScheduled))
            .OrderBy(run => run.Id)
            .Take(operation.BatchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (unleased.Length > 0)
        {
            DateTimeOffset observedAtUtc = clock.UtcNow;
            foreach (TaskRun run in unleased)
            {
                if (!run.RequestCancellation(LifecycleActor, observedAtUtc))
                {
                    throw new InvalidDataException(
                        "Task-runtime queued run could not be canceled during scope closure.");
                }
            }

            EnsureQuiescenceObserved(operation, observedAtUtc);
            return await this.SaveProgressAsync(
                    operation,
                    transaction,
                    TaskRuntimeScopeDestroyStatus.InProgress,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        TaskRun[] leased = await dbContext.TaskRuns
            .Where(run =>
                run.ScopeId == operation.ScopeId &&
                (run.Status == TaskRunStatus.Leased ||
                 run.Status == TaskRunStatus.Running))
            .OrderBy(run => run.Id)
            .Take(operation.BatchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (leased.Length > 0)
        {
            DateTimeOffset observedAtUtc = clock.UtcNow;
            foreach (TaskRun run in leased)
            {
                if (!run.RequestCancellation(LifecycleActor, observedAtUtc))
                {
                    throw new InvalidDataException(
                        "Task-runtime leased run could not request cancellation during scope closure.");
                }
            }

            EnsureQuiescenceObserved(operation, observedAtUtc);
            return await this.SaveProgressAsync(
                    operation,
                    transaction,
                    TaskRuntimeScopeDestroyStatus.Busy,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        long remainingActive = await this.CountActiveRunsAsync(
                operation.ScopeId,
                cancellationToken)
            .ConfigureAwait(false);
        if (remainingActive > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return DestroyResult(
                TaskRuntimeScopeDestroyStatus.Busy,
                Map(operation, remainingActive));
        }

        EnsureAdvanced(operation, clock.UtcNow);
        return null;
    }

    private async Task<TaskRuntimeScopeDestroyResult> SaveProgressAsync(
        TaskRuntimeScopeDestroyOperation operation,
        IDbContextTransaction? transaction,
        TaskRuntimeScopeDestroyStatus status,
        CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        long remainingActive = await this.CountActiveRunsAsync(
                operation.ScopeId,
                cancellationToken)
            .ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return DestroyResult(status, Map(operation, remainingActive));
    }

    private Task<long> CountActiveRunsAsync(
        string scopeId,
        CancellationToken cancellationToken) =>
        dbContext.TaskRuns.LongCountAsync(
            run =>
                run.ScopeId == scopeId &&
                (run.Status == TaskRunStatus.Queued ||
                 run.Status == TaskRunStatus.Leased ||
                 run.Status == TaskRunStatus.Running ||
                 run.Status == TaskRunStatus.CancellationRequested ||
                 run.Status == TaskRunStatus.RetryScheduled),
            cancellationToken);

    private static bool IsActive(TaskRunStatus status) =>
        status is
            TaskRunStatus.Queued or
            TaskRunStatus.Leased or
            TaskRunStatus.Running or
            TaskRunStatus.CancellationRequested or
            TaskRunStatus.RetryScheduled;

    private async Task<TaskRuntimeScopeDestroyResult?>
        TryReadCompletedResultAsync(
            Guid operationId,
            string scopeId,
            string requestSha256,
            CancellationToken cancellationToken)
    {
        DomainReceipt[] receipts = await dbContext.TaskScopeDestroyReceipts
            .AsNoTracking()
            .Where(receipt =>
                receipt.OperationId == operationId ||
                receipt.ScopeId == scopeId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (receipts.Length == 0)
        {
            return null;
        }

        DomainReceipt? exact = receipts.SingleOrDefault(receipt =>
            string.Equals(receipt.ScopeId, scopeId, StringComparison.Ordinal));
        return exact is not null &&
            receipts.Length == 1 &&
            exact.Matches(operationId, requestSha256)
                ? DestroyResult(
                    TaskRuntimeScopeDestroyStatus.Replayed,
                    receipt: Map(exact))
                : DestroyResult(TaskRuntimeScopeDestroyStatus.Conflict);
    }

    private static string DestroyRequestSha256(
        TaskRuntimeScopeDestroyRequest request,
        string scopeId) =>
        TaskRuntimeLifecycleHashes.Sha256(
            "gma-task-runtime-scope-destroy-request/v1|" +
            $"{request.OperationId:N}|{scopeId.Length.ToString(CultureInfo.InvariantCulture)}:" +
            $"{scopeId}|{request.ExpectedRevision.ToString(CultureInfo.InvariantCulture)}|" +
            request.BatchSize.ToString(CultureInfo.InvariantCulture));

    private static string RecordKeysSha256(
        TaskRuntimeScopeDestroyStage stage,
        IEnumerable<Guid> ids) =>
        TaskRuntimeLifecycleHashes.Sha256(
            "gma-task-runtime-scope-destroy-keys/v1|" +
            $"{(int)stage}|" +
            string.Join(",", ids.Select(id => id.ToString("N"))));

    private static void EnsureBatchRecorded(
        TaskRuntimeScopeDestroyOperation operation,
        IEnumerable<Guid> ids,
        int removed,
        bool stageCompleted,
        DateTimeOffset recordedAtUtc)
    {
        TaskRuntimeScopeDestroyStage stage = operation.Stage;
        if (!operation.RecordBatch(
                stage,
                removed,
                RecordKeysSha256(stage, ids),
                stageCompleted,
                recordedAtUtc))
        {
            throw new InvalidDataException(
                "Task-runtime scope destruction batch progress is invalid.");
        }
    }

    private static void EnsureAdvanced(
        TaskRuntimeScopeDestroyOperation operation,
        DateTimeOffset recordedAtUtc)
    {
        if (!operation.AdvanceEmptyStage(recordedAtUtc))
        {
            throw new InvalidDataException(
                "Task-runtime scope destruction stage progress is invalid.");
        }
    }

    private static void EnsureQuiescenceObserved(
        TaskRuntimeScopeDestroyOperation operation,
        DateTimeOffset observedAtUtc)
    {
        if (!operation.ObserveQuiescence(observedAtUtc))
        {
            throw new InvalidDataException(
                "Task-runtime scope quiescence progress is invalid.");
        }
    }

    private async Task<IDbContextTransaction?> BeginLifecycleTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null
            ? Task.CompletedTask
            : transaction.CommitAsync(cancellationToken);

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
