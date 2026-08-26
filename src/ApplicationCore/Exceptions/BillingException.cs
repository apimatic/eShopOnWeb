using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="StatusCode"/> carries the provider's
/// HTTP status when one exists (4xx = caller-actionable rejection), or a 5xx for transport
/// failures and unprocessable provider responses.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = 500, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
