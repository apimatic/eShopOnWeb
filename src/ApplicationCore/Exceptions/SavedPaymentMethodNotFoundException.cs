using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a saved card does not exist, or does not belong to the caller. Indistinguishable to
/// the caller so one shopper cannot probe for another's saved-card ids.
/// </summary>
public class SavedPaymentMethodNotFoundException : Exception
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId} for the current shopper.")
    {
    }
}
