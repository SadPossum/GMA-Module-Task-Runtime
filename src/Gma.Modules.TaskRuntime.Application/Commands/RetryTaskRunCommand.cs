namespace Gma.Modules.TaskRuntime.Application.Commands;

using Gma.Framework.Cqrs;

internal sealed record RetryTaskRunCommand(
    Guid RunId,
    string? RequestedBy,
    DateTimeOffset? ScheduledAtUtc) : ITransactionalCommand<Unit>;
