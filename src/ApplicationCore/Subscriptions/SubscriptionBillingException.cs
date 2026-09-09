using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The single failure type the billing integration surfaces to its callers. The integration layer
/// translates every provider/transport/parse failure into this type at its boundary, carrying a
/// caller-safe <see cref="Exception.Message"/> and a <see cref="SuggestedStatusCode"/> the HTTP
/// layer can return as-is — so no SDK or framework exception detail ever reaches the wire.
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>The HTTP status the API surface should return for this failure.</summary>
    public int SuggestedStatusCode { get; }

    /// <summary>The provider's HTTP status, when one was available (else null).</summary>
    public int? ProviderStatusCode { get; }

    public SubscriptionBillingException(string message, int suggestedStatusCode, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        SuggestedStatusCode = suggestedStatusCode;
        ProviderStatusCode = providerStatusCode;
    }
}
