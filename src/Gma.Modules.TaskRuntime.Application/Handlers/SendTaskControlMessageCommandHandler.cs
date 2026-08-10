namespace Gma.Modules.TaskRuntime.Application.Handlers;

using System.Text.Json;
using Gma.Framework.Cqrs;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Tasks;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Results;
using Gma.Modules.TaskRuntime.Application.Commands;
using Gma.Modules.TaskRuntime.Contracts;

internal sealed class SendTaskControlMessageCommandHandler(
    ITaskRunStore store,
    IIdGenerator idGenerator,
    ISystemClock clock)
    : ICommandHandler<SendTaskControlMessageCommand, TaskControlMessage>
{
    public async Task<Result<TaskControlMessage>> HandleAsync(
        SendTaskControlMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RunId == Guid.Empty)
        {
            return Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.InvalidRunId);
        }

        if (!IsValidJson(command.PayloadJson))
        {
            return Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.InvalidPayloadJson);
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        TaskControlMessage message;
        try
        {
            message = new TaskControlMessage(
                idGenerator.NewId(),
                command.RunId,
                command.CommandName,
                command.PayloadJson,
                nowUtc,
                command.RequestedBy,
                command.ExpiresAtUtc);
        }
        catch (ArgumentException)
        {
            return Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.InvalidControlMessage);
        }

        TaskControlMessageEnqueueOutcome outcome = await store
            .EnqueueControlMessageAsync(message, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            TaskControlMessageEnqueueOutcome.Enqueued or TaskControlMessageEnqueueOutcome.AlreadyExists =>
                Result.Success(message),
            TaskControlMessageEnqueueOutcome.RunNotFound =>
                Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.RunNotFound),
            TaskControlMessageEnqueueOutcome.Conflict =>
                Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.ConcurrentMutation),
            TaskControlMessageEnqueueOutcome.ScopeClosed =>
                Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.ScopeClosed),
            _ => Result.Failure<TaskControlMessage>(TaskRuntimeOperationErrors.RunCannotBeControlled)
        };
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
