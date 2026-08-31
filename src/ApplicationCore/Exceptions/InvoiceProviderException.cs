using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the provider boundary for any failure talking to the payment provider — a non-2xx provider
/// response, a transport failure, or a response body that could not be processed. Carries the provider's
/// HTTP status when there was one (null for transport/parse failures) so the API boundary can map it to a
/// caller-facing status deliberately rather than collapsing every provider failure into one code.
/// The message is always caller-safe; raw provider/exception detail is never carried onto it.
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The provider's HTTP status code, when the provider actually responded; otherwise null.</summary>
    public int? ProviderStatusCode { get; }
}
