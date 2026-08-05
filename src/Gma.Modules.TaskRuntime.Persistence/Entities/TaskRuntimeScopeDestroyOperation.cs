namespace Gma.Modules.TaskRuntime.Persistence.Entities;

internal sealed class TaskRuntimeScopeDestroyOperation
{
    public const int RemovalProofVersion = 1;
    public static readonly string InitialRemovalProofSha256 =
        TaskRuntimeLifecycleHashes.Sha256(
            "gma-task-runtime-scope-destroy-proof/v1|empty");

    private TaskRuntimeScopeDestroyOperation() { }

    private TaskRuntimeScopeDestroyOperation(
        Guid operationId,
        string scopeId,
        string requestSha256,
        long selectedRevision,
        long resultingRevision,
        int batchSize,
        DateTimeOffset startedAtUtc)
    {
        this.OperationId = operationId;
        this.ScopeId = scopeId;
        this.RequestSha256 = requestSha256;
        this.SelectedRevision = selectedRevision;
        this.ResultingRevision = resultingRevision;
        this.BatchSize = batchSize;
        this.Stage = TaskRuntimeScopeDestroyStage.QuiesceRuns;
        this.RemovalProofSha256 = InitialRemovalProofSha256;
        this.StartedAtUtc = startedAtUtc;
        this.UpdatedAtUtc = startedAtUtc;
        this.ConcurrencyVersion = 1;
    }

    public Guid OperationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long SelectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public int BatchSize { get; private set; }
    public TaskRuntimeScopeDestroyStage Stage { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int ProofVersion { get; private set; } = RemovalProofVersion;
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public int ConcurrencyVersion { get; private set; }
    public bool IsComplete => this.Stage == TaskRuntimeScopeDestroyStage.Completed;

    public static TaskRuntimeScopeDestroyOperation? TryCreate(
        Guid operationId,
        string scopeId,
        string requestSha256,
        long selectedRevision,
        long resultingRevision,
        int batchSize,
        int maximumBatchSize,
        DateTimeOffset startedAtUtc)
    {
        if (operationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(scopeId) ||
            !TaskRuntimeLifecycleHashes.IsSha256(requestSha256) ||
            selectedRevision is < 0 or long.MaxValue ||
            resultingRevision != selectedRevision + 1 ||
            batchSize is < 1 ||
            batchSize > maximumBatchSize ||
            startedAtUtc == default)
        {
            return null;
        }

        return new TaskRuntimeScopeDestroyOperation(
            operationId,
            scopeId,
            requestSha256,
            selectedRevision,
            resultingRevision,
            batchSize,
            startedAtUtc);
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(this.RequestSha256, requestSha256, StringComparison.Ordinal);

    public bool ObserveQuiescence(DateTimeOffset observedAtUtc)
    {
        if (this.Stage != TaskRuntimeScopeDestroyStage.QuiesceRuns ||
            observedAtUtc < this.UpdatedAtUtc ||
            this.ConcurrencyVersion == int.MaxValue)
        {
            return false;
        }

        this.UpdatedAtUtc = observedAtUtc;
        this.ConcurrencyVersion++;
        return true;
    }

    public bool RecordBatch(
        TaskRuntimeScopeDestroyStage stage,
        int removedRecordCount,
        string removedRecordIdsSha256,
        bool stageCompleted,
        DateTimeOffset recordedAtUtc)
    {
        if (this.IsComplete ||
            stage != this.Stage ||
            stage == TaskRuntimeScopeDestroyStage.QuiesceRuns ||
            removedRecordCount is < 1 ||
            removedRecordCount > this.BatchSize ||
            !TaskRuntimeLifecycleHashes.IsSha256(removedRecordIdsSha256) ||
            recordedAtUtc < this.UpdatedAtUtc ||
            this.RemovedRecordCount > long.MaxValue - removedRecordCount ||
            this.CompletedBatchCount == int.MaxValue ||
            this.ConcurrencyVersion == int.MaxValue)
        {
            return false;
        }

        int nextBatch = this.CompletedBatchCount + 1;
        this.RemovalProofSha256 = TaskRuntimeLifecycleHashes.Sha256(
            "gma-task-runtime-scope-destroy-proof/v1|" +
            $"{this.RemovalProofSha256}|{nextBatch}|{(int)stage}|" +
            $"{removedRecordCount}|{removedRecordIdsSha256}");
        this.RemovedRecordCount += removedRecordCount;
        this.CompletedBatchCount = nextBatch;
        this.UpdatedAtUtc = recordedAtUtc;
        this.ConcurrencyVersion++;
        if (stageCompleted)
        {
            this.Stage = Next(stage);
        }

        return true;
    }

    public bool AdvanceEmptyStage(DateTimeOffset recordedAtUtc)
    {
        if (this.IsComplete ||
            recordedAtUtc < this.UpdatedAtUtc ||
            this.ConcurrencyVersion == int.MaxValue)
        {
            return false;
        }

        this.Stage = Next(this.Stage);
        this.UpdatedAtUtc = recordedAtUtc;
        this.ConcurrencyVersion++;
        return true;
    }

    private static TaskRuntimeScopeDestroyStage Next(
        TaskRuntimeScopeDestroyStage stage) =>
        stage switch
        {
            TaskRuntimeScopeDestroyStage.QuiesceRuns =>
                TaskRuntimeScopeDestroyStage.ControlMessages,
            TaskRuntimeScopeDestroyStage.ControlMessages =>
                TaskRuntimeScopeDestroyStage.TaskRuns,
            TaskRuntimeScopeDestroyStage.TaskRuns =>
                TaskRuntimeScopeDestroyStage.Completed,
            _ => throw new InvalidOperationException(
                "The task-runtime scope destruction stage is invalid.")
        };
}

internal enum TaskRuntimeScopeDestroyStage
{
    Unknown = 0,
    QuiesceRuns = 1,
    ControlMessages = 2,
    TaskRuns = 3,
    Completed = 4
}
