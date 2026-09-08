using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the external billing provider (Maxio Advanced Billing) fails or returns an
/// unexpected response, after the request could not be satisfied.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
