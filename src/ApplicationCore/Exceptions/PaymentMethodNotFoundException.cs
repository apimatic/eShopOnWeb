using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The saved card does not exist, or does not belong to the caller. The same exception covers both
/// so a shopper cannot probe for another shopper's saved cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}.")
    {
    }
}
