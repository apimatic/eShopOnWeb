using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the Maxio Advanced Billing API rejects or fails a request. Carries the HTTP
/// status code and the error payload returned by Maxio so callers can surface a meaningful
/// message.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, string? responseBody = null)
        : base(string.IsNullOrWhiteSpace(responseBody) ? message : $"{message} {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned by the Maxio API.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body returned by the Maxio API, when available.</summary>
    public string? ResponseBody { get; }
}
