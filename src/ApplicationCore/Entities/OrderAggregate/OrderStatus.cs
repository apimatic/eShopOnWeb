namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Orders are additive to the existing
/// catalog/basket/checkout flow; they begin life <see cref="Placed"/> and an operator
/// can move them to <see cref="Dispatched"/> or <see cref="Cancelled"/>.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
