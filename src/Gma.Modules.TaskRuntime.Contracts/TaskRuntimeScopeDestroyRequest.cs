namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRuntimeScopeDestroyRequest(
    Guid OperationId,
    string ScopeId,
    long ExpectedRevision,
    int BatchSize);
