using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned by the Maxio API.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body returned by the Maxio API, when available.</summary>
    public string? ResponseBody { get; }
}
