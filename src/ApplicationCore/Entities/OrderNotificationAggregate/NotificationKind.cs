namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The reason a notification was sent, so an order's message history reads clearly.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    OrderCancelled = 3,
    /// <summary>The "how did the delivery go?" message queued for a few days after dispatch.</summary>
    DeliveryFollowUp = 4
}
