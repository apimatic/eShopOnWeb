namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// The lifecycle of an order as far as delivery notifications are concerned.
/// eShop's own <c>Order</c> aggregate carries no status, so this overlay tracks it
/// additively without touching the existing order model.
/// </summary>
public enum OrderDeliveryState
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
