using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API.
/// Error payloads follow the shapes declared in maxio-spec/components/schemas/errors:
/// Error-List-Response ({"errors": ["..."]}), Error-Array-Map-Response ({"errors": {"field": ["..."]}}),
/// Customer-Error-Response ({"errors": {"customer": "..."}}), Single-String-Error-Response
/// ({"errors": "..."}) and Single-Error-Response ({"error": "..."}). Some operations answer with a
/// bare JSON string (for example "A valid product_family_id is required").
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string Path { get; }
    public IReadOnlyList<string> Errors { get; }
    public string? RawBody { get; }

    /// <summary>True when the request itself was rejected and retrying it unchanged cannot help.</summary>
    public bool IsValidationFailure => StatusCode == HttpStatusCode.UnprocessableEntity
        || StatusCode == HttpStatusCode.BadRequest;

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail supplied";
        return $"Maxio API request {method} {path} failed with status {(int)statusCode} ({statusCode}): {detail}";
    }

    /// <summary>
    /// Extracts human readable messages from any of the error payload shapes declared by the spec.
    /// Never throws: an unparseable body degrades to an empty list.
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
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return Single(root.GetString());
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            if (root.TryGetProperty("error", out var singleError) && singleError.ValueKind == JsonValueKind.String)
            {
                return Single(singleError.GetString());
            }

            if (!root.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.String:
                    return Single(errors.GetString());

                case JsonValueKind.Array:
                    return errors.EnumerateArray()
                        .Select(Flatten)
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .Select(message => message!)
                        .ToArray();

                case JsonValueKind.Object:
                    var messages = new List<string>();
                    foreach (var property in errors.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            messages.AddRange(property.Value.EnumerateArray()
                                .Select(Flatten)
                                .Where(message => !string.IsNullOrWhiteSpace(message))
                                .Select(message => $"{property.Name}: {message}"));
                        }
                        else
                        {
                            var message = Flatten(property.Value);
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                messages.Add($"{property.Name}: {message}");
                            }
                        }
                    }

                    return messages;

                default:
                    return Array.Empty<string>();
            }
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page from a proxy, for example) - surface a trimmed excerpt.
            var trimmed = body!.Trim();
            return Single(trimmed.Length > 500 ? trimmed[..500] : trimmed);
        }
    }

    private static string? Flatten(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();

    private static IReadOnlyList<string> Single(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
}
