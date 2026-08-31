using System;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order references catalog items that do not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(params int[] catalogItemIds)
        : base($"No catalog item(s) found with id(s): {string.Join(", ", catalogItemIds)}")
    {
    }
}
