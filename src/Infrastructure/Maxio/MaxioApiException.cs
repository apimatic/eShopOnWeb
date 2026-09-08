using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success status code.
/// Carries the HTTP status and the error payload returned by the API.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public MaxioApiException(int statusCode, string? responseBody)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
