using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order line references a catalog item that does not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public int CatalogItemId { get; }

    public CatalogItemNotFoundException(int catalogItemId)
        : base($"Catalog item {catalogItemId} was not found.")
    {
        CatalogItemId = catalogItemId;
    }
}
