using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Error response body returned by Maxio Advanced Billing on non-success status
/// codes. The <c>errors</c> property may be an array of strings, a map of field
/// names to messages, or a map of field names to arrays of messages, so it is
/// kept as raw JSON and flattened when building an exception message.
/// </summary>
public class MaxioErrorResponse
{
    public JsonElement? Errors { get; set; }

    public string ToMessage()
    {
        if (Errors is not JsonElement errors || errors.ValueKind == JsonValueKind.Null)
        {
            return "The Maxio Advanced Billing API returned an error.";
        }

        var messages = new List<string>();
        Flatten(errors, messages);
        return messages.Count > 0
            ? string.Join(" ", messages)
            : "The Maxio Advanced Billing API returned an error.";
    }

    private static void Flatten(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, messages);
                }
                break;
        }
    }
}
