using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order references a catalog item id that does not exist. This is a caller error
/// (a bad request), not a missing resource on a route.
/// </summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(int catalogItemId)
        : base($"Catalog item '{catalogItemId}' does not exist.") { }
}
