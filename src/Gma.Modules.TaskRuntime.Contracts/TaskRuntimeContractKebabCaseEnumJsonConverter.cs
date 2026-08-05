namespace Gma.Modules.TaskRuntime.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class TaskRuntimeContractKebabCaseEnumJsonConverter<TEnum>
    : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ValuesByWireName =
        Enum.GetValues<TEnum>()
            .Where(value => !EqualityComparer<TEnum>.Default.Equals(value, default))
            .ToDictionary(
                value => JsonNamingPolicy.KebabCaseLower.ConvertName(
                    Enum.GetName(value)!),
                value => value,
                StringComparer.Ordinal);

    private static readonly Dictionary<TEnum, string> WireNamesByValue =
        ValuesByWireName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String ||
            !ValuesByWireName.TryGetValue(reader.GetString() ?? string.Empty, out TEnum value))
        {
            throw new JsonException($"{typeof(TEnum).Name} is invalid.");
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        if (!WireNamesByValue.TryGetValue(value, out string? wireName))
        {
            throw new JsonException($"{typeof(TEnum).Name} is invalid.");
        }

        writer.WriteStringValue(wireName);
    }
}
