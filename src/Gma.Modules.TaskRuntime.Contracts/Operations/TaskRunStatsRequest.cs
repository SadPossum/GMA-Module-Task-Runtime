namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRunStatsRequest(
    string? ModuleName,
    string? TaskName,
    string? WorkerGroup,
    string? ScopeId);
