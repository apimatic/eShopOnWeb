using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogOrderException : Exception
{
    public CatalogOrderException(string message) : base(message)
    {
    }
}
