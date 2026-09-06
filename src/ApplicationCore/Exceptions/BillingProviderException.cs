using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, or answered in a way this integration cannot act on.
/// Surfaces to callers as a gateway failure - the shopper's request may safely be retried.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? providerStatusCode = null, IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the billing provider, when a response was received.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Error messages reported by the billing provider, when it returned any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
