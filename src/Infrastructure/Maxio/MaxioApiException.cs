using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, string message, IReadOnlyList<string> errors)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public static MaxioApiException FromResponse(int statusCode, string body)
    {
        var errors = ParseErrors(body);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : $"HTTP {(int)statusCode} {statusCode}";
        return new MaxioApiException(statusCode, $"Maxio API request failed: {detail}", errors);
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var result = new List<string>();
            CollectErrors(document.RootElement, result);
            return result;
        }
        catch (System.Text.Json.JsonException)
        {
            return new[] { body };
        }
    }

    private static void CollectErrors(System.Text.Json.JsonElement element, List<string> result)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrors(item, result);
                }
                break;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        result.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        CollectErrors(property.Value, result);
                    }
                }
                break;
        }
    }
}
