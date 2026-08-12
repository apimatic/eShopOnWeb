namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order lifecycle event a notification corresponds to.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    // The "how did the delivery go?" message that is queued with the provider for a few
    // days after dispatch, and cancelled with the provider if the order is cancelled first.
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
