using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PayPalServerSdk.Core.Extensions;

internal static class ObjectExtensions
{
    extension(object? value)
    {
        public object? Normalize()
        {
            switch (value)
            {
                case null:
                    return null;
                // Already a dictionary-like input, but normalize values recursively
                case IDictionary<string, object?> dict:
                    return dict.ToDictionary(kv => kv.Key, kv => kv.Value.Normalize());
                // Already a list-like input, normalize items
                case IEnumerable<object?> list:
                    return list.Select(Normalize).ToList();
                default:
                    {
                        // Serialize to JSON and parse recursively
                        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
                        return object.ConvertElement(doc.RootElement);
                    }
            }
        }

        private static object? ConvertElement(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var i) ? i
                    : element.TryGetDecimal(out var d) ? d
                    : element.GetDouble(),
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(p => p.Name, p => object.ConvertElement(p.Value)),
                JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToList(),
                _ => element.GetRawText()
            };
    }
}