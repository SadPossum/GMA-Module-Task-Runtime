namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRuntimeScopeDestroyProgress(
    Guid OperationId,
    long SelectedRevision,
    long ResultingRevision,
    int BatchSize,
    TaskRuntimeScopeDestructionStage Stage,
    long RemovedRecordCount,
    int CompletedBatchCount,
    long RemainingActiveRunCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);
