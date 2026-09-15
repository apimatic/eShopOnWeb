using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Billing-provider failure already mapped to a caller-safe HTTP status and message.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
