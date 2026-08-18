namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Where an order sits in the additive dispatch/cancel lifecycle this feature adds on top of the
/// existing order model.
/// </summary>
public enum OrderDeliveryState
{
    Placed = 1,
    Dispatched = 2,
    Cancelled = 3
}
