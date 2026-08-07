using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a saved card does not exist, or does not belong to the requesting shopper. The two
/// cases are deliberately indistinguishable so one shopper cannot probe for another's saved cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card with id {paymentMethodId} was found for the current user.")
    {
    }
}
