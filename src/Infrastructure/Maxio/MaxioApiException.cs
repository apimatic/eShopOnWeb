using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call returns an unexpected (non-success, non-handled) response.
/// Carries the HTTP status and raw body so callers can surface a meaningful error.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string? ResponseBody { get; }
}
