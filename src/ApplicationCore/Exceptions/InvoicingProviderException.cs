using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the invoicing integration raises for anything that goes wrong talking to the
/// Visa/CyberSource provider — an error response, an unreadable body, or a transport failure. Carries the
/// provider's HTTP status (when there was one) so the API boundary can map it back to a caller-facing
/// status deliberately. Its message is always caller-safe: it never carries a secret or an SDK type name.
/// </summary>
public class InvoicingProviderException : Exception
{
    public int? StatusCode { get; }

    public InvoicingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
