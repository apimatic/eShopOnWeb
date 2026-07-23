using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or fails an operation.
/// </summary>
/// <remarks>
/// This is the single typed error the billing seam surfaces for provider-side failures, so callers
/// never have to know which SDK or transport sits behind <c>IBillingClient</c>.
/// </remarks>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message)
        : base(message)
    {
    }

    public BillingProviderException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public BillingProviderException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// HTTP status reported by the provider, or null when the provider surfaced a typed error
    /// payload without a status (the SDK exposes one or the other, never both).
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>True when the provider said the entity does not exist.</summary>
    public bool IsNotFound => StatusCode == 404;
}
