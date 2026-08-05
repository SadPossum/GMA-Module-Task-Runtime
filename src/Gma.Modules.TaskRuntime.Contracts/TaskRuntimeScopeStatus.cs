namespace Gma.Modules.TaskRuntime.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(TaskRuntimeContractKebabCaseEnumJsonConverter<TaskRuntimeScopeStatus>))]
public enum TaskRuntimeScopeStatus
{
    Unknown = 0,
    Invalid = 1,
    Missing = 2,
    Open = 3,
    Closing = 4,
    Closed = 5
}
