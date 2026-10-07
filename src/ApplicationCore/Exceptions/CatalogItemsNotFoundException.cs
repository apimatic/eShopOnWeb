using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemsNotFoundException : Exception
{
    public CatalogItemsNotFoundException(IReadOnlyCollection<int> catalogItemIds)
        : base($"Catalog item(s) not found: {string.Join(", ", catalogItemIds)}.")
    {
        CatalogItemIds = catalogItemIds;
    }

    public IReadOnlyCollection<int> CatalogItemIds { get; }
}
