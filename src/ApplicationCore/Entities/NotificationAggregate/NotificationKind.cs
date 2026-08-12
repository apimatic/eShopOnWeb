namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Which point in an order's life a notification marks.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    /// <summary>The "how did the delivery go?" message queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3
}
