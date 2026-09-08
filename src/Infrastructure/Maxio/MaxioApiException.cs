using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Billing API.
/// </summary>
public class MaxioApiException : System.Exception
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public MaxioApiException(HttpStatusCode statusCode, string responseBody, string? message = null)
        : base(BuildMessage(statusCode, responseBody, message))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = ParseErrors(responseBody);
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The raw response body returned by Maxio (may contain error details).
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Error messages extracted from the response body, when present.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the submission as a duplicate (Duplicate Prevention /
    /// uniqueness_token conflict). The first request may still have been processed.
    /// </summary>
    public bool IsDuplicateSubmission => StatusCode == HttpStatusCode.Conflict;

    private static string BuildMessage(HttpStatusCode statusCode, string responseBody, string? message)
    {
        var detail = string.IsNullOrWhiteSpace(message)
            ? string.Join("; ", ParseErrors(responseBody).DefaultIfEmpty(responseBody))
            : message!;
        return $"Maxio API call failed with {(int)statusCode} {statusCode}: {detail}";
    }

    private static IReadOnlyList<string> ParseErrors(string? responseBody)
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
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return errors.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(e => !string.IsNullOrWhiteSpace(e))
                        .ToArray();
                }
                if (errors.ValueKind == JsonValueKind.String)
                {
                    return new[] { errors.GetString()! };
                }
            }
        }
        catch (JsonException)
        {
            // body was not JSON; fall through
        }

        return Array.Empty<string>();
    }
}
