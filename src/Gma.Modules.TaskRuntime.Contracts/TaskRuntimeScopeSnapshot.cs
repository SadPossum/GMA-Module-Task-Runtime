namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRuntimeScopeSnapshot(
    TaskRuntimeScopeStatus Status,
    long Revision,
    long? SelectedRevision,
    long TotalRunCount,
    long ActiveRunCount,
    long ControlMessageCount);
