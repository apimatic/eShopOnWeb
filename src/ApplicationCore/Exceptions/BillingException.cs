using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Billing-provider failure mapped to a caller-safe HTTP status.
/// </summary>
public sealed class BillingException : Exception
{
    public BillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
