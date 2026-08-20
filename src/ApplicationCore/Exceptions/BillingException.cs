using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Billing-provider failure already translated for the HTTP boundary.
/// <see cref="StatusCode"/> is the status to return to the caller; <see cref="Exception.Message"/> is caller-safe.
/// </summary>
public sealed class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
