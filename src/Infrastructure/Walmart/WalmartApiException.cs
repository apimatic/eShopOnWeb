using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Raised when a Walmart Marketplace API call fails in a way the integration cannot recover from.
/// The message is derived from the contract's error model and never contains credentials or tokens.
/// </summary>
public class WalmartApiException : Exception
{
    public WalmartApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
