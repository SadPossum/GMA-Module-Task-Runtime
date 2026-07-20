namespace Gma.Modules.TaskRuntime.Application.Handlers;

using System.Text.Json;
using Gma.Framework.Cqrs;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Tasks;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Commands;

internal sealed class EnqueueTaskRunCommandHandler(
    ITaskRunStore store,
    IIdGenerator idGenerator,
    ISystemClock clock)
    : ICommandHandler<EnqueueTaskRunCommand, TaskRunDetails>
{
    public async Task<Result<TaskRunDetails>> HandleAsync(
        EnqueueTaskRunCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsValidJson(command.PayloadJson))
        {
            return Result.Failure<TaskRunDetails>(TaskRuntimeApplicationErrors.InvalidPayloadJson);
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        Guid runId = command.RunId.GetValueOrDefault();
        if (runId == Guid.Empty)
        {
            runId = idGenerator.NewId();
        }

        DateTimeOffset scheduledAtUtc = command.ScheduledAtUtc ?? nowUtc;
        TaskRunRequest request;
        try
        {
            request = new TaskRunRequest(
                runId,
                command.ModuleName,
                command.TaskName,
                command.PayloadJson,
                nowUtc,
                scheduledAtUtc,
                command.WorkerGroup,
                command.ScopeId,
                command.CorrelationId,
                command.RequestedBy,
                command.MaxAttempts,
                command.PayloadVersion,
                command.DeduplicationKey);
        }
        catch (ArgumentException)
        {
            return Result.Failure<TaskRunDetails>(TaskRuntimeApplicationErrors.InvalidRunRequest);
        }

        TaskRunEnqueueResult enqueue = await store.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
        return Result.Success(enqueue.Run);
    }

    private static bool IsValidJson(string payloadJson)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(payloadJson);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return false;
        }
    }
}
