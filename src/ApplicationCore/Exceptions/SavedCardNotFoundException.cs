using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int savedCardId)
        : base($"Saved payment method {savedCardId} was not found.")
    {
        SavedCardId = savedCardId;
    }

    public int SavedCardId { get; }
}
