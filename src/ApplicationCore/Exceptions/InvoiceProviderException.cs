using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the invoicing provider (Visa/CyberSource) surfaces through the
/// <see cref="Interfaces.IInvoicingProvider"/> boundary. It carries the provider's HTTP status when there
/// was one (so a caller-facing status can be mapped back deliberately) and a caller-safe message. It never
/// carries a raw SDK/framework message or any credential material.
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message, HttpStatusCode? providerStatusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The provider's HTTP status, when the provider actually answered; null for a transport failure.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }
}
