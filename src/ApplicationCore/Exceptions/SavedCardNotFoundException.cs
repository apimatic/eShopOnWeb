using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a saved card does not exist, or does not belong to the calling shopper. Not-owned is
/// deliberately reported as not-found so one shopper cannot probe for another's cards.
/// </summary>
public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int paymentMethodId) : base($"No saved card found with id {paymentMethodId}") { }
}
