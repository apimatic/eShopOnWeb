using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The saved payment method does not exist, or does not belong to the caller.
/// </summary>
public class SavedPaymentMethodNotFoundException : Exception
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId)
        : base($"Saved payment method {paymentMethodId} was not found.")
    {
    }
}
