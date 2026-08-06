using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order references a catalog item id that does not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(int catalogItemId)
        : base($"No catalog item found with id {catalogItemId}")
    {
    }
}
