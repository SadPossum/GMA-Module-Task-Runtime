namespace Gma.Modules.TaskRuntime.Persistence;

using System.Security.Cryptography;
using System.Text;

internal static class TaskRuntimeLifecycleHashes
{
    public static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
