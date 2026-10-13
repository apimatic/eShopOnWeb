using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Raised when a Walmart API call fails — a non-success HTTP status, an OAuth/authorization
/// failure, or a malformed response. Carries the status code and a message safe to surface;
/// it never includes credentials or tokens.
/// </summary>
public class WalmartApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public WalmartApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
