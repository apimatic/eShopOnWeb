using System;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order references catalog items that do not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(params int[] catalogItemIds)
        : base($"Catalog item(s) not found: {string.Join(", ", catalogItemIds)}")
    {
    }

    public CatalogItemNotFoundException(string message) : base(message)
    {
    }

    public CatalogItemNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
