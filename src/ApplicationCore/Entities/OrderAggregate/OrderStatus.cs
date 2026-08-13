namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Orders start <see cref="Placed"/> and can move
/// to <see cref="Dispatched"/> or <see cref="Cancelled"/>. This is an additive concept layered
/// on top of the existing order model to support order-progress notifications.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
