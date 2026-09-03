namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order lifecycle event a notification message corresponds to.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did the delivery go" message queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
