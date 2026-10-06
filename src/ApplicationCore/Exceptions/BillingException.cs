using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider (Maxio) cannot complete a requested operation.
/// Carries the HTTP status the caller should see; messages are always caller-safe.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode is >= 400 and <= 599 ? statusCode : 502;
    }

    public int StatusCode { get; }
}