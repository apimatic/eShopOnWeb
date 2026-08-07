using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a saved card does not exist or does not belong to the caller.
/// </summary>
public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int paymentMethodId)
        : base($"No saved card with id {paymentMethodId} was found for the current user.")
    {
    }
}
