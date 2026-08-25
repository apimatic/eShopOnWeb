using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="StatusCode"/> is the HTTP status the
/// API should surface; <see cref="Exception.Message"/> is always caller-safe (no SDK internals).
/// </summary>
public class BillingServiceException : Exception
{
    public BillingServiceException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
