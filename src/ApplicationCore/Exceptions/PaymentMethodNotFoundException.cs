using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a saved card does not exist, or does not belong to the caller. The two are
/// deliberately indistinguishable so one shopper cannot probe for another's saved cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method found with id {paymentMethodId}")
    {
    }
}
