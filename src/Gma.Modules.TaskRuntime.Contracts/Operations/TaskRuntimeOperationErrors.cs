namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Results;

public static class TaskRuntimeOperationErrors
{
    public static readonly Error RunNotFound = new("TaskRuntime.RunNotFound", "The task run was not found.");
    public static readonly Error InvalidPayloadJson = new("TaskRuntime.InvalidPayloadJson", "The task payload must be valid JSON.");
    public static readonly Error InvalidRunId = new("TaskRuntime.InvalidRunId", "The task run id must not be empty.");
    public static readonly Error RunCannotBeCanceled = new("TaskRuntime.RunCannotBeCanceled", "The task run cannot be canceled from its current status.");
    public static readonly Error RunCannotBeRetried = new("TaskRuntime.RunCannotBeRetried", "The task run cannot be retried from its current status.");
    public static readonly Error RunCannotBeControlled = new("TaskRuntime.RunCannotBeControlled", "The task run cannot receive control messages from its current status.");
    public static readonly Error InvalidControlMessage = new("TaskRuntime.InvalidControlMessage", "The task control message is invalid.");
    public static readonly Error InvalidRunRequest = new("TaskRuntime.InvalidRunRequest", "The task run request is invalid.");
    public static readonly Error InvalidRunFilter = new("TaskRuntime.InvalidRunFilter", "The task run filter is invalid.");
    public static readonly Error ConcurrentMutation = new("TaskRuntime.ConcurrentMutation", "The task run changed concurrently. Retry the operation.");
    public static readonly Error ScopeClosed = new("TaskRuntime.ScopeClosed", "The task scope is not accepting new work.");
}
