using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Failure talking to the billing provider. <see cref="StatusCode"/> is the HTTP status
/// to return to the caller — never an SDK or framework exception message.
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
