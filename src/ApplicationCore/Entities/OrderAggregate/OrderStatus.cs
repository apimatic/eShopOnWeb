namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its lifecycle. Added to support dispatch / cancel notifications.
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    Dispatched = 1,
    Cancelled = 2
}
