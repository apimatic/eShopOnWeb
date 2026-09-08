using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when Maxio configuration is missing/invalid and a call cannot be attempted.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the Maxio API returns a non-success status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string requestPath, string content)
        : base(BuildMessage(statusCode, requestPath, content))
    {
        StatusCode = statusCode;
        RequestPath = requestPath;
        Content = content;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestPath { get; }

    /// <summary>Raw response body returned by Maxio (may be empty).</summary>
    public string Content { get; }

    /// <summary>
    /// Best-effort extraction of the Maxio error message(s). Maxio returns several error
    /// shapes (arrays of strings, maps, and single objects) all nested under "errors".
    /// </summary>
    public string? ErrorMessage { get; set; }

    private static string BuildMessage(HttpStatusCode statusCode, string requestPath, string content)
    {
        var snippet = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
        return $"Maxio API request failed: {(int)statusCode} ({statusCode}) for {requestPath}. Response: {snippet}";
    }
}
