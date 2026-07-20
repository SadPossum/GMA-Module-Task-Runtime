namespace Gma.Modules.TaskRuntime.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Commands;

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
            return Result.Failure<Unit>(TaskRuntimeApplicationErrors.InvalidRunId);
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
            TaskRunMutationOutcome.NotFound => Result.Failure<Unit>(TaskRuntimeApplicationErrors.RunNotFound),
            TaskRunMutationOutcome.Conflict => Result.Failure<Unit>(TaskRuntimeApplicationErrors.ConcurrentMutation),
            TaskRunMutationOutcome.InvalidRequest => Result.Failure<Unit>(TaskRuntimeApplicationErrors.InvalidRunRequest),
            _ => Result.Failure<Unit>(TaskRuntimeApplicationErrors.RunCannotBeCanceled)
        };
    }
}
