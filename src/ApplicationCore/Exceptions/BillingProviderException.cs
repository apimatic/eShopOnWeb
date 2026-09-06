using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected or failed a request. Carries enough context to map the failure onto
/// a sensible HTTP status without leaking provider internals or credentials to the caller.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        int? providerStatusCode = null,
        IEnumerable<string>? providerErrors = null,
        bool isCallerError = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors?.ToArray() ?? Array.Empty<string>();
        IsCallerError = isCallerError;
    }

    /// <summary>HTTP status the provider returned, when the failure came back over the wire.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Human-readable validation messages the provider returned, if any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }

    /// <summary>
    /// True when the request was rejected because of what the caller asked for (a 4xx worth
    /// surfacing), false when it was an upstream failure the caller cannot fix.
    /// </summary>
    public bool IsCallerError { get; }
}
