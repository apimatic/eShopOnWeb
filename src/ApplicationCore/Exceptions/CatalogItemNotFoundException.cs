using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order references a catalog item id that does not exist.</summary>
public class CatalogItemNotFoundException : Exception, IApiStatusCodeException
{
    public CatalogItemNotFoundException(int catalogItemId)
        : base($"Catalog item {catalogItemId} was not found.")
    {
    }

    public int StatusCode => 400;
    public string? Issue => null;
}
