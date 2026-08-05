namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRuntimeScopeDestroyReceipt(
    Guid OperationId,
    long SelectedRevision,
    long ResultingRevision,
    int BatchSize,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
