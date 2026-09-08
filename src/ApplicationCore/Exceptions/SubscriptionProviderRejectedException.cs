using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Maxio rejected the request (a deterministic 4xx provider response). Carries the provider's HTTP
/// status so the API can surface a faithful client error instead of a blanket outage.
/// </summary>
public class SubscriptionProviderRejectedException : Exception
{
    public SubscriptionProviderRejectedException(int providerStatusCode, string message)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>HTTP status returned by the Maxio API.</summary>
    public int ProviderStatusCode { get; }
}
