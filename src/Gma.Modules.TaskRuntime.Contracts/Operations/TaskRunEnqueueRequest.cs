namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Tasks;

public sealed record TaskRunEnqueueRequest(
    string ModuleName,
    string TaskName,
    string PayloadJson,
    DateTimeOffset? ScheduledAtUtc = null,
    string WorkerGroup = TaskWorkerGroups.Default,
    string? ScopeId = null,
    Guid? CorrelationId = null,
    string? RequestedBy = null,
    int MaxAttempts = 1,
    int PayloadVersion = 1,
    string? DeduplicationKey = null,
    Guid? RunId = null);
