namespace Gma.Modules.TaskRuntime.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Queries;

internal sealed class ListTaskRunsQueryHandler(ITaskRunStore store)
    : IQueryHandler<ListTaskRunsQuery, TaskRunPage>
{
    public async Task<Result<TaskRunPage>> HandleAsync(
        ListTaskRunsQuery query,
        CancellationToken cancellationToken)
    {
        TaskRunFilter filter;
        try
        {
            filter = new TaskRunFilter(
                query.ModuleName,
                query.TaskName,
                query.WorkerGroup,
                query.Status,
                query.ScopeId,
                query.Page,
                query.PageSize);
        }
        catch (ArgumentException)
        {
            return Result.Failure<TaskRunPage>(TaskRuntimeApplicationErrors.InvalidRunFilter);
        }

        TaskRunPage page = await store.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return Result.Success(page);
    }
}
