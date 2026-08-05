namespace Gma.Modules.TaskRuntime.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Commands;

internal sealed class RetryTaskRunCommandHandler(
    ITaskRunStore store,
    ISystemClock clock)
    : ICommandHandler<RetryTaskRunCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RetryTaskRunCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty)
        {
            return Result.Failure<Unit>(TaskRuntimeApplicationErrors.InvalidRunId);
        }

        TaskRunMutationOutcome outcome = await store.RetryAsync(
                command.RunId,
                command.RequestedBy,
                command.ScheduledAtUtc ?? clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            TaskRunMutationOutcome.Applied => Result.Success(Unit.Value),
            TaskRunMutationOutcome.NotFound => Result.Failure<Unit>(TaskRuntimeApplicationErrors.RunNotFound),
            TaskRunMutationOutcome.Conflict => Result.Failure<Unit>(TaskRuntimeApplicationErrors.ConcurrentMutation),
            TaskRunMutationOutcome.InvalidRequest => Result.Failure<Unit>(TaskRuntimeApplicationErrors.InvalidRunRequest),
            TaskRunMutationOutcome.ScopeClosed => Result.Failure<Unit>(TaskRuntimeApplicationErrors.ScopeClosed),
            _ => Result.Failure<Unit>(TaskRuntimeApplicationErrors.RunCannotBeRetried)
        };
    }
}
