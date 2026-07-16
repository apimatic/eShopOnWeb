using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps a failure returned by the billing provider (Maxio) behind the <c>IBillingClient</c> seam,
/// so ApplicationCore/Web/PublicApi never need to know about the provider SDK's own exception types.
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
