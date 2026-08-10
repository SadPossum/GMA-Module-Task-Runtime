namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Results;
using Gma.Framework.Tasks;

public interface ITaskRunEnqueuer
{
    Task<Result<TaskRunDetails>> EnqueueAsync(
        TaskRunEnqueueRequest request,
        CancellationToken cancellationToken = default);
}
