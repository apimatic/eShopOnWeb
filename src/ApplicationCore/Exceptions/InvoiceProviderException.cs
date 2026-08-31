using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the invoicing integration raises when the billing provider errors, is
/// unreachable, or returns something that cannot be processed. It carries the provider's HTTP status
/// (when there was one) so the API boundary can map it back deliberately, and only ever carries a
/// caller-safe message — never the provider's raw body or an SDK/framework exception string.
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The HTTP status the provider returned, or null for a transport/parse failure with no status.</summary>
    public int? ProviderStatusCode { get; }
}
