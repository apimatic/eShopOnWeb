using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a saved card does not exist, or does not belong to the requesting shopper. The
/// two cases are indistinguishable so a shopper cannot probe for others' saved cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method with id {paymentMethodId} was found for the current user.")
    {
    }
}
