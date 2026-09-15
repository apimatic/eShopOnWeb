namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its lifecycle. Cancellation is terminal and may be reached from either
/// <see cref="Placed"/> or <see cref="Dispatched"/> — cancelling after dispatch is exactly the case
/// that must call off the queued "how did delivery go?" follow-up.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
