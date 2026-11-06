using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Turns the typed error of a failed Maxio call into a short, human-readable reason.
/// </summary>
public static class MaxioErrorText
{
    public static string From(ApiException ex)
    {
        try
        {
            var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
            var json = error is null ? null : ErrorJson(error);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var messages = new List<string>();
                using var doc = JsonDocument.Parse(json);
                Collect(doc.RootElement, null, messages);
                var text = string.Join(" ", messages.Distinct());
                if (text.Length > 0) return Truncate(text);
            }
        }
        catch (Exception)
        {
            // fall through to the generic text below
        }
        return $"Maxio answered with HTTP {(int)ex.StatusCode}.";
    }

    private static string? ErrorJson(object error)
    {
        foreach (var method in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].IsOut) continue;
            var args = new object?[1];
            if (method.Invoke(error, args) is not true || args[0] is null) continue;
            if (args[0] is RawError raw) return raw.ReadAsString();
            return JsonSerializer.Serialize(args[0], args[0]!.GetType());
        }
        return null;
    }

    private static void Collect(JsonElement element, string? key, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s)) messages.Add(key is null ? s! : $"{key}: {s}");
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, key, messages);
                break;
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    var name = p.Name is "errors" or "error" or "message" ? key : p.Name;
                    Collect(p.Value, name, messages);
                }
                break;
        }
    }

    private static string Truncate(string text) => text.Length <= 400 ? text : text.Substring(0, 400) + "...";
}
