using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string requestPath, string responseBody)
        : base($"Maxio API call '{requestPath}' failed with HTTP {(int)statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        RequestPath = requestPath;
        ResponseBody = responseBody;
    }

    /// <summary>HTTP status code returned by Maxio.</summary>
    public int StatusCode { get; }

    /// <summary>Path of the failing request (never contains the API key).</summary>
    public string RequestPath { get; }

    /// <summary>Raw error body returned by Maxio (may include field-level errors).</summary>
    public string ResponseBody { get; }
}
