namespace Gma.Modules.TaskRuntime.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Commands;
using Gma.Modules.TaskRuntime.Contracts;

internal sealed class CancelTaskRunCommandHandler(
    ITaskRunStore store,
    ISystemClock clock)
    : ICommandHandler<CancelTaskRunCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        CancelTaskRunCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty)
        {
            return Result.Failure<Unit>(TaskRuntimeOperationErrors.InvalidRunId);
        }

        TaskRunMutationOutcome outcome = await store.RequestCancellationAsync(
                command.RunId,
                command.RequestedBy,
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            TaskRunMutationOutcome.Applied or TaskRunMutationOutcome.AlreadyApplied => Result.Success(Unit.Value),
            TaskRunMutationOutcome.NotFound => Result.Failure<Unit>(TaskRuntimeOperationErrors.RunNotFound),
            TaskRunMutationOutcome.Conflict => Result.Failure<Unit>(TaskRuntimeOperationErrors.ConcurrentMutation),
            TaskRunMutationOutcome.InvalidRequest => Result.Failure<Unit>(TaskRuntimeOperationErrors.InvalidRunRequest),
            _ => Result.Failure<Unit>(TaskRuntimeOperationErrors.RunCannotBeCanceled)
        };
    }
}
