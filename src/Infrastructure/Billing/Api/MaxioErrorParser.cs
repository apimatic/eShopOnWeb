using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Api;

/// <summary>
/// Parses the Maxio error models described in the OpenAPI spec. Validation failures return
/// 422 with an "errors" member that is either a list of strings or an object keyed by field.
/// Other failures may return a bare body. Never trust the raw body verbatim into logs.
/// </summary>
public static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string? body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
            return errors;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("errors", out var errorsElement))
            {
                switch (errorsElement.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errorsElement.EnumerateArray())
                            errors.Add(Describe(item));
                        break;
                    case JsonValueKind.Object:
                        foreach (var property in errorsElement.EnumerateObject())
                            errors.Add($"{property.Name}: {Describe(property.Value)}");
                        break;
                    case JsonValueKind.String:
                        errors.Add(errorsElement.GetString()!);
                        break;
                }
            }
            else if (root.ValueKind == JsonValueKind.String)
            {
                errors.Add(root.GetString()!);
            }
            else
            {
                errors.Add(body);
            }
        }
        catch (JsonException)
        {
            errors.Add(body);
        }

        return errors;
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => string.Join("; ", element.EnumerateArray().Select(Describe)),
        JsonValueKind.String => element.GetString() ?? string.Empty,
        _ => element.GetRawText()
    };
}
