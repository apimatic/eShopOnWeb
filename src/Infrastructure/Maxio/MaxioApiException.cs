using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        return $"Maxio API returned {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}";
    }
}

public static class MaxioApiErrorParser
{
    public static IReadOnlyList<string> ParseErrors(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out JsonElement errors))
            {
                return new[] { content };
            }

            var result = new List<string>();
            switch (errors.ValueKind)
            {
                case JsonValueKind.String:
                    result.Add(errors.GetString()!);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in errors.EnumerateArray())
                    {
                        result.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.ToString());
                    }
                    break;
                case JsonValueKind.Object:
                    foreach (JsonProperty property in errors.EnumerateObject())
                    {
                        result.Add($"{property.Name}: {property.Value}");
                    }
                    break;
                default:
                    result.Add(errors.ToString());
                    break;
            }

            return result;
        }
        catch (JsonException)
        {
            return new[] { content };
        }
    }
}
