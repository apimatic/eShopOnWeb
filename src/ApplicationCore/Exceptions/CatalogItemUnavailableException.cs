using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemUnavailableException : Exception
{
    public CatalogItemUnavailableException(string message) : base(message)
    {
    }
}
