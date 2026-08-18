namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Additive to the original eShopOnWeb model so
/// operators can dispatch or cancel an order and shoppers can be notified as it moves.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed by the shopper (default on creation).</summary>
    Placed = 0,

    /// <summary>An operator has dispatched the order; it is on its way to the shopper.</summary>
    Dispatched = 1,

    /// <summary>An operator has cancelled the order.</summary>
    Cancelled = 2
}
