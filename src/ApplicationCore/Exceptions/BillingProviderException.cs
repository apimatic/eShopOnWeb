using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Wraps any billing-provider failure (transport, or a non-success response) into a typed, provider-agnostic error.</summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
