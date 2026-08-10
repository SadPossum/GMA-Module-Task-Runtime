namespace Gma.Modules.TaskRuntime.Application.Queries;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;

internal sealed record ListTaskRunsQuery(
    string? ModuleName,
    string? TaskName,
    string? WorkerGroup,
    TaskRunStatus? Status,
    string? ScopeId,
    int Page,
    int PageSize) : IQuery<TaskRunPage>;
