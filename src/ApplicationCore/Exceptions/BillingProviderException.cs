using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or cannot serve a request. Carries the provider's HTTP status
/// so hosts can translate it into a meaningful response.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingProviderException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// HTTP status reported by the provider, or 503 when the provider could not be reached at all.
    /// </summary>
    public int StatusCode { get; }
}
