namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an <see cref="Order"/> as it moves through fulfilment. Added to support
/// order-progress notifications; the existing checkout flow simply leaves an order
/// <see cref="Submitted"/>.
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    Dispatched = 1,
    Cancelled = 2
}
