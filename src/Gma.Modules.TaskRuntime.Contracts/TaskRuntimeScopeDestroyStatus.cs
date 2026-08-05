namespace Gma.Modules.TaskRuntime.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(TaskRuntimeContractKebabCaseEnumJsonConverter<TaskRuntimeScopeDestroyStatus>))]
public enum TaskRuntimeScopeDestroyStatus
{
    Unknown = 0,
    Invalid = 1,
    InProgress = 2,
    Completed = 3,
    Replayed = 4,
    Stale = 5,
    Busy = 6,
    Conflict = 7
}
