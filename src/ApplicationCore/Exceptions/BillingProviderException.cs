using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the external billing system of record fails or is misconfigured.
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