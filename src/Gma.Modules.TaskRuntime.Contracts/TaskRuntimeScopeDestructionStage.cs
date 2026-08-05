namespace Gma.Modules.TaskRuntime.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(TaskRuntimeContractKebabCaseEnumJsonConverter<TaskRuntimeScopeDestructionStage>))]
public enum TaskRuntimeScopeDestructionStage
{
    Unknown = 0,
    QuiesceRuns = 1,
    ControlMessages = 2,
    TaskRuns = 3,
    Completed = 4
}
