using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order is placed with no line items.</summary>
public class OrderMustHaveItemsException : Exception
{
    public OrderMustHaveItemsException() : base("An order must contain at least one item.") { }
}

/// <summary>Thrown when a requested catalog item id does not exist.</summary>
public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(int catalogItemId)
        : base($"Catalog item {catalogItemId} was not found.") { }
}
