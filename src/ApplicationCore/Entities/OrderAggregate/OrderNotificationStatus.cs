namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks how far an order has moved for notification purposes. eShopOnWeb historically had no
/// notion of an order being dispatched or cancelled; this is the additive state the SMS
/// notification feature needs so a dispatch/cancel transition can gate its outbound message.
/// </summary>
public enum OrderNotificationStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
