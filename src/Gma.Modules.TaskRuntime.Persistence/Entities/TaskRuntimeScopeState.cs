namespace Gma.Modules.TaskRuntime.Persistence.Entities;

internal sealed class TaskRuntimeScopeState
{
    private TaskRuntimeScopeState() { }

    private TaskRuntimeScopeState(
        string scopeId,
        Guid operationId,
        string requestSha256,
        long selectedRevision,
        long resultingRevision,
        DateTimeOffset startedAtUtc)
    {
        this.ScopeId = scopeId;
        this.OperationId = operationId;
        this.RequestSha256 = requestSha256;
        this.SelectedRevision = selectedRevision;
        this.ResultingRevision = resultingRevision;
        this.Status = TaskRuntimeScopeStateStatus.Closing;
        this.StartedAtUtc = startedAtUtc;
        this.UpdatedAtUtc = startedAtUtc;
        this.ConcurrencyVersion = 1;
    }

    public string ScopeId { get; private set; } = string.Empty;
    public Guid OperationId { get; private set; }
    public string RequestSha256 { get; private set; } = string.Empty;
    public long SelectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public TaskRuntimeScopeStateStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public int ConcurrencyVersion { get; private set; }

    public static TaskRuntimeScopeState? TryBeginClosing(
        string scopeId,
        Guid operationId,
        string requestSha256,
        long selectedRevision,
        long resultingRevision,
        DateTimeOffset startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(scopeId) ||
            operationId == Guid.Empty ||
            !TaskRuntimeLifecycleHashes.IsSha256(requestSha256) ||
            selectedRevision is < 0 or long.MaxValue ||
            resultingRevision != selectedRevision + 1 ||
            startedAtUtc == default)
        {
            return null;
        }

        return new TaskRuntimeScopeState(
            scopeId,
            operationId,
            requestSha256,
            selectedRevision,
            resultingRevision,
            startedAtUtc);
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(this.RequestSha256, requestSha256, StringComparison.Ordinal);

    public bool Complete(DateTimeOffset completedAtUtc)
    {
        if (this.Status != TaskRuntimeScopeStateStatus.Closing ||
            completedAtUtc < this.UpdatedAtUtc ||
            this.ConcurrencyVersion == int.MaxValue)
        {
            return false;
        }

        this.Status = TaskRuntimeScopeStateStatus.Closed;
        this.UpdatedAtUtc = completedAtUtc;
        this.ClosedAtUtc = completedAtUtc;
        this.ConcurrencyVersion++;
        return true;
    }
}

internal enum TaskRuntimeScopeStateStatus
{
    Unknown = 0,
    Closing = 1,
    Closed = 2
}
