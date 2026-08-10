namespace Gma.Modules.TaskRuntime.Contracts;

public sealed record TaskRunControlRequest(
    Guid RunId,
    string CommandName,
    string PayloadJson,
    DateTimeOffset? ExpiresAtUtc = null,
    string? RequestedBy = null);
