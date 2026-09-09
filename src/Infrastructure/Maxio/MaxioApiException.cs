using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string body, string? message = null)
        : base(message ?? $"Maxio API request failed with status {(HttpStatusCode)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        Body = body;
    }

    public int StatusCode { get; }

    public string Body { get; }

    /// <summary>Parses the standard Maxio error envelope: { "errors": [ ... ] }.</summary>
    public IReadOnlyList<string> ParseErrors()
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var e in errors.EnumerateArray())
                {
                    list.Add(e.ToString());
                }
                return list;
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return Array.Empty<string>();
    }
}

/// <summary>
/// Raised when the Maxio integration is not configured correctly.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
