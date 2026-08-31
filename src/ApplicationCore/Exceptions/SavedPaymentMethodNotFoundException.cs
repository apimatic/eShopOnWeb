using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SavedPaymentMethodNotFoundException : Exception
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId) : base($"Saved payment method with id {paymentMethodId} was not found.")
    {
    }
}
