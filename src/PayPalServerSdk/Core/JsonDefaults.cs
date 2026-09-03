using System.Text.Json;

namespace PayPalServerSdk.Core;

internal static class JsonDefaults
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
