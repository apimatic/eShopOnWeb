using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system answered, but with a failure the shopper cannot fix (provider error, our credentials,
/// configuration, or an unreadable response).
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public int? ProviderStatusCode { get; }
}
