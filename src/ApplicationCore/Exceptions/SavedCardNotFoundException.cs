using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int savedCardId) : base($"Saved card {savedCardId} was not found.")
    {
        SavedCardId = savedCardId;
    }

    public int SavedCardId { get; }
}
