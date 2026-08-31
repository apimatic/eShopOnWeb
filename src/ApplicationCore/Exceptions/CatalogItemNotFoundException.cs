using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order references catalog items that do not exist.
/// </summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(IEnumerable<int> catalogItemIds)
        : base($"The following catalog items were not found: {string.Join(", ", catalogItemIds)}.")
    {
    }

    public CatalogItemNotFoundException(string message) : base(message)
    {
    }

    public CatalogItemNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
