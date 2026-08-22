using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException() : base("One or more catalog items were not found.")
    {
    }
}
