using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A billing-provider (Maxio) call failed — connection failure, unexpected response, or a
/// provider-side rejection not covered by a more specific exception. Infrastructure translates every
/// provider-specific error shape into this single type at the <c>IBillingClient</c> boundary, so
/// ApplicationCore and above never need to know about the provider's SDK exception types.
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
