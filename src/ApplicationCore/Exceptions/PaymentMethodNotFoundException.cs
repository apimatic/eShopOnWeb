using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a saved payment method does not exist, or exists but is not owned by the requesting
/// buyer. One exception for both cases so a shopper cannot probe for others' saved cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method found with id {paymentMethodId}.")
    {
    }
}
