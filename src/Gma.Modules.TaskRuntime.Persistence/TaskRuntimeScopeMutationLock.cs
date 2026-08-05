namespace Gma.Modules.TaskRuntime.Persistence;

using Gma.Framework.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

internal static class TaskRuntimeScopeMutationLock
{
    private const string ResourcePrefix =
        "gma:task-runtime:scope-lifecycle:";

    private const string OperationResourcePrefix =
        "gma:task-runtime:scope-destroy-operation:";

    public static Task AcquireAdmissionAsync(
        TaskRuntimeDbContext dbContext,
        string scopeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return dbContext.Database.IsRelational()
            ? EfTransactionKeyLock.AcquireAsync(
                dbContext,
                ResourcePrefix + scopeId,
                EfTransactionKeyLockMode.Shared,
                cancellationToken)
            : Task.CompletedTask;
    }

    public static async Task AcquireLifecycleAsync(
        TaskRuntimeDbContext dbContext,
        string scopeId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Lifecycle operation id must not be empty.",
                nameof(operationId));
        }

        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        await EfTransactionKeyLock.AcquireAsync(
                dbContext,
                OperationResourcePrefix + operationId.ToString("D"),
                EfTransactionKeyLockMode.Exclusive,
                cancellationToken)
            .ConfigureAwait(false);
        await EfTransactionKeyLock.AcquireAsync(
                dbContext,
                ResourcePrefix + scopeId,
                EfTransactionKeyLockMode.Exclusive,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
