namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Results;
using Gma.Framework.Tasks;

public interface ITaskRunController
{
    Task<Result> CancelAsync(
        Guid runId,
        string? requestedBy,
        CancellationToken cancellationToken = default);

    Task<Result> RetryAsync(
        Guid runId,
        string? requestedBy,
        DateTimeOffset? scheduledAtUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<TaskControlMessage>> SendControlMessageAsync(
        TaskRunControlRequest request,
        CancellationToken cancellationToken = default);
}
