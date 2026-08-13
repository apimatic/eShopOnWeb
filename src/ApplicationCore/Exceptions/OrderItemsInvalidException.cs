using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a placed order references catalog items that do not exist or has invalid quantities.</summary>
public class OrderItemsInvalidException : Exception
{
    public OrderItemsInvalidException(string message) : base(message)
    {
    }

    public static OrderItemsInvalidException ForMissingCatalogItems(IEnumerable<int> ids) =>
        new($"The following catalog items do not exist: {string.Join(", ", ids)}.");
}
