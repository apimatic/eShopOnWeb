namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The dispatch/cancel lifecycle this feature adds on top of the base <c>Order</c> aggregate
/// (which itself carries no such notion).
/// </summary>
public enum OrderLifecycleStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
