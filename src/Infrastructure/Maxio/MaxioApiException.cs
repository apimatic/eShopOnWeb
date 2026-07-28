using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call returns a non-success status. Parses the spec's error
/// envelope (<c>{ "errors": [...] | { field: msg } }</c>) into readable messages.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors, string rawBody)
        : base(BuildMessage(statusCode, operation, errors, rawBody))
    {
        StatusCode = statusCode;
        Operation = operation;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Operation { get; }

    public IReadOnlyList<string> Errors { get; }

    public string RawBody { get; }

    /// <summary>True for HTTP 422 (validation) responses.</summary>
    public bool IsUnprocessable => StatusCode == HttpStatusCode.UnprocessableEntity;

    private static string BuildMessage(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors, string rawBody)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : rawBody;
        return $"Maxio API call '{operation}' failed with status {(int)statusCode} ({statusCode}): {detail}";
    }

    /// <summary>
    /// Builds a <see cref="MaxioApiException"/> from a raw error response body, tolerating the
    /// several shapes the spec allows for the <c>errors</c> member.
    /// </summary>
    public static MaxioApiException FromResponse(HttpStatusCode statusCode, string operation, string rawBody)
    {
        return new MaxioApiException(statusCode, operation, ParseErrors(rawBody), rawBody);
    }

    private static IReadOnlyList<string> ParseErrors(string rawBody)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                CollectMessages(errors, messages);
            }
        }
        catch (JsonException)
        {
            // Body was not JSON; caller falls back to the raw body.
        }

        return messages;
    }

    private static void CollectMessages(JsonElement errors, List<string> messages)
    {
        switch (errors.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(errors.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    messages.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString());
                }
                break;
            case JsonValueKind.Object:
                foreach (var prop in errors.EnumerateObject())
                {
                    var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                    messages.Add($"{prop.Name}: {value}");
                }
                break;
        }
    }
}
