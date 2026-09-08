using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response for a request that
/// was otherwise well-formed. Carries the HTTP status and the raw response body for diagnostics.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string? responseBody, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned by the Maxio API.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body returned by the Maxio API (may be null/empty).</summary>
    public string? ResponseBody { get; }
}
