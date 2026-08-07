using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a saved card does not exist, or does not belong to the caller. The two cases are
/// deliberately indistinguishable so one shopper can never probe for another's cards.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card with id {paymentMethodId} was found for the current user.")
    {
    }
}
