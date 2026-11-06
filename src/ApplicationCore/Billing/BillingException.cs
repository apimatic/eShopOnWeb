using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The single failure type surfaced by the billing integration. Message is always safe to return to callers.
/// </summary>
public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
