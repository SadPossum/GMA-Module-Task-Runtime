namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Results;
using Gma.Framework.Tasks;

public interface ITaskRunReader
{
    Task<Result<TaskRunDetails>> GetAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<Result<TaskRunPage>> ListAsync(
        TaskRunListRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TaskRunStats>> GetStatsAsync(
        TaskRunStatsRequest request,
        CancellationToken cancellationToken = default);
}
