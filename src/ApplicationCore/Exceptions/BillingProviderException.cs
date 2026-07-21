using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown by <see cref="Interfaces.IBillingClient"/> when the billing provider rejects a call
/// or is unreachable. Carries the provider's HTTP status code (when known) so callers can decide
/// whether a retry is safe.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingProviderException(string message, Exception innerException, int? statusCode = null) : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
