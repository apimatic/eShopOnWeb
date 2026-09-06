using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejected a request or could not be reached. Carries the
/// provider's own error messages so they can be surfaced (or logged) without leaking transport details.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, IEnumerable<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrors = providerErrors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Validation / business messages returned by the billing provider, if any.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    /// <summary>
    /// True when the provider rejected the request as invalid (the caller can fix it),
    /// false when the call failed for transport or provider-side reasons.
    /// </summary>
    public bool IsRequestRejected { get; init; }
}
