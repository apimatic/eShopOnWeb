namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Additive to the original checkout flow, which
/// simply wrote an order row with no notion of dispatch or cancellation.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
