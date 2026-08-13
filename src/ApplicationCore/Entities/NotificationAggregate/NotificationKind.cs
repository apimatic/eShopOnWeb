namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The point in an order's life a notification marks.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    OrderCancelled = 3,
    /// <summary>The "how did the delivery go?" message queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 4
}
