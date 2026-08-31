using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemsNotFoundException : Exception
{
    public CatalogItemsNotFoundException(string message) : base(message) { }
}
