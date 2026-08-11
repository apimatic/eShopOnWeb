using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a saved card does not exist, or exists but is not owned by the caller. As with
/// orders, the two cases are indistinguishable so one shopper cannot probe for another's cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method found with id {paymentMethodId}")
    {
    }
}
