using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base(BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = ExtractErrors(responseBody);
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string responseBody)
    {
        var errors = ExtractErrors(responseBody);
        return errors.Count > 0
            ? $"Maxio Advanced Billing returned {(int)statusCode} ({statusCode}): {string.Join(" ", errors)}"
            : $"Maxio Advanced Billing returned {(int)statusCode} ({statusCode}).";
    }

    private static IReadOnlyList<string> ExtractErrors(string responseBody)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    errors.Add(document.RootElement.GetString()!);
                }
                return errors;
            }

            if (document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                CollectErrorValues(errorsElement, errors);
            }
            else if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                CollectErrorValues(errorElement, errors);
            }
        }
        catch (JsonException)
        {
            // Non-JSON response body (e.g. plain text); fall back to the raw body.
            errors.Add(responseBody);
        }

        return errors;
    }

    private static void CollectErrorValues(JsonElement element, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        errors.Add(item.GetString()!);
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        errors.Add($"{property.Name}: {property.Value.GetString()}");
                    }
                }
                break;
            case JsonValueKind.String:
                errors.Add(element.GetString()!);
                break;
        }
    }
}
