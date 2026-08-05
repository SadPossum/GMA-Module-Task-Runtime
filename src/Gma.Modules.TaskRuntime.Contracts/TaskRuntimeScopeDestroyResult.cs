namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRuntimeScopeDestroyResult(
    TaskRuntimeScopeDestroyStatus Status,
    TaskRuntimeScopeDestroyProgress? Progress,
    TaskRuntimeScopeDestroyReceipt? Receipt);
