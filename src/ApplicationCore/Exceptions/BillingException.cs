using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider (Maxio) rejects a request for a reason the shopper can act
/// on — e.g. an unknown plan handle or a plan that requires a payment method we cannot supply.
/// Surfaced to API callers as a 4xx rather than a 500.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
