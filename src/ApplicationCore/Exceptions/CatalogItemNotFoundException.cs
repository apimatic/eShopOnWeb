using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(int catalogItemId) : base($"Catalog item {catalogItemId} was not found.")
    {
    }
}
