using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order is placed referencing a catalog item id that does not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(int catalogItemId)
        : base($"No catalog item exists with id {catalogItemId}.")
    {
    }
}
