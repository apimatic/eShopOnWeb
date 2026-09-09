using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response.
/// Exposes the HTTP status code and the errors returned by the API.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio API request failed with status {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        Errors = ParseErrors(responseBody);
    }

    private static IReadOnlyList<string> ParseErrors(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind == JsonValueKind.Array
                    ? errors.EnumerateArray().Select(e => e.ToString()).ToList()
                    : new List<string> { errors.ToString() };
            }

            return new List<string> { responseBody };
        }
        catch (JsonException)
        {
            return new List<string> { responseBody };
        }
    }
}
