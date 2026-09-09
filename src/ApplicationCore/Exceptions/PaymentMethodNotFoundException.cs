using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a saved card does not exist, or does not belong to the requesting shopper.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}.")
    {
    }
}
