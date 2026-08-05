namespace Gma.Modules.TaskRuntime.Contracts;

public interface ITaskRuntimeScopeLifecycle
{
    Task<TaskRuntimeScopeSnapshot> GetSnapshotAsync(
        string scopeId,
        CancellationToken cancellationToken);

    Task<TaskRuntimeScopeDestroyResult> DestroyBatchAsync(
        TaskRuntimeScopeDestroyRequest request,
        CancellationToken cancellationToken);
}
