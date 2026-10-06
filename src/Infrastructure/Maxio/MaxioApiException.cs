using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success status code.
/// Carries the HTTP status and any error payload the API returned so callers
/// (e.g. the exception middleware) can surface meaningful errors.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Flattened list of error messages returned by the API ("errors" can be an
    /// array of strings or a map of field -> message(s) per the spec error models).
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        Errors = ParseErrors(responseBody);
    }

    private static IReadOnlyList<string> ParseErrors(string responseBody)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return result;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                Collect(errors, result);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Non-JSON error body; expose it verbatim.
            result.Add(responseBody);
        }

        if (result.Count == 0)
        {
            result.Add(responseBody);
        }

        return result;
    }

    private static void Collect(System.Text.Json.JsonElement element, List<string> result)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, result);
                }
                break;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    Collect(prop.Value, result);
                }
                break;
            case System.Text.Json.JsonValueKind.String:
                result.Add(element.GetString() ?? string.Empty);
                break;
            default:
                result.Add(element.ToString());
                break;
        }
    }
}