using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// The HTTP status code returned by the Billing API.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The raw response body returned by the Billing API (may contain its <c>errors</c> list).
    /// </summary>
    public string ResponseBody { get; }

    public IReadOnlyList<string> ErrorMessages => MaxioErrorParser.ParseErrors(ResponseBody);
}

/// <summary>
/// Thrown when creating a customer whose <c>reference</c> is already taken
/// (i.e. a customer with the same app-side id already exists in Maxio).
/// </summary>
public class MaxioReferenceTakenException : MaxioApiException
{
    public MaxioReferenceTakenException(string message, int statusCode, string responseBody)
        : base(message, statusCode, responseBody)
    {
    }
}

/// <summary>
/// Thrown when a requested Maxio resource does not exist (HTTP 404).
/// </summary>
public class MaxioNotFoundException : MaxioApiException
{
    public MaxioNotFoundException(string message, int statusCode, string responseBody)
        : base(message, statusCode, responseBody)
    {
    }
}

internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> ParseErrors(string responseBody)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return errors;
        }

        try
        {
            var parsed = System.Text.Json.JsonDocument.Parse(responseBody);
            if (parsed.RootElement.TryGetProperty("errors", out var element) && element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var e in element.EnumerateArray())
                {
                    errors.Add(e.GetString() ?? string.Empty);
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Response was not JSON; fall back to the raw body being surfaced by the message.
        }

        return errors;
    }
}