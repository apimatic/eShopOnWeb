using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when paying with a saved card that does not exist or does not belong to the shopper.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"Saved payment method {paymentMethodId} was not found for this shopper.")
    {
    }
}
