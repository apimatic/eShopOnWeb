using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the invoicing provider integration presents to the rest of the application.
/// It carries the provider's HTTP status (when the provider answered) so the API boundary can map a
/// provider rejection the caller can act on (a 4xx) back to that same status, while a transport failure
/// or an unreadable response — which carry no meaningful caller status — surface as a provider outage.
/// The message is always caller-safe; provider/SDK internal exception text is never surfaced through it.
/// </summary>
public class InvoicingProviderException : Exception
{
    /// <summary>The provider's HTTP status code, when the provider actually answered; otherwise null.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>
    /// True when a non-idempotent write may have reached the provider but the outcome is unknown
    /// (e.g. a transport failure after the request was sent). Such a bill must be reconciled, not blindly retried.
    /// </summary>
    public bool OutcomeUnknown { get; }

    public InvoicingProviderException(string message, int? providerStatusCode = null, Exception? innerException = null, bool outcomeUnknown = false)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeUnknown = outcomeUnknown;
    }
}
