using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Raised when a Walmart API call fails (transport error, non-success status, or an error body
/// returned per the contract's error model). Carries the HTTP status when there was a response.
/// Never carries secrets or tokens.
/// </summary>
public class WalmartApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ErrorCode { get; }

    public WalmartApiException(string message, HttpStatusCode? statusCode = null, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
