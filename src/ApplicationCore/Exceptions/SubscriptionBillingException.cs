using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Boundary exception for failures talking to the Maxio Advanced Billing provider (API errors, transport
/// failures, or unreadable responses). <see cref="ProviderStatusCode"/> carries the provider HTTP status
/// where one is available so the API layer can map caller-fixable 4xx through and everything else to 5xx.
/// The message is caller-safe and never carries a raw provider/SDK exception string.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The provider HTTP status code, when the failure carried one; otherwise null.</summary>
    public int? ProviderStatusCode { get; }
}
