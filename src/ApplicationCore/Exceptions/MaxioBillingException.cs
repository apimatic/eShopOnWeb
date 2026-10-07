using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing integration fails. Carries the
/// provider's HTTP status when one was observed, so callers can map provider
/// 4xx to client-facing 4xx and treat transport failures as infrastructure errors.
/// Messages are always caller-safe (never raw SDK exception text).
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? providerStatusCode = null) : base(message)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>
    /// The HTTP status the billing provider returned, or null when no
    /// provider status was observed (transport failure, unreadable response).
    /// </summary>
    public int? ProviderStatusCode { get; }
}
