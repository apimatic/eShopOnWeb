using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type raised by the subscription-billing layer. It carries the billing
/// provider's HTTP status (when one is known) so the API boundary can map a provider client
/// error (4xx the caller can act on) back to a client error, while transport/parse failures
/// surface as server errors. The message is always caller-safe — no provider or framework
/// exception detail is leaked through it.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int? providerStatusCode = null, Exception? innerException = null, bool outcomeUnknown = false)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    /// <summary>The provider HTTP status that caused this failure, when known.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>True when the provider reported a client error (4xx) the caller could act on.</summary>
    public bool IsClientError => ProviderStatusCode is >= 400 and < 500;

    /// <summary>
    /// True when a write may or may not have taken effect (a transport failure on a POST, or a
    /// re-send blocked to keep the write single). Callers should reconcile against provider state
    /// rather than assume the write did not happen.
    /// </summary>
    public bool OutcomeUnknown { get; }
}
