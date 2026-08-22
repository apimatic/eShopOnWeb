namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemNotFoundException : System.Exception
{
    public CatalogItemNotFoundException(int catalogItemId)
        : base($"Catalog item {catalogItemId} was not found.")
    {
    }
}
