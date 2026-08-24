using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. The message is caller-safe;
/// ProviderStatusCode carries the provider's HTTP status when one exists.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public int? ProviderStatusCode { get; }
}
