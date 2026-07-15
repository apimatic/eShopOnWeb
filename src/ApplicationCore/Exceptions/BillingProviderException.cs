using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider (Maxio) rejects a request or is unreachable.
/// Thrown only by <see cref="Interfaces.IBillingClient"/> implementations, so callers
/// never need to know which concrete provider is behind the seam.
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
