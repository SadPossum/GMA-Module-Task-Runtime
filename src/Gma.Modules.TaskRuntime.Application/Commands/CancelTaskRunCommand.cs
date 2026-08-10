namespace Gma.Modules.TaskRuntime.Application.Commands;

using Gma.Framework.Cqrs;

internal sealed record CancelTaskRunCommand(
    Guid RunId,
    string? RequestedBy) : ITransactionalCommand<Unit>;
