using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the invoicing provider reports a problem. Carries a suggested HTTP status for the
/// caller: a provider-side refusal of a transition (a 4xx) is surfaced as that refusal, whereas an
/// infrastructure or unexpected error is surfaced as a bad-gateway (502). The message never contains
/// any credential material.
/// </summary>
public class InvoiceProviderException : Exception
{
    /// <summary>The HTTP status the API should return to its caller for this provider outcome.</summary>
    public int SuggestedStatusCode { get; }

    /// <summary>The provider's own status code, when the failure came from a provider response.</summary>
    public int? ProviderStatusCode { get; }

    public InvoiceProviderException(string message, int suggestedStatusCode, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        SuggestedStatusCode = suggestedStatusCode;
        ProviderStatusCode = providerStatusCode;
    }
}
