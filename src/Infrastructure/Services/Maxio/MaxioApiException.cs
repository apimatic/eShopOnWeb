using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public MaxioApiException(int statusCode, string? responseBody)
        : base($"Maxio API request failed with status {(HttpStatusCode)statusCode} ({(int)statusCode}). Response: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
