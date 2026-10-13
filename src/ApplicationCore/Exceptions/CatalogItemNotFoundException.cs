using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation references a catalog item id that does not exist.
/// </summary>
public class CatalogItemNotFoundException : Exception
{
    public int CatalogItemId { get; }

    public CatalogItemNotFoundException(int catalogItemId)
        : base($"No catalog item exists with id {catalogItemId}.")
    {
        CatalogItemId = catalogItemId;
    }
}
