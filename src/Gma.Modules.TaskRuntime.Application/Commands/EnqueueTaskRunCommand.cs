namespace Gma.Modules.TaskRuntime.Application.Commands;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;

internal sealed record EnqueueTaskRunCommand(
    Guid? RunId,
    string ModuleName,
    string TaskName,
    string PayloadJson,
    DateTimeOffset? ScheduledAtUtc,
    string WorkerGroup,
    string? ScopeId,
    Guid? CorrelationId,
    string? RequestedBy,
    int MaxAttempts,
    int PayloadVersion,
    string? DeduplicationKey) : ITransactionalCommand<TaskRunDetails>;
