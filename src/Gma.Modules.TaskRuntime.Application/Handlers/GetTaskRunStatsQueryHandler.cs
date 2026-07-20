namespace Gma.Modules.TaskRuntime.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Queries;

internal sealed class GetTaskRunStatsQueryHandler(ITaskRunStore store)
    : IQueryHandler<GetTaskRunStatsQuery, TaskRunStats>
{
    public async Task<Result<TaskRunStats>> HandleAsync(
        GetTaskRunStatsQuery query,
        CancellationToken cancellationToken)
    {
        TaskRunStatsFilter filter;
        try
        {
            filter = new TaskRunStatsFilter(
                query.ModuleName,
                query.TaskName,
                query.WorkerGroup,
                query.ScopeId);
        }
        catch (ArgumentException)
        {
            return Result.Failure<TaskRunStats>(TaskRuntimeApplicationErrors.InvalidRunFilter);
        }

        TaskRunStats stats = await store.GetStatsAsync(filter, cancellationToken).ConfigureAwait(false);

        return Result.Success(stats);
    }
}
