using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when Maxio Advanced Billing returns an unexpected/failure response.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio API call failed with status {(int)statusCode} {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// True when the failure reflects an invalid request (4xx) rather than an upstream outage (5xx).
    /// </summary>
    public bool IsClientError => (int)StatusCode is >= 400 and < 500;
}
