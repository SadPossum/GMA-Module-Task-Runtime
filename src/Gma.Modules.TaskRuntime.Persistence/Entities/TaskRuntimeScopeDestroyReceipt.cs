namespace Gma.Modules.TaskRuntime.Persistence.Entities;

internal sealed class TaskRuntimeScopeDestroyReceipt
{
    private TaskRuntimeScopeDestroyReceipt() { }

    private TaskRuntimeScopeDestroyReceipt(
        TaskRuntimeScopeDestroyOperation operation,
        DateTimeOffset completedAtUtc)
    {
        this.OperationId = operation.OperationId;
        this.ScopeId = operation.ScopeId;
        this.RequestSha256 = operation.RequestSha256;
        this.SelectedRevision = operation.SelectedRevision;
        this.ResultingRevision = operation.ResultingRevision;
        this.BatchSize = operation.BatchSize;
        this.RemovedRecordCount = operation.RemovedRecordCount;
        this.CompletedBatchCount = operation.CompletedBatchCount;
        this.RemovalProofVersion = operation.ProofVersion;
        this.RemovalProofSha256 = operation.RemovalProofSha256;
        this.StartedAtUtc = operation.StartedAtUtc;
        this.CompletedAtUtc = completedAtUtc;
    }

    public Guid OperationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long SelectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public int BatchSize { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int RemovalProofVersion { get; private set; }
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static TaskRuntimeScopeDestroyReceipt? TryCreate(
        TaskRuntimeScopeDestroyOperation operation,
        DateTimeOffset completedAtUtc)
    {
        bool progressShapeValid = operation is not null &&
            ((operation.RemovedRecordCount == 0 &&
              operation.CompletedBatchCount == 0) ||
             (operation.RemovedRecordCount > 0 &&
              operation.CompletedBatchCount > 0));
        if (operation is null ||
            !operation.IsComplete ||
            !progressShapeValid ||
            operation.ResultingRevision != operation.SelectedRevision + 1 ||
            operation.ProofVersion !=
                TaskRuntimeScopeDestroyOperation.RemovalProofVersion ||
            completedAtUtc < operation.UpdatedAtUtc)
        {
            return null;
        }

        return new TaskRuntimeScopeDestroyReceipt(operation, completedAtUtc);
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(this.RequestSha256, requestSha256, StringComparison.Ordinal);
}
