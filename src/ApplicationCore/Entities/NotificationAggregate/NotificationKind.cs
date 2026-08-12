namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Which order event a notification was raised for.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3
}
