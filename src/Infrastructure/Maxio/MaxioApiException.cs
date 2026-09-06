using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API. Translated into an application level
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingException"/> before it leaves the
/// infrastructure layer.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string Path { get; }

    /// <summary>Messages Maxio returned in the response body.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the request because an identical one, carrying the same
    /// <c>uniqueness_token</c>, was already received within the last 60 minutes.
    /// </summary>
    public bool IsDuplicateSubmission => StatusCode == HttpStatusCode.Conflict;

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio returned {(int)statusCode} {statusCode} for {method} {path}: {detail}";
    }

    /// <summary>
    /// Extracts the messages from a Maxio error body. Maxio returns either
    /// <c>{"errors": ["..."]}</c> or <c>{"errors": {"field": "..."}}</c> depending on the endpoint,
    /// and occasionally a bare string; anything unparseable yields an empty list.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.String => new[] { errors.GetString()! },
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(element => element.ValueKind == JsonValueKind.String
                        ? element.GetString()
                        : element.ToString())
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => message!)
                    .ToArray(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .Select(property => property.Value.ValueKind == JsonValueKind.String
                        ? $"{property.Name}: {property.Value.GetString()}"
                        : $"{property.Name}: {property.Value}")
                    .ToArray(),
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
