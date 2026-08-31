using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The single failure type the invoicing provider surfaces to the application. It carries the provider's
/// HTTP status when one is known (a provider rejection the caller may be able to act on), and none when the
/// failure was transport-level or otherwise statusless. The message is always caller-safe — the adapter
/// never lets a raw SDK/JSON message reach this type.
/// </summary>
public class InvoicingProviderException : Exception
{
    public InvoicingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status, when the failure carried one; otherwise null.</summary>
    public int? StatusCode { get; }
}
