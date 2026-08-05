namespace Gma.Modules.TaskRuntime.Persistence;

using Gma.Modules.TaskRuntime.Contracts;
using Gma.Modules.TaskRuntime.Persistence.Entities;
using ContractReceipt =
    Gma.Modules.TaskRuntime.Contracts.TaskRuntimeScopeDestroyReceipt;
using DomainReceipt =
    Gma.Modules.TaskRuntime.Persistence.Entities.TaskRuntimeScopeDestroyReceipt;

internal sealed partial class TaskRuntimeScopeLifecycleService
{
    private static TaskRuntimeScopeDestroyResult DestroyResult(
        TaskRuntimeScopeDestroyStatus status,
        TaskRuntimeScopeDestroyProgress? progress = null,
        ContractReceipt? receipt = null) =>
        new(status, progress, receipt);

    private static TaskRuntimeScopeDestroyProgress Map(
        TaskRuntimeScopeDestroyOperation operation,
        long remainingActiveRunCount) =>
        new(
            operation.OperationId,
            operation.SelectedRevision,
            operation.ResultingRevision,
            operation.BatchSize,
            Map(operation.Stage),
            operation.RemovedRecordCount,
            operation.CompletedBatchCount,
            remainingActiveRunCount,
            operation.ProofVersion,
            operation.RemovalProofSha256,
            operation.StartedAtUtc,
            operation.UpdatedAtUtc);

    private static ContractReceipt Map(
        DomainReceipt receipt) =>
        new(
            receipt.OperationId,
            receipt.SelectedRevision,
            receipt.ResultingRevision,
            receipt.BatchSize,
            receipt.RemovedRecordCount,
            receipt.CompletedBatchCount,
            receipt.RemovalProofVersion,
            receipt.RemovalProofSha256,
            receipt.StartedAtUtc,
            receipt.CompletedAtUtc);

    private static TaskRuntimeScopeDestructionStage Map(
        TaskRuntimeScopeDestroyStage stage) =>
        stage switch
        {
            TaskRuntimeScopeDestroyStage.QuiesceRuns =>
                TaskRuntimeScopeDestructionStage.QuiesceRuns,
            TaskRuntimeScopeDestroyStage.ControlMessages =>
                TaskRuntimeScopeDestructionStage.ControlMessages,
            TaskRuntimeScopeDestroyStage.TaskRuns =>
                TaskRuntimeScopeDestructionStage.TaskRuns,
            TaskRuntimeScopeDestroyStage.Completed =>
                TaskRuntimeScopeDestructionStage.Completed,
            _ => TaskRuntimeScopeDestructionStage.Unknown
        };
}
