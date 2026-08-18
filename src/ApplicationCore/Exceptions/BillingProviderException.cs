using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Billing-provider failure already translated for the HTTP boundary.
/// <see cref="Message"/> is caller-safe — never an SDK or framework exception message.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public bool IsClientError => StatusCode >= 400 && StatusCode < 500;
}
