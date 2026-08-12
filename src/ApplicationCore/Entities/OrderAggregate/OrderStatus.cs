namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Added to support dispatch/cancel
/// notifications; new orders start as <see cref="Placed"/>.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
