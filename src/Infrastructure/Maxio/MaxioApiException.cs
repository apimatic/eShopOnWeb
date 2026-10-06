using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Billing API returns a non-success response or
/// the request could not be completed.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string? responseBody,
        IReadOnlyList<string>? errors = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors ?? Array.Empty<string>();
    }

    public static MaxioApiException FromResponse(HttpResponseMessage response, string responseBody)
    {
        var errors = ParseErrors(responseBody);
        var detail = errors.Count > 0
            ? string.Join("; ", errors)
            : $"Maxio Billing API returned {(int)response.StatusCode} {response.StatusCode}.";

        return new MaxioApiException(
            $"Maxio Billing API error ({(int)response.StatusCode} {response.StatusCode}): {detail}",
            (int)response.StatusCode,
            responseBody,
            errors);
    }

    private static List<string> ParseErrors(string responseBody)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return errors;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        errors.Add(item.GetString() ?? string.Empty);
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                     doc.RootElement.TryGetProperty("errors", out var element) &&
                     element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        errors.Add(item.GetString() ?? string.Empty);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON body: fall through with the raw body available in ResponseBody.
        }

        return errors;
    }

    /// <summary>HTTP status returned by the Billing API (0 when the call failed before a response).</summary>
    public int StatusCode { get; }

    /// <summary>Raw response body, useful for diagnostics.</summary>
    public string? ResponseBody { get; }

    /// <summary>Structured error list from the Billing API, when present.</summary>
    public IReadOnlyList<string> Errors { get; }
}