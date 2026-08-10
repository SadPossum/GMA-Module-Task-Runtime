namespace Gma.Modules.TaskRuntime.Admin.Contracts;

using Gma.Framework.Results;

public static class TaskRuntimeAdminInputErrors
{
    public static readonly Error InvalidStatus = new("TaskRuntime.InvalidStatus", "The task run status is not supported.");
    public static readonly Error PayloadRequired = new("TaskRuntime.PayloadRequired", "Provide either --payload-json or --payload-file.");
    public static readonly Error PayloadSourceConflict = new("TaskRuntime.PayloadSourceConflict", "Use either --payload-json or --payload-file, not both.");
    public static readonly Error PayloadFileNotFound = new("TaskRuntime.PayloadFileNotFound", "The task payload file was not found.");
}
