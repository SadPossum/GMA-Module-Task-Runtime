namespace Gma.Modules.TaskRuntime.Application.Queries;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;

internal sealed record GetTaskRunStatsQuery(
    string? ModuleName,
    string? TaskName,
    string? WorkerGroup,
    string? ScopeId) : IQuery<TaskRunStats>;
