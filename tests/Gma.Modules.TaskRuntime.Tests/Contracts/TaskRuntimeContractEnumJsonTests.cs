namespace Gma.Modules.TaskRuntime.Tests;

using System.Text.Json;
using Gma.Modules.TaskRuntime.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TaskRuntimeContractEnumJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(TaskRuntimeScopeStatus.Invalid, "invalid")]
    [InlineData(TaskRuntimeScopeStatus.Missing, "missing")]
    [InlineData(TaskRuntimeScopeStatus.Open, "open")]
    [InlineData(TaskRuntimeScopeStatus.Closing, "closing")]
    [InlineData(TaskRuntimeScopeStatus.Closed, "closed")]
    public void Scope_status_uses_stable_wire_names(
        TaskRuntimeScopeStatus status,
        string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(TaskRuntimeScopeDestroyStatus.Invalid, "invalid")]
    [InlineData(TaskRuntimeScopeDestroyStatus.InProgress, "in-progress")]
    [InlineData(TaskRuntimeScopeDestroyStatus.Completed, "completed")]
    [InlineData(TaskRuntimeScopeDestroyStatus.Replayed, "replayed")]
    [InlineData(TaskRuntimeScopeDestroyStatus.Stale, "stale")]
    [InlineData(TaskRuntimeScopeDestroyStatus.Busy, "busy")]
    [InlineData(TaskRuntimeScopeDestroyStatus.Conflict, "conflict")]
    public void Scope_destroy_status_uses_stable_wire_names(
        TaskRuntimeScopeDestroyStatus status,
        string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(TaskRuntimeScopeDestructionStage.QuiesceRuns, "quiesce-runs")]
    [InlineData(TaskRuntimeScopeDestructionStage.ControlMessages, "control-messages")]
    [InlineData(TaskRuntimeScopeDestructionStage.TaskRuns, "task-runs")]
    [InlineData(TaskRuntimeScopeDestructionStage.Completed, "completed")]
    public void Scope_destruction_stage_uses_stable_wire_names(
        TaskRuntimeScopeDestructionStage stage,
        string wireName) =>
        AssertWireValue(stage, wireName);

    [Theory]
    [InlineData("1")]
    [InlineData("\"unknown\"")]
    [InlineData("\"future\"")]
    public void Scope_enums_reject_numeric_unknown_and_future_values(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TaskRuntimeScopeStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TaskRuntimeScopeDestroyStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TaskRuntimeScopeDestructionStage>(json, JsonOptions));
    }

    [Fact]
    public void Scope_enums_reject_unknown_writes()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            TaskRuntimeScopeStatus.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            TaskRuntimeScopeDestroyStatus.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            TaskRuntimeScopeDestructionStage.Unknown,
            JsonOptions));
    }

    private static void AssertWireValue<T>(T value, string wireName)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        Assert.Equal($"\"{wireName}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<T>(json, JsonOptions));
    }
}
