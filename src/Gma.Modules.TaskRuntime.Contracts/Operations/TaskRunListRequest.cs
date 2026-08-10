namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Tasks;

public sealed record TaskRunListRequest(
    string? ModuleName,
    string? TaskName,
    string? WorkerGroup,
    TaskRunStatus? Status,
    string? ScopeId,
    int Page,
    int PageSize);
