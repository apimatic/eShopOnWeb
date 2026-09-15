using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a saved card does not exist, or does not belong to the caller.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card with id {paymentMethodId} was found.")
    {
    }
}
