using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the subscription billing integration raises across its boundary, so
/// callers have one exception to handle instead of the provider SDK's several. Carries the
/// provider's HTTP status (when there was one) so the API layer can map a provider 4xx back to a
/// client 4xx and reserve 5xx for genuine outages / unreadable responses.
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>The provider's HTTP status code, when the failure came from a provider response.</summary>
    public int? ProviderStatusCode { get; }

    public SubscriptionBillingException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>
    /// True when the provider rejected the request with a 4xx the caller could act on
    /// (validation, conflict, not-found) — as opposed to a transport failure or an unknown error.
    /// </summary>
    public bool IsClientError => ProviderStatusCode is >= 400 and < 500;
}
