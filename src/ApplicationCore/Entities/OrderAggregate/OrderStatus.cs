namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its lifecycle. Additive to the original eShop model, which
/// had no notion of an order being dispatched or cancelled.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but not yet dispatched.</summary>
    Placed = 0,

    /// <summary>An operator has marked the order as dispatched / on its way.</summary>
    Dispatched = 1,

    /// <summary>An operator has cancelled the order.</summary>
    Cancelled = 2
}
