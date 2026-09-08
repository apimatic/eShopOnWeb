using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails or returns an unexpected result.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public MaxioApiException(string message, int statusCode, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// HTTP status code returned by the Maxio API (0 when the call never reached the API).
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Raw response body returned by the Maxio API, if any.
    /// </summary>
    public string? ResponseBody { get; }
}
