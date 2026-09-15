namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Added to support the SMS notification flow
/// (order placed / dispatched / cancelled). Existing checkout flows leave orders in the
/// default <see cref="Placed"/> state.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
