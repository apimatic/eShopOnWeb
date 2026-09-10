using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure interacting with the billing provider, raised at the integration boundary so callers
/// have a single failure type to handle. <see cref="ProviderStatusCode"/> carries the provider's
/// HTTP status where one is known (null for transport failures or unreadable responses), so the
/// API layer can map caller-fixable failures (4xx) distinctly from provider/our-credential
/// failures (which must not be handed back to the caller as their fault).
/// </summary>
public class BillingException : Exception
{
    public int? ProviderStatusCode { get; }

    public BillingException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
