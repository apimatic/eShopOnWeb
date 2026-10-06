using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Billing API returns a non-success, non-404 response
/// (or the request fails after retries).
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public MaxioApiException(HttpStatusCode statusCode, string responseBody, string? message = null)
        : base(message ?? $"Maxio Billing API request failed with status {(int)statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}