using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps any failure talking to the billing provider (Maxio) — network failure, non-2xx
/// response, or a configured product/component handle that does not resolve.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
