using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order references catalog item ids that do not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public IReadOnlyCollection<int> CatalogItemIds { get; }

    public CatalogItemNotFoundException(IReadOnlyCollection<int> catalogItemIds)
        : base($"No catalog item(s) found for id(s): {string.Join(", ", catalogItemIds)}.")
    {
        CatalogItemIds = catalogItemIds;
    }

    public CatalogItemNotFoundException(int catalogItemId)
        : this(new[] { catalogItemId })
    {
    }
}
